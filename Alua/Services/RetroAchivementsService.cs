using Alua.Data;
using CommunityToolkit.Mvvm.DependencyInjection;
using Sachya;
using Serilog;
using Game = Alua.Models.Game;
//I'M STILL IN A DREAM, SNAKE EATER
namespace Alua.Services;

/// <summary>
/// Handles getting data from RetroAchievements.
/// </summary>
public class RetroAchievementsService(string username)
{
    /// <summary>
    /// RetroAchievements API client instance.
    /// </summary>
    private readonly RetroAchievements _apiClient = new(username, AppConfig.RAAPIKey ?? 
            throw new ArgumentException("No RA API Key"), "Alua");

    /// <summary>
    /// Scan games that have been completed by the user on RetroAchievements.
    /// </summary>
    /// <returns>list of Alua game objects</returns>
    public async Task<List<Game>> GetCompletedGamesAsync()
    {
        Ioc.Default.GetRequiredService<AppVM>().GamesFoundMessage = "Preparing to scan your RetroAchievements Library...";
        if (string.IsNullOrWhiteSpace(username))
        {
            return new();
        }

        // Retrieve completed games using the legacy endpoint.
        var completedGames = await _apiClient.GetUserCompletionProgressAsync(username);
        var result = new List<Game>();

        foreach (var completed in completedGames.Results)
        {
            Game game = new()
            {
                Name = completed.Title ?? "Unknown Game",
                Icon = "https://i.retroachievements.org/" + (completed.ImageIcon ?? ""),
                Author = string.Empty,
                Platform = Platforms.RetroAchievements, // Ensure your Platforms enum contains this value.
                PlaytimeMinutes = -1 // RetroAchievements does not provide playtime data.
            };

            try
            {
                // Get detailed user progress (including achievements) for this game.
                var progress = await _apiClient.GetGameInfoAndUserProgressAsync(username, 
                    completed.GameID, includeAwardMetadata: true);
                game.Achievements = new();

                foreach (var kvp in progress.Achievements)
                {
                    // Construct the full URL only if a badge name is available; otherwise fallback to a default icon.
                    string iconUrl = string.IsNullOrWhiteSpace(kvp.Value.BadgeName)
                        ? "default_icon.png"
                        : $"https://i.retroachievements.org/Badge/{kvp.Value.BadgeName}.png";

                    game.Achievements.Add(new()
                    {
                        Title = kvp.Value.Title ?? "Achievement Name Unavailable",
                        Description = kvp.Value.Description ?? "Achievement Description Unavailable",
                        IsUnlocked = kvp.Value.DateEarned.HasValue,
                        Id = kvp.Key,
                        Icon = iconUrl,
                        //IsHidden = kvp.Value.Type == "1"  // ngl i don't think RA has hidden achievements,
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to get achievements for {0}", completed.Title);
                game.Achievements = new();
            }

            // Add game to collection, update progress message
            result.Add(game);
            Ioc.Default.GetRequiredService<AppVM>().LoadingGamesSummary = 
                $"Scanned {game.Name} ( {result.Count} / {completedGames.Results.Count})";

        }

        return result;
    }
}
