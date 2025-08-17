using System.Collections.ObjectModel;
using System.Net;
using System.Text.Json;
using System.Web;
using Alua.Services;
using Alua.Services.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;
using Sachya.PSN;
using Serilog;

namespace Alua.Services.Providers;

public sealed class PSNService : IAchievementProvider<PSNService>
{
    private PSNClient _apiClient = null!;
    private AppVM _appVm = null!;
    private SettingsVM _settingsVm = null!;

    private PSNService() {}

    /// <summary>
    /// Creates a new instance of the PSN Service using PSN SSO token
    /// </summary>
    /// <param name="npssoToken">PSN SSO token</param>
    /// <returns>PSNService</returns>
    public static async Task<PSNService> Create(string npssoToken)
    {
        try
        {
            Log.Information("Creating PSN service with SSO token");
            var settingsVm = Ioc.Default.GetRequiredService<SettingsVM>();
            
            if (string.IsNullOrEmpty(npssoToken))
            {
                Log.Warning("No PSN SSO token provided. User needs to provide NPSSO token.");
                throw new InvalidOperationException("PSN SSO token required. Please provide a valid NPSSO token.");
            }
            
            PSNService psn = new()
            {
                _apiClient = await PSNClient.CreateFromNpsso(npssoToken),
                _appVm = Ioc.Default.GetRequiredService<AppVM>(),
                _settingsVm = settingsVm
            };

            Log.Information("Successfully created PSN service using SSO token");
            return psn;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create PSN service with SSO token");
            throw;
        }
    }

    /// <summary>
    /// Creates a new instance of the PSN Service using username and password
    /// </summary>
    /// <param name="username">PlayStation account email/username</param>
    /// <param name="password">PlayStation account password</param>
    /// <returns>PSNService</returns>
    public static async Task<PSNService> CreateFromCredentials(string username, string password)
    {
        try
        {
            Log.Information("Creating PSN service with username and password");
            var settingsVm = Ioc.Default.GetRequiredService<SettingsVM>();
            
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                Log.Warning("Username or password not provided.");
                throw new InvalidOperationException("Username and password are required.");
            }
            
            // Get NPSSO token from credentials
            var npssoToken = await PSNClient.GetNpssoFromCredentials(username, password);
            Log.Information("Successfully obtained NPSSO token from credentials");
            
            PSNService psn = new()
            {
                _apiClient = await PSNClient.CreateFromNpsso(npssoToken),
                _appVm = Ioc.Default.GetRequiredService<AppVM>(),
                _settingsVm = settingsVm
            };

            Log.Information("Successfully created PSN service using credentials");
            return psn;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create PSN service with credentials");
            throw;
        }
    }

    /// <summary>
    /// Gets the users whole PSN library
    /// </summary>
    /// <returns>Array of Games</returns>
    public async Task<Game[]> GetLibrary()
    {
        try
        {
            Log.Information("Getting PSN library for user");
            
            var trophyTitles = await _apiClient.GetUserTrophyTitlesAsync( "me");
            List<Game> games = new();
            foreach (var game in trophyTitles.TrophyTitles) { games.Add(ConvertToAluaGame(game)); }
            
            
            return games.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get PSN library for user");
            return [];
        }
    }

    /// <summary>
    /// Refreshes the users library and returns recently updated games with trophy data
    /// </summary>
    /// <returns>Array of games with trophy information</returns>
    public async Task<Game[]> RefreshLibrary()
    {
        try
        {
            Log.Information("Refreshing PSN library for user with trophy data");
            
            // Get recent trophy titles (limit to 10 most recent)
            var recentTrophies = await _apiClient.GetUserTrophyTitlesAsync("me", limit: 10);
            
            var gamesWithTrophies = new List<Game>();
            
            // For each recent game, fetch detailed trophy information
            foreach (var title in recentTrophies.TrophyTitles)
            {
                try
                {
                    var gameWithTrophies = await GetGameWithTrophyData(title);
                    gamesWithTrophies.Add(gameWithTrophies);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to get trophy data for {GameName}, adding basic info only", title.TrophyTitleName);
                    // Fallback to basic game info if trophy data fetch fails
                    gamesWithTrophies.Add(ConvertToAluaGame(title));
                }
                
                // Small delay to avoid hitting rate limits
                await Task.Delay(100);
            }
            
            
            return gamesWithTrophies.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to refresh PSN library for user");
            return [];
        }
    }

    /// <summary>
    /// Updates data for a single title with full trophy information
    /// </summary>
    /// <param name="identifier">Game Identifier (NPCommunicationID)</param>
    /// <returns>Game Object with trophy data</returns>
    public async Task<Game> RefreshTitle(string identifier)
    {
        try
        {
            Log.Information("Refreshing PSN title {Identifier} for user with trophy data", identifier);
            
            // Get trophy titles to find the specific game
            var trophyTitles = await _apiClient.GetUserTrophyTitlesAsync("me");
            var targetTitle = trophyTitles.TrophyTitles
                .FirstOrDefault(t => t.NpCommunicationId == identifier);
            
            if (targetTitle == null)
            {
                throw new InvalidOperationException($"Game with identifier {identifier} not found in user's library");
            }

            // Get detailed trophy information for this title
            return await GetGameWithTrophyData(targetTitle);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to refresh PSN title {Identifier} for user", identifier);
            throw;
        }
    }

    /// <summary>
    /// Gets a game with full trophy data populated
    /// </summary>
    /// <param name="title">Trophy title from PSN API</param>
    /// <returns>Game object with trophy achievements</returns>
    private async Task<Game> GetGameWithTrophyData(TrophyTitle title)
    {
        try
        {
            // Determine platform from the trophy title platform field
            var platform = GetPlatformCode(title.TrophyTitlePlatform);
            
            // Get earned trophies for this title
            var earnedTrophies = await _apiClient.GetUserEarnedTrophiesAsync(title.NpCommunicationId, platform, "me");
            
            // Get title trophies (definitions)
            var titleTrophies = await _apiClient.GetTitleTrophiesAsync(title.NpCommunicationId, platform);
            
            return ConvertSingleToAluaGame(title, earnedTrophies, titleTrophies);
        }
        catch (PSNApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            Log.Warning($"Trophy data not found for {title.TrophyTitleName}");
            // Return basic game info if trophy data isn't available (user hasn't played)
            return ConvertToAluaGame(title);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to get detailed trophy data for {GameName}, returning basic info", title.TrophyTitleName);
            // Fallback to basic game info if detailed data fails
            return ConvertToAluaGame(title);
        }
    }

    /// <summary>
    /// Converts a single PSN trophy title to Alua Game object
    /// </summary>
    private Game ConvertToAluaGame(TrophyTitle title)
    {
        return new Game
        {
            Name = title.TrophyTitleName,
            Author = "PlayStation Network", // Use as provider
            Icon = title.TrophyTitleIconUrl,
            LastUpdated = title.LastUpdatedDateTime.DateTime,
            Identifier = title.NpCommunicationId,
            Platform = Platforms.PlayStation,
            PlaytimeMinutes = -1, //Playtime is not available in PSN API
            Achievements = new ()
        };
    }

    /// <summary>
    /// Converts a PSN trophy title with detailed trophy information to Alua Game object
    /// </summary>
    private Game ConvertSingleToAluaGame(TrophyTitle title, UserEarnedTrophiesResponse earnedTrophies, TitleTrophiesResponse titleTrophies)
    {
        var achievements = new ObservableCollection<Achievement>();
        
        // Create a lookup for earned trophies
        var earnedLookup = earnedTrophies.Trophies.ToDictionary(t => t.TrophyId, t => t);
        
        // Convert trophies to achievements
        foreach (var trophy in titleTrophies.Trophies)
        {
            var earnedTrophy = earnedLookup.GetValueOrDefault(trophy.TrophyId);
            
            var achievement = new Achievement
            {
                Id = trophy.TrophyId.ToString(),
                Title = trophy.TrophyName,
                Description = trophy.TrophyDetail,
                IsUnlocked = earnedTrophy?.Earned ?? false,
                UnlockedOn = earnedTrophy?.EarnedDateTime,
                Icon = trophy.TrophyIconUrl,
                IsHidden = trophy.TrophyHidden,
                RarityPercentage = double.TryParse(earnedTrophy?.TrophyEarnedRate, out var rate) ? rate : -1,
            };
            
            achievements.Add(achievement);
        }

        var game = ConvertToAluaGame(title);
        game.Achievements = achievements;
        
        return game;
    }

    /// <summary>
    /// Gets platform code for API calls from platform display name
    /// </summary>
    private string GetPlatformCode(string? platformDisplay)
    {
        if (string.IsNullOrEmpty(platformDisplay))
            return "PS4";
            
        return platformDisplay.ToUpper() switch
        {
            var p when p.Contains("PS5") => "PS5",
            var p when p.Contains("PS4") => "PS4", 
            var p when p.Contains("PS3") => "PS3",
            var p when p.Contains("VITA") => "PSVITA",
            _ => "PS4" // Default fallback
        };
    }
}
