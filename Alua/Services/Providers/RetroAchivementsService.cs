using System;
using CommunityToolkit.Mvvm.DependencyInjection;
using Sachya;
using Serilog;
using Game = Alua.Models.Game;
//I'M STILL IN A DREAM, SNAKE EATER
namespace Alua.Services.Providers;

/// <summary>
/// Handles getting data from RetroAchievements.
/// </summary>
public class RetroAchievementsService : IAchievementProvider<RetroAchievementsService>
{
    /// <summary>
    /// RetroAchievements API client instance.
    /// </summary>
    private RetroAchievements _apiClient = null!;

    private string _username = null!;

    /// <summary>
    /// Creates a new instance of the RetroAchievements Service.
    /// </summary>
    /// <param name="username">Username</param>
    /// <returns>Retro Achievements object.</returns>
    public static Task<RetroAchievementsService> Create(string username)
    {
        return Task.FromResult(new RetroAchievementsService
            {
                _username =  username,
                _apiClient = new( username, AppConfig.RAAPIKey ?? "", "Alua")
            }
        );
    }

    /// <summary>
    /// Scan games that have been completed by the user on RetroAchievements.
    /// </summary>
    /// <returns>list of Alua game objects</returns>
    public async Task<Game[]> GetLibrary()
    {
        if (string.IsNullOrWhiteSpace(_username))
        {
            return [];
        }

        // Retrieve completed games using the legacy endpoint.
        var completedGames = await _apiClient.GetUserCompletionProgressAsync(_username);
        var result = new List<Game>();

        foreach (var completed in completedGames.Results)
        {
            Game game = new()
            {
                Name = completed.Title ?? "Unknown Game",
                Icon = "https://i.retroachievements.org/" + (completed.ImageIcon ?? ""),
                Author = string.Empty,
                Platform = Platforms.RetroAchievements, // Ensure your Platforms enum contains this value.
                PlaytimeMinutes = -1, // RetroAchievements does not provide playtime data.
                Achievements = (await GetAchievements(completed.GameID)).ToObservableCollection(),
                Identifier = "ra-"+completed.GameID,
                LastUpdated = DateTime.UtcNow
            };

            // Add game to collection, update progress message
            result.Add(game);
            Ioc.Default.GetRequiredService<ViewModels.AppVM>().LoadingGamesSummary = $"Scanned {game.Name} ( {result.Count} / {completedGames.Results.Count})";
        }

        return result.ToArray();
    }

    /// <summary>
    /// Gets users last 20 played games
    /// (quicker than full library scan).
    /// </summary>
    /// <returns></returns>
    public async Task<Game[]> RefreshLibrary()
    {
        var response = await _apiClient.GetUserRecentlyPlayedGamesAsync(_username, 0, 20);
        List<Game> result = new List<Game>();
        foreach (var game in response)
        {
            try
            {
                result.Add(new Game
                {
                    Name = game.Title ?? "Unknown Game",
                    Icon = "https://i.retroachievements.org/" + (game.ImageIcon ?? ""),
                    Author = string.Empty,
                    Platform = Platforms.RetroAchievements, // Ensure your Platforms enum contains this value.
                    PlaytimeMinutes = -1, // RetroAchievements does not provide playtime data.
                    Achievements = (await GetAchievements(game.GameID)).ToObservableCollection(),
                    Identifier = "ra-"+game.GameID,
                    LastUpdated = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Cannot refresh game {GameId} in RetroAchievements", game.GameID);
            }

        }
        
        return result.ToArray();
    }

    /// <summary>
    /// Refreshes the title and info for a specific game.
    /// </summary>
    /// <param name="identifier">RetroAchievements game ID.</param>
    /// <returns>Game object with updated title, icon, and achievements.</returns>
    public async Task<Game> RefreshTitle(string identifier)
    {
        int gameId = int.Parse(identifier.Split("-")[1]);
        GameInfoExtended gameInfo = await _apiClient.GetGameExtendedAsync(gameId);
        return new Game
        {
            Name = gameInfo.Title ?? "Unknown Game",
            Icon = "https://i.retroachievements.org/" + (gameInfo.ImageIcon ?? ""),
            Author = string.Empty,
            Platform = Platforms.RetroAchievements,
            PlaytimeMinutes = -1,
            Achievements = (await GetAchievements(gameId)).ToObservableCollection(),
            Identifier = "ra-"+gameId,
            LastUpdated = DateTime.UtcNow
        };
    }

    private async Task<List<Achievement>> GetAchievements(int gameID)
    {
        List<Achievement> achievements = new();

        try
        {
            // Get detailed user progress (including achievements) for this game.
            var progress = await _apiClient.GetGameInfoAndUserProgressAsync(_username, gameID, includeAwardMetadata: true);

            // Get game extended data for unlock statistics in batch
            var gameExtended = await _apiClient.GetGameExtendedAsync(gameID);
            
            foreach (var kvp in progress.Achievements)
            {
                // Construct the full URL only if a badge name is available; otherwise fallback to a default icon.
                string iconUrl = string.IsNullOrWhiteSpace(kvp.Value.BadgeName)
                    ? "default_icon.png"
                    : $"https://i.retroachievements.org/Badge/{kvp.Value.BadgeName}.png";

                var achievement = new Achievement
                {
                    Title = kvp.Value.Title ?? "Achievement Name Unavailable",
                    Description = kvp.Value.Description ?? "Achievement Description Unavailable",
                    IsUnlocked = kvp.Value.DateEarned.HasValue,
                    Id = kvp.Key,
                    Icon = iconUrl
                };
                
                // Calculate unlock percentage using batch data instead of individual API calls
                if (int.TryParse(kvp.Key, out int achievementId))
                {
                    double percentage = CalculateUnlockPercentageFromBatch(achievementId, gameExtended);
                    if (percentage >= 0) // Only set if we got valid data
                    {
                        achievement.RarityPercentage = percentage;
                    }
                }

                achievements.Add(achievement);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get achievements for {0}", gameID);
            achievements = new();
        }

        return achievements;
    }

    /// <summary>
    /// Calculates the unlock percentage from batch game data instead of individual API calls
    /// </summary>
    /// <param name="achievementId">The achievement ID</param>
    /// <param name="gameData">Game extended data containing NumAwarded statistics</param>
    /// <returns>Percentage (0-100) of users who unlocked the achievement, or -1 if failed</returns>
    private double CalculateUnlockPercentageFromBatch(int achievementId, dynamic gameData)
    {
        try
        {
            // Check if gameData has the expected structure
            // The Sachya.GameInfo object might not have an Achievements property
            var gameDataType = gameData.GetType();
            var achievementsProperty = gameDataType.GetProperty("Achievements");
            
            if (achievementsProperty != null)
            {
                var achievements = achievementsProperty.GetValue(gameData);
                if (achievements != null)
                {
                    var achievementKey = achievementId.ToString();
                    var achievementsDict = achievements as IDictionary<string, AchievementCoreInfo>;
                    
                    if (achievementsDict != null && achievementsDict.ContainsKey(achievementKey))
                    {
                        var achievementData = achievementsDict[achievementKey];
                        var achievementDataType = achievementData.GetType();
                        var numAwardedProperty = achievementDataType.GetProperty("NumAwarded");
                        
                        if (numAwardedProperty != null)
                        {
                            int numAwarded = (int)(numAwardedProperty.GetValue(achievementData) ?? 0);
                            
                            // Try to get total players count
                            var totalPlayersProperty = gameDataType.GetProperty("NumDistinctPlayersCasual") ?? 
                                                     gameDataType.GetProperty("NumDistinctPlayers");
                            
                            if (totalPlayersProperty != null)
                            {
                                int totalPlayers = (int)(totalPlayersProperty.GetValue(gameData) ?? 0);
                                
                                if (totalPlayers > 0)
                                {
                                    return (double)numAwarded / totalPlayers * 100.0;
                                }
                            }
                        }
                    }
                }
            }
            
            return -1; // Invalid data or achievement not found
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to calculate unlock percentage for achievement {AchievementId} from batch data", achievementId);
            return -1; // Failed to calculate
        }
    }
}
