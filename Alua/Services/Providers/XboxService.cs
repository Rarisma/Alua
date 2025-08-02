using CommunityToolkit.Mvvm.DependencyInjection;
using Sachya.Clients;
using Sachya.Definitions.Xbox;
using Serilog;
using Game = Alua.Models.Game;
using AluaAchievement = Alua.Models.Achievement;

namespace Alua.Services.Providers;

public sealed class XboxService : IAchievementProvider<XboxService>
{
    private OpenXblApiClient _apiClient = null!;
    private ViewModels.AppVM _appVm = null!;
    private ViewModels.SettingsVM _settingsVm = null!;
    private string _xuid = string.Empty;

    private XboxService() {}

    /// <summary>
    /// Creates a new instance of the Xbox Service
    /// </summary>
    /// <param name="apiKey">OpenXBL API key</param>
    /// <returns>XboxService</returns>
    public static async Task<XboxService> Create(string apiKey)
    {
        try
        {
            XboxService xbox = new()
            {
                _apiClient = new OpenXblApiClient(apiKey),
                _appVm = Ioc.Default.GetRequiredService<ViewModels.AppVM>(),
                _settingsVm = Ioc.Default.GetRequiredService<ViewModels.SettingsVM>()
            };

            // Get the current user's profile to obtain XUID
            var profile = await xbox._apiClient.GetProfileAsync();
            xbox._xuid = profile.ProfileUsers?.FirstOrDefault()?.Id ?? string.Empty;

            if (string.IsNullOrEmpty(xbox._xuid))
            {
                throw new InvalidOperationException("Unable to retrieve user XUID from profile");
            }

            return xbox;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create XboxService");
            throw;
        }
    }

    #region Methods
    /// <summary>
    /// Gets a users whole library
    /// </summary>
    /// <returns>Game array</returns>
    public async Task<Game[]> GetLibrary()
    {
        try
        {
            // Validate required data before making API call
            if (string.IsNullOrEmpty(_xuid))
            {
                Log.Error("XUID is null or empty");
                return [];
            }

            if (_apiClient == null)
            {
                Log.Error("Xbox API client is null");
                return [];
            }

            // Try to get achievements data which includes title information
            var achievementsResponse = await _apiClient.GetAchievementsAsync();
            
            if (achievementsResponse?.Titles != null && achievementsResponse.Titles.Any())
            {
                Log.Information("Using achievements endpoint for Xbox library (found {Count} titles)", achievementsResponse.Titles.Count);
                return await ConvertToAluaFromAchievementsAsync(achievementsResponse.Titles);
            }

            // Fallback to title history if achievements endpoint doesn't work
            Log.Information("Falling back to title history endpoint for Xbox library");
            var titleHistory = await _apiClient.GetTitleHistoryAsync(_xuid);
            
            // Check if the response is valid
            if (titleHistory?.Titles == null)
            {
                Log.Error("Xbox API returned null response or titles list for user {XUID}", _xuid);
                return [];
            }

            return await ConvertToAluaAsync(titleHistory.Titles);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get Xbox library for user {XUID}", _xuid);
            return [];
        }
    }
    
    /// <summary>
    /// Gets recently played games in a users library
    /// </summary>
    /// <returns>Game Array</returns>
    public async Task<Game[]> RefreshLibrary()
    {
        try
        {
            // Validate required data before making API call
            if (string.IsNullOrEmpty(_xuid))
            {
                Log.Error("XUID is null or empty in RefreshLibrary");
                return [];
            }

            if (_apiClient == null)
            {
                Log.Error("Xbox API client is null in RefreshLibrary");
                return [];
            }

            var titleHistory = await _apiClient.GetTitleHistoryAsync(_xuid);

            var skip = _settingsVm.Games.Values!
                .Where(g => g is { HasAchievements: false, Platform: Platforms.Xbox })
                .Select(g => g.Identifier)
                .ToHashSet(StringComparer.Ordinal);

            // Get the 5 most recently played games
            var recentTitles = titleHistory.Titles
                .Where(t => !skip.Contains($"xbox-{t.TitleId}"))
                .Take(5)
                .ToList();

            return await ConvertToAluaAsync(recentTitles);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to refresh Xbox library");
            return [];
        }
    }

    /// <summary>
    /// Updates/Gets data for a title in a users library
    /// </summary>
    /// <param name="identifier"></param>
    /// <returns>Game Object</returns>
    public async Task<Game> RefreshTitle(string identifier)
    {
        try
        {
            string titleId = identifier.Split("xbox-").Last();

            // Get title history for this specific game
            var titleHistory = await _apiClient.GetTitleHistoryAsync(_xuid);
            var title = titleHistory.Titles.FirstOrDefault(t => t.TitleId == titleId);

            if (title == null)
            {
                throw new InvalidOperationException($"Title {titleId} not found in user's library");
            }

            var achievements = await GetAchievementDataAsync(titleId);

            return new Game
            {
                Identifier = $"xbox-{titleId}",
                Name = title.Name,
                Icon = title.DisplayImage,
                Author = string.Empty,
                Platform = Platforms.Xbox,
                PlaytimeMinutes = 0,
                Achievements = achievements.ToObservableCollection(),
                LastUpdated = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to refresh Xbox title {Identifier}", identifier);
            throw;
        }
    }

    #endregion

    #region Helpers
    private async Task<AluaAchievement[]> GetAchievementDataAsync(string titleId)
    {
        try
        {
            // Try to get player-specific achievement data for this title
            var playerTitleData = await _apiClient.GetPlayerTitleAchievementsAsync(_xuid, titleId);
            
            if (playerTitleData?.Titles == null || !playerTitleData.Titles.Any())
            {
                Log.Warning("No achievement data found for Xbox title {TitleId}", titleId);
                return [];
            }

            var titleInfo = playerTitleData.Titles.FirstOrDefault();
            if (titleInfo?.Achievement == null || titleInfo.Achievement.TotalAchievements == 0)
            {
                Log.Information("Xbox title {TitleId} has no achievements", titleId);
                return [];
            }

            // Try to get detailed achievement information
            var detailedAchievements = await TryGetDetailedAchievements(titleId);
            
            if (detailedAchievements != null && detailedAchievements.Any())
            {
                // We have detailed achievement data
                return detailedAchievements.ToArray();
            }

            // Fallback: Create placeholder achievements based on summary data
            Log.Information("Creating placeholder achievements for Xbox title {TitleId} (Total: {Total}, Unlocked: {Unlocked})", 
                titleId, titleInfo.Achievement.TotalAchievements, titleInfo.Achievement.CurrentAchievements);

            var achievements = new List<AluaAchievement>();
            for (int i = 0; i < titleInfo.Achievement.TotalAchievements; i++)
            {
                achievements.Add(new AluaAchievement
                {
                    Title = $"Achievement {i + 1}",
                    Description = $"Progress: {titleInfo.Achievement.CurrentGamerscore}/{titleInfo.Achievement.TotalGamerscore} Gamerscore",
                    Icon = string.Empty,
                    IsUnlocked = i < titleInfo.Achievement.CurrentAchievements,
                    Id = $"xbox-{titleId}-{i}",
                    IsHidden = false,
                    RarityPercentage = null
                });
            }
            
            return achievements.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get achievement data for Xbox game {TitleId}", titleId);
            return [];
        }
    }

    private async Task<List<AluaAchievement>?> TryGetDetailedAchievements(string titleId)
    {
        try
        {
            // Try to get the title's achievement schema
            var titleAchievements = await _apiClient.GetTitleAchievementsAsync(titleId);
            
            if (titleAchievements?.Achievements == null || !titleAchievements.Achievements.Any())
            {
                // Try the multiple titles endpoint as a fallback
                var multipleResponse = await _apiClient.GetMultipleTitleAchievementsAsync(titleId);
                if (multipleResponse?.Achievements != null && multipleResponse.Achievements.Any())
                {
                    // Convert detailed achievements
                    return multipleResponse.Achievements.Select(ach => new AluaAchievement
                    {
                        Title = ach.Name ?? "Unknown Achievement",
                        Description = ach.Description ?? string.Empty,
                        Icon = ach.Icon ?? string.Empty,
                        IsUnlocked = false, // We'll need to match these with player data
                        Id = $"xbox-{titleId}-{ach.Id}",
                        IsHidden = ach.IsSecret,
                        RarityPercentage = null
                    }).ToList();
                }
                return null;
            }

            // Convert title achievements to Alua format
            return titleAchievements.Achievements.Select(ach => new AluaAchievement
            {
                Title = ach.Name ?? "Unknown Achievement",
                Description = ach.Description ?? string.Empty,
                Icon = ach.Icon ?? string.Empty,
                IsUnlocked = false, // Default to false, we need player-specific data
                Id = $"xbox-{titleId}-{ach.Id}",
                IsHidden = false, // TitleAchievement doesn't have IsSecret property
                RarityPercentage = null
            }).ToList();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not get detailed achievements for title {TitleId}", titleId);
            return null;
        }
    }

    /// <summary>
    /// Bridges Xbox data to Alua.
    /// </summary>
    /// <param name="src"></param>
    /// <returns></returns>
    private async Task<Game[]> ConvertToAluaAsync(List<TitleHistory> src)
    {
        List<Game> result = new();
        
        for (int index = 0; index < src.Count; index++)
        {
            var title = src[index];
            
            // Check if the game has achievements from the title history data
            var achievements = new AluaAchievement[0];
            if (title.Achievement != null && title.Achievement.TotalAchievements > 0)
            {
                achievements = await GetAchievementDataAsync(title.TitleId);
            }
            
            result.Add(new Game
            {
                Name = title.Name,
                Icon = title.DisplayImage ?? string.Empty,
                Author = string.Empty,
                Platform = Platforms.Xbox,
                PlaytimeMinutes = 0,
                Identifier = $"xbox-{title.TitleId}",
                Achievements = achievements.ToObservableCollection(),
                LastUpdated = DateTime.UtcNow
            });

            _appVm.LoadingGamesSummary = $"Scanned {title.Name} ({index + 1}/{src.Count})";
        }

        return result.ToArray();
    }

    /// <summary>
    /// Converts achievement titles to Alua format
    /// </summary>
    private async Task<Game[]> ConvertToAluaFromAchievementsAsync(List<Title> src)
    {
        List<Game> result = new();
        
        for (int index = 0; index < src.Count; index++)
        {
            var title = src[index];
            
            // Use achievement data that's already included
            var achievements = new List<AluaAchievement>();
            if (title.Achievement != null && title.Achievement.TotalAchievements > 0)
            {
                // Try to get detailed achievements
                var detailed = await TryGetDetailedAchievements(title.TitleId);
                if (detailed != null && detailed.Any())
                {
                    achievements = detailed;
                }
                else
                {
                    // Create placeholder achievements
                    for (int i = 0; i < title.Achievement.TotalAchievements; i++)
                    {
                        achievements.Add(new AluaAchievement
                        {
                            Title = $"Achievement {i + 1}",
                            Description = $"Xbox Achievement",
                            Icon = string.Empty,
                            IsUnlocked = i < title.Achievement.CurrentAchievements,
                            Id = $"xbox-{title.TitleId}-{i}",
                            IsHidden = false,
                            RarityPercentage = null
                        });
                    }
                }
            }
            
            result.Add(new Game
            {
                Name = title.Name,
                Icon = title.DisplayImage ?? string.Empty,
                Author = string.Empty,
                Platform = Platforms.Xbox,
                PlaytimeMinutes = 0,
                Identifier = $"xbox-{title.TitleId}",
                Achievements = achievements.ToObservableCollection(),
                LastUpdated = DateTime.UtcNow
            });

            _appVm.LoadingGamesSummary = $"Scanned {title.Name} ({index + 1}/{src.Count})";
        }

        return result.ToArray();
    }

    #endregion
}
