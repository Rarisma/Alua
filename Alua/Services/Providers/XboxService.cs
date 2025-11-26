using Alua.Services;
using CommunityToolkit.Mvvm.DependencyInjection;
using Sachya.Clients;
using Sachya.Definitions.Xbox;
using Serilog;
using Game = Alua.Models.Game;
using AluaAchievement = Alua.Models.Achievement;

namespace Alua.Services.Providers;

public sealed class XboxService : IAchievementProvider<XboxService>
{
    internal XboxApiClient _apiClient = null!;
    private ViewModels.AppVM _appVm = null!;
    private ViewModels.SettingsVM _settingsVm = null!;
    private string _xuid = string.Empty;

    private XboxService() {}

    /// <summary>
    /// Creates a new instance of the Xbox Service
    /// </summary>
    /// <param name="microsoftAccessToken">Microsoft OAuth access token with Xbox Live scopes</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>XboxService</returns>
    public static async Task<XboxService> Create(string microsoftAccessToken, CancellationToken cancellationToken = default)
    {
        try
        {
            Log.Information("Creating XboxService instance with Microsoft access token (length: {TokenLength})", microsoftAccessToken?.Length ?? 0);
            
            XboxService xbox = new()
            {
                _apiClient = new XboxApiClient(microsoftAccessToken),
                _appVm = Ioc.Default.GetRequiredService<ViewModels.AppVM>(),
                _settingsVm = Ioc.Default.GetRequiredService<ViewModels.SettingsVM>()
            };
            
            Log.Debug("XboxService initialized, authenticating with Xbox Live");

            // Authenticate with Xbox Live
            bool authenticated = await xbox._apiClient.AuthenticateAsync();
            if (!authenticated)
            {
                Log.Error("Failed to authenticate with Xbox Live");
                throw new InvalidOperationException("Failed to authenticate with Xbox Live");
            }
            
            Log.Debug("Successfully authenticated with Xbox Live");
            
            // Get the XUID from the authenticated client
            xbox._xuid = xbox._apiClient.Xuid ?? string.Empty;
            
            if (string.IsNullOrEmpty(xbox._xuid))
            {
                // Try to get from profile as fallback
                Log.Debug("XUID not available from auth, fetching user profile");
                var profile = await xbox._apiClient.GetProfileAsync();
                
                Log.Debug("Profile response received. ProfileUsers count: {Count}", 
                    profile?.ProfileUsers?.Count ?? 0);
                
                xbox._xuid = profile.ProfileUsers?.FirstOrDefault()?.Id ?? string.Empty;
            }

            if (string.IsNullOrEmpty(xbox._xuid))
            {
                Log.Error("Unable to retrieve user XUID");
                throw new InvalidOperationException("Unable to retrieve user XUID");
            }
            
            Log.Information("XboxService created successfully. XUID: {XUID}", xbox._xuid);

            return xbox;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create XboxService with Microsoft access token (length: {TokenLength})", microsoftAccessToken?.Length ?? 0);
            throw;
        }
    }

    #region Methods
    /// <summary>
    /// Gets a users whole library
    /// </summary>
    /// <returns>Game array</returns>
    public async Task<Game[]> GetLibrary(CancellationToken cancellationToken = default)
    {
        try
        {
            Log.Information("Starting GetLibrary for Xbox user {XUID}", _xuid);
            
            // Validate required data before making API call
            if (string.IsNullOrEmpty(_xuid))
            {
                Log.Error("XUID is null or empty in GetLibrary");
                return [];
            }

            if (_apiClient == null)
            {
                Log.Error("Xbox API client is null in GetLibrary");
                return [];
            }

            // Try to get achievements data which includes title information
            Log.Debug("Attempting to fetch library using achievements endpoint");
            var achievementsResponse = await _apiClient.GetAchievementsAsync();
            
            Log.Debug("Achievements endpoint response: Titles count = {Count}, Response null = {IsNull}",
                achievementsResponse?.Titles?.Count ?? 0, achievementsResponse == null);
            
            if (achievementsResponse?.Titles != null && achievementsResponse.Titles.Any())
            {
                Log.Information("Using achievements endpoint for Xbox library (found {Count} titles)", achievementsResponse.Titles.Count);
                var games = await ConvertToAluaFromAchievementsAsync(achievementsResponse.Titles);
                Log.Information("Successfully converted {Count} games from achievements endpoint", games.Length);
                return games;
            }

            // Fallback to title history if achievements endpoint doesn't work
            Log.Information("Achievements endpoint returned no data, falling back to title history endpoint");
            var titleHistory = await _apiClient.GetTitleHistoryAsync(_xuid);
            
            Log.Debug("Title history response: Titles count = {Count}, Response null = {IsNull}",
                titleHistory?.Titles?.Count ?? 0, titleHistory == null);
            
            // Check if the response is valid
            if (titleHistory?.Titles == null)
            {
                Log.Error("Xbox API returned null response or titles list for user {XUID}. Full response: {@Response}", 
                    _xuid, titleHistory);
                return [];
            }

            Log.Information("Converting {Count} titles from title history", titleHistory.Titles.Count);
            var result = await ConvertToAluaAsync(titleHistory.Titles);
            Log.Information("GetLibrary completed successfully, returning {Count} games", result.Length);
            return result;
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
    public async Task<Game[]> RefreshLibrary(CancellationToken cancellationToken = default)
    {
        try
        {
            Log.Information("Starting RefreshLibrary for Xbox user {XUID}", _xuid);
            
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

            Log.Debug("Fetching title history for refresh");
            var titleHistory = await _apiClient.GetTitleHistoryAsync(_xuid);
            
            Log.Debug("Title history response: {Count} titles found", titleHistory?.Titles?.Count ?? 0);

            var skip = _settingsVm.Games.Values!
                .Where(g => g is { HasAchievements: false, Platform: Platforms.Xbox })
                .Select(g => g.Identifier)
                .ToHashSet(StringComparer.Ordinal);
            
            Log.Debug("Skipping {Count} games without achievements", skip.Count);

            // Get the 5 most recently played games
            var recentTitles = titleHistory.Titles
                .Where(t => !skip.Contains($"xbox-{t.TitleId}"))
                .Take(5)
                .ToList();
            
            Log.Information("Found {Count} recent titles to refresh after filtering", recentTitles.Count);
            foreach (var title in recentTitles)
            {
                Log.Debug("Will refresh: {TitleName} (ID: {TitleId})", title.Name, title.TitleId);
            }

            var result = await ConvertToAluaAsync(recentTitles);
            Log.Information("RefreshLibrary completed, returning {Count} games", result.Length);
            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to refresh Xbox library for user {XUID}", _xuid);
            return [];
        }
    }

    /// <summary>
    /// Updates/Gets data for a title in a users library
    /// </summary>
    /// <param name="identifier"></param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Game Object</returns>
    public async Task<Game> RefreshTitle(string identifier, CancellationToken cancellationToken = default)
    {
        try
        {
            Log.Information("Starting RefreshTitle for identifier: {Identifier}", identifier);
            string titleId = identifier.Split("xbox-").Last();
            Log.Debug("Extracted titleId: {TitleId} from identifier: {Identifier}", titleId, identifier);

            // Get title history for this specific game
            Log.Debug("Fetching title history to find specific title");
            var titleHistory = await _apiClient.GetTitleHistoryAsync(_xuid);
            
            Log.Debug("Title history contains {Count} titles", titleHistory?.Titles?.Count ?? 0);
            var title = titleHistory.Titles.FirstOrDefault(t => t.TitleId == titleId);

            if (title == null)
            {
                Log.Error("Title {TitleId} not found in user's library. Available titles: {@TitleIds}", 
                    titleId, titleHistory?.Titles?.Select(t => t.TitleId));
                throw new InvalidOperationException($"Title {titleId} not found in user's library");
            }

            Log.Information("Found title: {TitleName} (ID: {TitleId})", title.Name, titleId);
            Log.Debug("Title details - HasAchievements: {HasAch}, TotalGamerscore: {Gamerscore}",
                title.Achievement != null, title.Achievement?.TotalGamerscore ?? 0);
            
            var achievements = await GetAchievementDataAsync(titleId);
            Log.Information("Retrieved {Count} achievements for title {TitleId}", achievements.Length, titleId);

            var game = new Game
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
            
            Log.Information("RefreshTitle completed successfully for {TitleName}", title.Name);
            return game;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to refresh Xbox title {Identifier}", identifier);
            throw;
        }
    }

    #endregion

    #region Helpers
    
    /// <summary>
    /// Determines if a title ID belongs to an Xbox 360 game
    /// </summary>
    /// <param name="titleId">The title ID to check</param>
    /// <returns>True if it's an Xbox 360 title</returns>
    private bool IsXbox360Title(string titleId)
    {
        // Xbox 360 title IDs are typically 8 hex characters (e.g., "584111F7" for Banjo-Kazooie)
        // Xbox One/Series titles are typically 9-10 digit numbers
        
        if (string.IsNullOrEmpty(titleId))
            return false;
        
        // Check if it's a hex string (Xbox 360 format)
        bool isHex = titleId.Length == 8 && 
                     titleId.All(c => (c >= '0' && c <= '9') || 
                                     (c >= 'A' && c <= 'F') || 
                                     (c >= 'a' && c <= 'f'));
        
        if (isHex)
        {
            Log.Debug("Title {TitleId} identified as Xbox 360 (hex format)", titleId);
            return true;
        }
        
        // Some Xbox 360 titles might use numeric IDs in certain ranges
        if (long.TryParse(titleId, out long numericId))
        {
            // Xbox 360 numeric IDs are typically in specific ranges
            bool is360Range = numericId < 1000000000; // Xbox One/Series titles typically start at 1000000000+
            if (is360Range)
            {
                Log.Debug("Title {TitleId} identified as Xbox 360 (numeric range)", titleId);
                return true;
            }
        }
        
        Log.Debug("Title {TitleId} identified as Xbox One/Series", titleId);
        return false;
    }
    
    private async Task<AluaAchievement[]> GetAchievementDataAsync(string titleId)
    {
        try
        {
            Log.Information("Getting achievement data for title {TitleId}", titleId);
            
            // Check if this is an Xbox 360 title (titleId typically starts with certain patterns for 360 games)
            bool isXbox360Title = IsXbox360Title(titleId);
            
            if (isXbox360Title)
            {
                Log.Information("Detected Xbox 360 title {TitleId}, using Xbox 360 specific endpoint", titleId);
                
                // Try Xbox 360 specific endpoint first
                var xbox360Response = await _apiClient.GetXbox360AchievementsAsync(_xuid, titleId);
                
                Log.Debug("Xbox 360 achievements response: Titles count = {Count}, Response null = {IsNull}",
                    xbox360Response?.Titles?.Count ?? 0, xbox360Response == null);
                
                if (xbox360Response?.Titles != null && xbox360Response.Titles.Any())
                {
                    var titleInfo = xbox360Response.Titles.FirstOrDefault();
                    if (titleInfo?.Achievement != null && titleInfo.Achievement.TotalAchievements > 0)
                    {
                        // Try to get detailed achievements for Xbox 360 title
                        var detailedAchievements = await TryGetDetailedAchievementsWithProgress(titleId, titleInfo);
                        if (detailedAchievements != null && detailedAchievements.Any())
                        {
                            Log.Information("Successfully retrieved {Count} Xbox 360 achievements for title {TitleId}", 
                                detailedAchievements.Count, titleId);
                            return detailedAchievements.ToArray();
                        }
                    }
                }
                
                Log.Warning("Xbox 360 endpoint didn't return detailed achievements for {TitleId}, falling back to standard endpoints", titleId);
            }
            
            // First, try to get player-specific achievement data with unlock status
            Log.Debug("Attempting to fetch player achievements for XUID: {XUID}, TitleId: {TitleId}", _xuid, titleId);
            var playerAchievements = await _apiClient.GetPlayerAchievementsAsync(_xuid, titleId);
            
            Log.Debug("Player achievements response: Titles count = {Count}, Response null = {IsNull}",
                playerAchievements?.Titles?.Count ?? 0, playerAchievements == null);
            
            if (playerAchievements?.Titles != null && playerAchievements.Titles.Any())
            {
                var titleInfo = playerAchievements.Titles.FirstOrDefault();
                Log.Debug("Title info from player achievements: Name = {Name}, TotalAchievements = {Total}, CurrentAchievements = {Current}",
                    titleInfo?.Name, titleInfo?.Achievement?.TotalAchievements ?? 0, titleInfo?.Achievement?.CurrentAchievements ?? 0);
                    
                if (titleInfo?.Achievement != null && titleInfo.Achievement.TotalAchievements > 0)
                {
                    // Try to get detailed achievement schema for this title
                    Log.Debug("Attempting to get detailed achievements with progress");
                    var detailedAchievements = await TryGetDetailedAchievementsWithProgress(titleId, titleInfo);
                    if (detailedAchievements != null && detailedAchievements.Any())
                    {
                        Log.Information("Successfully retrieved {Count} detailed achievements for title {TitleId}", 
                            detailedAchievements.Count, titleId);
                        return detailedAchievements.ToArray();
                    }
                    Log.Debug("Detailed achievements returned null or empty");
                }
            }
            
            // Fallback: Try the player/title endpoint
            Log.Information("Falling back to player/title achievements endpoint for title {TitleId}", titleId);
            var playerTitleData = await _apiClient.GetPlayerTitleAchievementsAsync(_xuid, titleId);
            
            Log.Debug("Player title data response: Titles count = {Count}, Response null = {IsNull}",
                playerTitleData?.Titles?.Count ?? 0, playerTitleData == null);
            
            if (playerTitleData?.Titles == null || !playerTitleData.Titles.Any())
            {
                Log.Warning("No achievement data found for Xbox title {TitleId}. Response: {@Response}", 
                    titleId, playerTitleData);
                return [];
            }

            var titleData = playerTitleData.Titles.FirstOrDefault();
            Log.Debug("Title data from player/title endpoint: Name = {Name}, TotalAchievements = {Total}, CurrentAchievements = {Current}",
                titleData?.Name, titleData?.Achievement?.TotalAchievements ?? 0, titleData?.Achievement?.CurrentAchievements ?? 0);
                
            if (titleData?.Achievement == null || titleData.Achievement.TotalAchievements == 0)
            {
                Log.Information("Xbox title {TitleId} has no achievements according to API", titleId);
                return [];
            }

            // Try once more to get detailed achievements with the title data
            Log.Debug("Second attempt to get detailed achievements with title data");
            var achievements = await TryGetDetailedAchievementsWithProgress(titleId, titleData);
            if (achievements != null && achievements.Any())
            {
                Log.Information("Successfully retrieved {Count} achievements on second attempt for title {TitleId}", 
                    achievements.Count, titleId);
                return achievements.ToArray();
            }

            // Final fallback: Create placeholder achievements
            Log.Warning("Creating {Count} placeholder achievements for Xbox title {TitleId} (Unlocked: {Unlocked}/{Total}, Gamerscore: {Current}/{Total}GS)", 
                titleData.Achievement.TotalAchievements, titleId, 
                titleData.Achievement.CurrentAchievements, titleData.Achievement.TotalAchievements,
                titleData.Achievement.CurrentGamerscore, titleData.Achievement.TotalGamerscore);

            var placeholderAchievements = new List<AluaAchievement>();
            for (int i = 0; i < titleData.Achievement.TotalAchievements; i++)
            {
                placeholderAchievements.Add(new AluaAchievement
                {
                    Title = $"Achievement {i + 1}",
                    Description = $"Progress: {titleData.Achievement.CurrentGamerscore}/{titleData.Achievement.TotalGamerscore} Gamerscore",
                    Icon = string.Empty,
                    IsUnlocked = i < titleData.Achievement.CurrentAchievements,
                    Id = $"xbox-{titleId}-{i}",
                    IsHidden = false,
                    RarityPercentage = null
                });
            }
            
            Log.Debug("Created {Count} placeholder achievements", placeholderAchievements.Count);
            return placeholderAchievements.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get achievement data for Xbox game {TitleId}", titleId);
            return [];
        }
    }

    private async Task<List<AluaAchievement>?> TryGetDetailedAchievementsWithProgress(string titleId, Title titleInfo)
    {
        try
        {
            Log.Debug("TryGetDetailedAchievementsWithProgress starting for title {TitleId}", titleId);
            
            // Check if this is an Xbox 360 title - they need special handling
            bool isXbox360Title = IsXbox360Title(titleId);
            if (isXbox360Title)
            {
                Log.Information("Title {TitleId} is Xbox 360, using title achievements endpoint directly", titleId);
                // For Xbox 360 titles, skip straight to the title achievements endpoint
                // as the multiple titles endpoint often doesn't work for them
                var xbox360Achievements = await _apiClient.GetTitleAchievementsAsync(titleId);
                
                if (xbox360Achievements?.Achievements != null && xbox360Achievements.Achievements.Any())
                {
                    Log.Information("Found {Count} Xbox 360 achievements for title {TitleId}", 
                        xbox360Achievements.Achievements.Count, titleId);
                    
                    // Log details of first achievement to debug
                    var firstAch = xbox360Achievements.Achievements.First();
                    Log.Information("Xbox 360 Achievement sample - Name: '{Name}', Description: '{Desc}', ID: '{Id}', Icon: '{Icon}', Gamerscore: '{GS}'",
                        firstAch.Name ?? "null", firstAch.Description ?? "null", firstAch.Id ?? "null", 
                        firstAch.Icon ?? "null", firstAch.Gamerscore ?? "null");
                    
                    var result = new List<AluaAchievement>();
                    var unlockedCount = titleInfo.Achievement?.CurrentAchievements ?? 0;
                    
                    for (int i = 0; i < xbox360Achievements.Achievements.Count; i++)
                    {
                        var ach = xbox360Achievements.Achievements[i];
                        // For Xbox 360, we have to approximate which achievements are unlocked
                        var isUnlocked = i < unlockedCount;
                        
                        result.Add(new AluaAchievement
                        {
                            Title = ach.Name ?? $"Achievement {i + 1}",
                            Description = ach.Description ?? string.Empty,
                            Icon = ach.Icon ?? string.Empty,
                            IsUnlocked = isUnlocked,
                            Id = $"xbox-{titleId}-{ach.Id}",
                            IsHidden = false,
                            RarityPercentage = null
                        });
                    }
                    
                    Log.Information("Successfully created {Count} Xbox 360 achievements for title {TitleId}",
                        result.Count, titleId);
                    return result;
                }
            }
            
            // First try to get the multiple titles endpoint which returns full Achievement objects
            Log.Debug("Attempting GetMultipleTitleAchievementsAsync for title {TitleId}", titleId);
            var multipleResponse = await _apiClient.GetMultipleTitleAchievementsAsync(titleId);
            
            Log.Debug("Multiple title achievements response: Achievements count = {Count}, Response null = {IsNull}",
                multipleResponse?.Achievements?.Count ?? 0, multipleResponse == null);
            
            if (multipleResponse?.Achievements != null && multipleResponse.Achievements.Any())
            {
                Log.Information("Processing {Count} achievements from multiple titles endpoint for title {TitleId}", 
                    multipleResponse.Achievements.Count, titleId);
                    
                // This endpoint returns Achievement objects with Progressions
                var result = new List<AluaAchievement>();
                
                foreach (var ach in multipleResponse.Achievements)
                {
                    // Check if achievement is unlocked by looking at Progressions
                    bool isUnlocked = false;
                    if (ach.Progressions != null && ach.Progressions.Any())
                    {
                        // If there's a progression with a TimeUnlocked, the achievement is unlocked
                        isUnlocked = ach.Progressions.Any(p => p.TimeUnlocked != default(DateTime));
                        Log.Verbose("Achievement {AchId} has {Count} progressions, unlocked = {IsUnlocked}",
                            ach.Id, ach.Progressions.Count, isUnlocked);
                    }
                    
                    result.Add(new AluaAchievement
                    {
                        Title = ach.Name ?? "Unknown Achievement",
                        Description = ach.Description ?? string.Empty,
                        Icon = ach.Icon ?? string.Empty,
                        IsUnlocked = isUnlocked,
                        Id = $"xbox-{titleId}-{ach.Id}",
                        IsHidden = ach.IsSecret,
                        RarityPercentage = null
                    });
                }
                
                // Verify our unlock count matches what the API reported
                var actualUnlocked = result.Count(a => a.IsUnlocked);
                var expectedUnlocked = titleInfo.Achievement?.CurrentAchievements ?? 0;
                
                Log.Debug("Achievement unlock verification: Actual = {Actual}, Expected = {Expected}",
                    actualUnlocked, expectedUnlocked);
                
                if (actualUnlocked != expectedUnlocked)
                {
                    Log.Warning("Xbox unlock status mismatch for title {TitleId}. Found {Actual} unlocked, expected {Expected}", 
                        titleId, actualUnlocked, expectedUnlocked);
                }
                
                Log.Information("Successfully processed {Count} achievements with {Unlocked} unlocked for title {TitleId}",
                    result.Count, actualUnlocked, titleId);
                return result;
            }
            
            // Fallback: Try the title achievements endpoint (returns TitleAchievement objects without progress)
            Log.Information("Falling back to title achievements endpoint for title {TitleId}", titleId);
            var titleAchievements = await _apiClient.GetTitleAchievementsAsync(titleId);
            
            Log.Debug("Title achievements response: Achievements count = {Count}, Response null = {IsNull}",
                titleAchievements?.Achievements?.Count ?? 0, titleAchievements == null);
            
            if (titleAchievements?.Achievements != null && titleAchievements.Achievements.Any())
            {
                // This endpoint returns TitleAchievement objects without individual progress
                // We'll have to approximate which achievements are unlocked
                var unlockedCount = titleInfo.Achievement?.CurrentAchievements ?? 0;
                var result = new List<AluaAchievement>();
                
                Log.Warning("Using approximate unlock status for {Count} achievements. Title endpoint doesn't provide individual progress. Will mark first {Unlocked} as unlocked.",
                    titleAchievements.Achievements.Count, unlockedCount);
                
                for (int i = 0; i < titleAchievements.Achievements.Count; i++)
                {
                    var ach = titleAchievements.Achievements[i];
                    var isUnlocked = i < unlockedCount;
                    
                    Log.Verbose("Creating achievement {Index}: {Name}, Unlocked = {IsUnlocked}",
                        i, ach.Name ?? $"Achievement {i + 1}", isUnlocked);
                        
                    result.Add(new AluaAchievement
                    {
                        Title = ach.Name ?? $"Achievement {i + 1}",
                        Description = ach.Description ?? string.Empty,
                        Icon = ach.Icon ?? string.Empty,
                        // Approximate: mark first N achievements as unlocked
                        IsUnlocked = isUnlocked,
                        Id = $"xbox-{titleId}-{ach.Id}",
                        IsHidden = false, // TitleAchievement doesn't have IsSecret
                        RarityPercentage = null
                    });
                }
                
                Log.Information("Created {Count} achievements with approximate unlock status for title {TitleId}",
                    result.Count, titleId);
                return result;
            }
            
            Log.Debug("No achievements found from any endpoint for title {TitleId}", titleId);
            return null;
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
        Log.Information("Starting ConvertToAluaAsync with {Count} titles", src.Count);
        List<Game> result = new();
        
        // Filter to only include games with achievements
        var gamesWithAchievements = src.Where(t => 
            t.Achievement != null && 
            t.Achievement.TotalGamerscore > 0).ToList();
        
        Log.Information("Filtered to {Count} games with achievements (from {Total} total)", 
            gamesWithAchievements.Count, src.Count);
        
        for (int index = 0; index < gamesWithAchievements.Count; index++)
        {
            var title = gamesWithAchievements[index];
            
            Log.Debug("Converting title {Index}/{Total}: {Name} (ID: {TitleId}, Gamerscore: {GS})",
                index + 1, gamesWithAchievements.Count, title.Name, title.TitleId, 
                title.Achievement?.TotalGamerscore ?? 0);
            
            // Get achievement data for this title
            var achievements = await GetAchievementDataAsync(title.TitleId);
            
            Log.Debug("Retrieved {Count} achievements for {Name}", achievements.Length, title.Name);
            
            var game = new Game
            {
                Name = title.Name,
                Icon = title.DisplayImage ?? string.Empty,
                Author = string.Empty,
                Platform = Platforms.Xbox,
                PlaytimeMinutes = 0,
                Identifier = $"xbox-{title.TitleId}",
                Achievements = achievements.ToObservableCollection(),
                LastUpdated = DateTime.UtcNow
            };
            
            result.Add(game);

            _appVm.LoadingGamesSummary = $"Scanned {title.Name} ({index + 1}/{gamesWithAchievements.Count})";
            Log.Verbose("Updated loading summary: {Summary}", _appVm.LoadingGamesSummary);
        }
        
        Log.Information("ConvertToAluaAsync completed. Converted {Count} games", result.Count);
        return result.ToArray();
    }

    /// <summary>
    /// Converts achievement titles to Alua format
    /// </summary>
    private async Task<Game[]> ConvertToAluaFromAchievementsAsync(List<Title> src)
    {
        Log.Information("Starting ConvertToAluaFromAchievementsAsync with {Count} titles", src.Count);
        List<Game> result = new();
        
        // Filter to only include games with achievements
        var gamesWithAchievements = src.Where(t => 
            t.Achievement != null && 
            t.Achievement.TotalGamerscore > 0).ToList();
        
        Log.Information("Filtered to {Count} games with achievements (from {Total} total)", 
            gamesWithAchievements.Count, src.Count);
        
        for (int index = 0; index < gamesWithAchievements.Count; index++)
        {
            var title = gamesWithAchievements[index];
            
            Log.Debug("Converting title {Index}/{Total}: {Name} (ID: {TitleId}, Total Achievements: {TotalAch}, Current: {CurrentAch})",
                index + 1, gamesWithAchievements.Count, title.Name, title.TitleId, 
                title.Achievement?.TotalAchievements ?? 0, title.Achievement?.CurrentAchievements ?? 0);
            
            // Get achievement data for this title
            var achievements = new List<AluaAchievement>();
            
            // Try to get detailed achievements
            Log.Debug("Attempting to get detailed achievements for {Name}", title.Name);
            var detailed = await TryGetDetailedAchievementsWithProgress(title.TitleId, title);
            if (detailed != null && detailed.Any())
            {
                achievements = detailed;
                Log.Information("Using {Count} detailed achievements for {Name}", detailed.Count, title.Name);
            }
            else
            {
                // Create placeholder achievements
                Log.Warning("Creating {Count} placeholder achievements for {Name} (no detailed data available)",
                    title.Achievement.TotalAchievements, title.Name);
                    
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
                Log.Debug("Created {Count} placeholder achievements", achievements.Count);
            }
            
            var game = new Game
            {
                Name = title.Name,
                Icon = title.DisplayImage ?? string.Empty,
                Author = string.Empty,
                Platform = Platforms.Xbox,
                PlaytimeMinutes = 0,
                Identifier = $"xbox-{title.TitleId}",
                Achievements = achievements.ToObservableCollection(),
                LastUpdated = DateTime.UtcNow
            };
            
            result.Add(game);

            _appVm.LoadingGamesSummary = $"Scanned {title.Name} ({index + 1}/{gamesWithAchievements.Count})";
            Log.Verbose("Updated loading summary: {Summary}", _appVm.LoadingGamesSummary);
        }
        
        Log.Information("ConvertToAluaFromAchievementsAsync completed. Converted {Count} games", result.Count);
        return result.ToArray();
    }

    #endregion
}
