using System.Collections.ObjectModel;
using System.Net;
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
    private string _npssoToken = null!;
    private bool _hasRetriedAuth;

    private PSNService() {}

    /// <summary>
    /// Creates a new instance of the PSN Service using PSN SSO token
    /// </summary>
    /// <param name="npssoToken">PSN SSO token</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>PSNService</returns>
    public static async Task<PSNService> Create(string npssoToken, CancellationToken cancellationToken = default)
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
                _settingsVm = settingsVm,
                _npssoToken = npssoToken
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
    /// Gets the users whole PSN library with trophy data
    /// </summary>
    /// <returns>Array of Games</returns>
    public async Task<Game[]> GetLibrary(CancellationToken cancellationToken = default)
    {
        try
        {
            Log.Information("Getting PSN library for user");

            var trophyTitles = await _apiClient.GetUserTrophyTitlesAsync("me");

            if (trophyTitles.TrophyTitles.Count == 0)
                return [];

            // Fetch trophy data for all games (matching Steam's GetLibrary pattern)
            using var executor = new RateLimitedExecutor(3, "PSNLibrary");

            var games = await executor.ExecuteAllWithNullableAsync(
                trophyTitles.TrophyTitles,
                async (title, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        return await GetGameWithTrophyData(title);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to get trophy data for {GameName}, adding basic info only", title.TrophyTitleName);
                        return ConvertToAluaGame(title);
                    }
                },
                (current, total) => _appVm.LoadingGamesSummary = $"Scanning PSN ({current}/{total})",
                cancellationToken
            );

            return games.Where(g => g != null).Cast<Game>().ToArray();
        }
        catch (PlaystationApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            Log.Warning("PSN access token expired, attempting re-authentication from stored NPSSO");
            if (await TryRecreateClient())
                return await GetLibrary(cancellationToken);

            _appVm.SetError("PlayStation: Session expired. Please sign in again in Settings.");
            return [];
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
    public async Task<Game[]> RefreshLibrary(CancellationToken cancellationToken = default)
    {
        try
        {
            Log.Information("Refreshing PSN library for user with trophy data");

            // Get recent trophy titles (limit to 10 most recent)
            var recentTrophies = await _apiClient.GetUserTrophyTitlesAsync("me", limit: 10);

            if (recentTrophies.TrophyTitles.Count == 0)
                return [];

            // Use RateLimitedExecutor with concurrency of 3 for PSN (more conservative due to stricter rate limits)
            using var executor = new RateLimitedExecutor(3, "PSNRefresh");

            var games = await executor.ExecuteAllWithNullableAsync(
                recentTrophies.TrophyTitles,
                async (title, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        return await GetGameWithTrophyData(title);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to get trophy data for {GameName}, adding basic info only", title.TrophyTitleName);
                        return ConvertToAluaGame(title);
                    }
                },
                (current, total) => _appVm.LoadingGamesSummary = $"Refreshing PSN ({current}/{total})",
                cancellationToken
            );

            return games.Where(g => g != null).Cast<Game>().ToArray();
        }
        catch (PlaystationApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            Log.Warning("PSN access token expired, attempting re-authentication from stored NPSSO");
            if (await TryRecreateClient())
                return await RefreshLibrary(cancellationToken);

            _appVm.SetError("PlayStation: Session expired. Please sign in again in Settings.");
            return [];
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
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Game Object with trophy data</returns>
    public async Task<Game> RefreshTitle(string identifier, CancellationToken cancellationToken = default)
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
        catch (PlaystationApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            Log.Warning("PSN access token expired during RefreshTitle, attempting re-authentication from stored NPSSO");
            if (await TryRecreateClient())
                return await RefreshTitle(identifier, cancellationToken);

            _appVm.SetError("PlayStation: Session expired. Please sign in again in Settings.");
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to refresh PSN title {Identifier} for user", identifier);
            throw;
        }
    }

    /// <summary>
    /// Attempts to re-create the PSN client from the stored NPSSO token.
    /// Returns true if successful, false if the NPSSO itself has expired.
    /// </summary>
    private async Task<bool> TryRecreateClient()
    {
        if (_hasRetriedAuth)
        {
            Log.Warning("Already retried PSN re-authentication, NPSSO token likely expired");
            return false;
        }

        _hasRetriedAuth = true;
        try
        {
            Log.Information("Re-creating PSN client from stored NPSSO token");
            _apiClient.Dispose();
            _apiClient = await PSNClient.CreateFromNpsso(_npssoToken);
            _hasRetriedAuth = false;
            Log.Information("Successfully re-created PSN client");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to re-create PSN client from stored NPSSO — token likely expired");
            return false;
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
        catch (PlaystationApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            Log.Warning("Trophy data not found for {GameName}", title.TrophyTitleName);
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
            LastUpdated = DateTime.UtcNow,
            LastPlayed = title.LastUpdatedDateTime.DateTime,
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
                RarityPercentage = Math.Round(double.TryParse(earnedTrophy?.TrophyEarnedRate, out var rate) ? rate : -1,2),
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
