using System;
using Alua.Models;
using Alua.Services;
using CommunityToolkit.Mvvm.DependencyInjection;
using Sachya;
using Sachya.Clients;
using Sachya.Definitions.RetroAchievements;
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
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Retro Achievements object.</returns>
    public static Task<RetroAchievementsService> Create(string username, CancellationToken cancellationToken = default)
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
    public async Task<Game[]> GetLibrary(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_username))
        {
            return [];
        }

        try
        {
            // Retrieve completed games using the legacy endpoint.
            var completedGames = await _apiClient.GetUserCompletionProgressAsync(_username, cancellationToken: cancellationToken);
            var appVm = Ioc.Default.GetRequiredService<ViewModels.AppVM>();

            if (completedGames.Results.Count == 0)
                return [];

            // Use RateLimitedExecutor for parallel processing with concurrency limit of 4
            using var executor = new RateLimitedExecutor(4, "RALibrary");

            var games = await executor.ExecuteAllAsync(
                completedGames.Results,
                async (completed, ct) =>
                {
                    ct.ThrowIfCancellationRequested();

                    var (achievements, playtime, parentGameID) = await GetAchievements(completed.GameID, ct);
                    return new Game
                    {
                        Name = completed.Title ?? "Unknown Game",
                        Icon = BuildIconUrl(completed.ImageIcon),
                        Author = string.Empty,
                        Platform = Platforms.RetroAchievements,
                        PlaytimeMinutes = playtime,
                        Achievements = achievements.ToObservableCollection(),
                        Identifier = ProviderIds.Retro + completed.GameID,
                        ParentIdentifier = ParentIdentifierFor(parentGameID),
                        LastUpdated = DateTime.UtcNow,
                        LastPlayed = completed.MostRecentAwardedDate?.UtcDateTime
                    };
                },
                (current, total) => appVm.LoadingGamesSummary = $"Scanned RetroAchievements ({current}/{total})",
                cancellationToken
            );

            return games;
        }
        catch (Exception ex)
        {
            // Match the other providers' GetLibrary contract: a failed scan degrades to an
            // empty library rather than throwing out of the bulk scan.
            Log.Error(ex, "Failed to get RetroAchievements library");
            return [];
        }
    }

    /// <summary>
    /// Gets users last 20 played games
    /// (quicker than full library scan).
    /// </summary>
    /// <returns></returns>
    public async Task<Game[]> RefreshLibrary(CancellationToken cancellationToken = default)
    {
        var response = await _apiClient.GetUserRecentlyPlayedGamesAsync(_username, 0, 5);

        if (response.Count == 0)
            return [];

        // Use RateLimitedExecutor for parallel processing with concurrency limit of 4
        using var executor = new RateLimitedExecutor(4, "RARefresh");

        var games = await executor.ExecuteAllWithNullableAsync(
            response,
            async (game, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var (achievements, playtime, parentGameID) = await GetAchievements(game.GameID, ct);
                    return new Game
                    {
                        Name = game.Title ?? "Unknown Game",
                        // The recently-played payload's ImageIcon is intermittently empty (right after a
                        // session). BuildIconUrl then yields "" instead of the old unloadable bare-domain
                        // URL, and SettingsVM.AddOrUpdateGame keeps the previously-scanned icon rather than
                        // wiping it — so a refresh can never blank out an icon you already have.
                        Icon = BuildIconUrl(game.ImageIcon),
                        Author = string.Empty,
                        Platform = Platforms.RetroAchievements,
                        PlaytimeMinutes = playtime,
                        Achievements = achievements.ToObservableCollection(),
                        Identifier = ProviderIds.Retro + game.GameID,
                        ParentIdentifier = ParentIdentifierFor(parentGameID),
                        LastUpdated = DateTime.UtcNow,
                        LastPlayed = game.LastPlayed
                    };
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Cannot refresh game {GameId} in RetroAchievements", game.GameID);
                    return null;
                }
            },
            cancellationToken: cancellationToken
        );

        return games.Where(g => g != null).Cast<Game>().ToArray();
    }

    /// <summary>
    /// Refreshes the title and info for a specific game.
    /// Uses the game extended data (already fetched inside GetAchievements) so there is no
    /// duplicate API call. LastPlayed is derived from the Max(UnlockedOn) across achievements
    /// rather than making an additional recently-played fetch.
    /// </summary>
    /// <param name="identifier">RetroAchievements game ID.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Game object with updated title, icon, and achievements.</returns>
    public async Task<Game> RefreshTitle(string identifier, CancellationToken cancellationToken = default)
    {
        int gameId = int.Parse(identifier.Split("-")[1]);

        // GetAchievements internally fetches both GameInfoAndUserProgress and GameExtended
        // concurrently — no need to call GetGameExtendedAsync again here.
        var (achievementsList, playtime, gameTitle, gameIcon, parentGameID) = await GetAchievementsWithMetadata(gameId, cancellationToken);

        // GetAchievementsWithMetadata swallows API errors and returns an empty/degraded result
        // (good for the bulk library scan, where one bad game shouldn't fail everything). For a
        // single-title refresh that would silently overwrite the persisted game with an empty
        // "Unknown Game" placeholder — fail loudly so the caller keeps the existing data.
        if (gameTitle == null && achievementsList.Count == 0)
            throw new InvalidOperationException($"Failed to refresh RetroAchievements title {gameId}");

        var achievements = achievementsList.ToObservableCollection();

        // Derive LastPlayed from the most recent achievement unlock — avoids an extra
        // GetUserRecentlyPlayedGamesAsync(50) call just for one date.
        DateTime? lastPlayed = achievements
            .Where(a => a.UnlockedOn.HasValue)
            .Select(a => a.UnlockedOn!.Value)
            .DefaultIfEmpty()
            .Max() is var maxUnlock && maxUnlock != DateTime.MinValue
            ? maxUnlock
            : (DateTime?)null;

        var game = new Game
        {
            Name = gameTitle ?? "Unknown Game",
            Icon = BuildIconUrl(gameIcon),
            Author = string.Empty,
            Platform = Platforms.RetroAchievements,
            PlaytimeMinutes = playtime,
            Achievements = achievements,
            Identifier = ProviderIds.Retro + gameId,
            ParentIdentifier = ParentIdentifierFor(parentGameID),
            LastUpdated = DateTime.UtcNow,
            LastPlayed = lastPlayed
        };

        return game;
    }

    /// <summary>
    /// Maps a RetroAchievements <c>ParentGameID</c> to an Alua parent identifier, or null when the
    /// game is not a subset (the API reports 0 / null for standalone games).
    /// </summary>
    private static string? ParentIdentifierFor(int? parentGameID) =>
        parentGameID is > 0 ? ProviderIds.Retro + parentGameID.Value : null;

    /// <summary>
    /// Builds a RetroAchievements image URL, returning an empty string (no icon) when the source
    /// path is missing. Crucially this never returns the bare domain "https://i.retroachievements.org/"
    /// — an unloadable URL that the old "base + (path ?? "")" concatenation produced whenever a
    /// game's image path was empty, surfacing as a blank or wrong (recycled) game icon.
    /// </summary>
    private static string BuildIconUrl(string? imagePath) =>
        string.IsNullOrWhiteSpace(imagePath) ? string.Empty : "https://i.retroachievements.org/" + imagePath;

    /// <summary>
    /// Fetches achievement data for a game. The two independent API calls
    /// (GetGameInfoAndUserProgressAsync and GetGameExtendedAsync) are executed concurrently.
    /// </summary>
    private async Task<(List<Achievement> Achievements, int PlaytimeMinutes, int? ParentGameID)> GetAchievements(int gameID, CancellationToken cancellationToken = default)
    {
        var (achievements, playtime, _, _, parentGameID) = await GetAchievementsWithMetadata(gameID, cancellationToken);
        return (achievements, playtime, parentGameID);
    }

    /// <summary>
    /// Fetches achievement data and game metadata for a game.
    /// The two independent API calls are executed concurrently via Task.WhenAll.
    /// </summary>
    private async Task<(List<Achievement> Achievements, int PlaytimeMinutes, string? Title, string? Icon, int? ParentGameID)> GetAchievementsWithMetadata(int gameID, CancellationToken cancellationToken = default)
    {
        List<Achievement> achievements = new(64);
        int playtimeMinutes = -1;
        string? title = null;
        string? icon = null;
        int? parentGameID = null;

        try
        {
            // Fire both independent API calls concurrently
            var progressTask = _apiClient.GetGameInfoAndUserProgressAsync(_username, gameID, includeAwardMetadata: true, cancellationToken: cancellationToken);
            var extendedTask = _apiClient.GetGameExtendedAsync(gameID, cancellationToken: cancellationToken);

            await Task.WhenAll(progressTask, extendedTask);

            var progress = progressTask.Result;
            var gameExtended = extendedTask.Result;

            playtimeMinutes = progress.UserTotalPlaytime.HasValue ? progress.UserTotalPlaytime.Value / 60 : -1;
            title = gameExtended.Title;
            icon = gameExtended.ImageIcon;
            parentGameID = gameExtended.ParentGameID;

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
                    UnlockedOn = kvp.Value.DateEarned,
                    Id = kvp.Key,
                    Icon = iconUrl,
                    // RetroAchievements is the one provider that classifies achievements
                    // (missable / progression / win_condition) via the Type field.
                    Flags = MapRaType(kvp.Value.Type)
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

        return (achievements, playtimeMinutes, title, icon, parentGameID);
    }

    /// <summary>
    /// Maps a RetroAchievements achievement <c>Type</c> string to <see cref="AchievementFlags"/>.
    /// RA Type is single-valued per achievement, so at most one flag bit is set.
    /// </summary>
    private static AchievementFlags MapRaType(string? type) => type?.Trim().ToLowerInvariant() switch
    {
        "missable"      => AchievementFlags.Missable,
        "progression"   => AchievementFlags.Progression,
        "win_condition" => AchievementFlags.WinCondition,
        _               => AchievementFlags.None
    };

    /// <summary>
    /// Calculates the unlock percentage from batch game data instead of individual API calls.
    /// </summary>
    /// <param name="achievementId">The achievement ID</param>
    /// <param name="gameData">Game extended data containing NumAwarded statistics</param>
    /// <returns>Percentage (0-100) of users who unlocked the achievement, or -1 if unavailable</returns>
    private static double CalculateUnlockPercentageFromBatch(int achievementId, GameInfoExtended gameData)
    {
        if (!gameData.Achievements.TryGetValue(achievementId.ToString(), out var achievement))
            return -1;

        int totalPlayers = gameData.NumDistinctPlayersCasual > 0
            ? gameData.NumDistinctPlayersCasual
            : gameData.NumDistinctPlayers;

        if (totalPlayers <= 0)
            return -1;

        int numAwarded = achievement.NumAwarded ?? 0;
        return (double)numAwarded / totalPlayers * 100.0;
    }
}
