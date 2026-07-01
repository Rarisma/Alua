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
    public Task<Game[]> GetLibrary(CancellationToken cancellationToken = default, Action<Game>? onGameReady = null)
        => GetLibraryCore(cancellationToken, isRetry: false, onGameReady);

    private async Task<Game[]> GetLibraryCore(CancellationToken cancellationToken, bool isRetry, Action<Game>? onGameReady = null)
    {
        try
        {
            Log.Information("Getting PSN library for user");

            var trophyTitles = await _apiClient.GetUserTrophyTitlesAsync("me");

            if (trophyTitles.TrophyTitles.Count == 0)
                return [];

            // Surface an up-front warning for large libraries — the per-game trophy fetches at
            // concurrency 3 take minutes for 1000+ titles, so set the expectation before the
            // per-game progress callback kicks in.
            int titleCount = trophyTitles.TrophyTitles.Count;
            if (titleCount > 100)
            {
                Log.Information("Large PSN library detected ({Count} titles), this will take a while", titleCount);
                _appVm.LoadingGamesSummary = $"Large PSN library ({titleCount} titles), this may take several minutes...";
            }

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
                progressCallback: (current, total) => _appVm.LoadingGamesSummary = $"Scanning PSN ({current}/{total})",
                onItemCompleted: onGameReady,
                cancellationToken: cancellationToken
            );

            return games.Where(g => g != null).Cast<Game>().ToArray();
        }
        catch (PlaystationApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            Log.Warning("PSN access token expired, attempting re-authentication from stored NPSSO");
            if (!isRetry && await TryRecreateClient())
                return await GetLibraryCore(cancellationToken, isRetry: true, onGameReady);

            _appVm.SetError(ProviderError.AuthExpired("PlayStation").UserMessage);
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
    public Task<Game[]> RefreshLibrary(CancellationToken cancellationToken = default, Action<Game>? onGameReady = null)
        => RefreshLibraryCore(cancellationToken, isRetry: false, onGameReady);

    private async Task<Game[]> RefreshLibraryCore(CancellationToken cancellationToken, bool isRetry, Action<Game>? onGameReady = null)
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
                progressCallback: (current, total) => _appVm.LoadingGamesSummary = $"Refreshing PSN ({current}/{total})",
                onItemCompleted: onGameReady,
                cancellationToken: cancellationToken
            );

            return games.Where(g => g != null).Cast<Game>().ToArray();
        }
        catch (PlaystationApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            Log.Warning("PSN access token expired, attempting re-authentication from stored NPSSO");
            if (!isRetry && await TryRecreateClient())
                return await RefreshLibraryCore(cancellationToken, isRetry: true, onGameReady);

            _appVm.SetError(ProviderError.AuthExpired("PlayStation").UserMessage);
            return [];
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to refresh PSN library for user");
            return [];
        }
    }

    /// <summary>
    /// Updates data for a single title with full trophy information.
    /// Fetches only a small window of recent titles to locate the target rather than
    /// downloading the user's entire trophy library.
    /// </summary>
    /// <param name="identifier">Game Identifier (NPCommunicationID)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Game Object with trophy data</returns>
    public Task<Game> RefreshTitle(string identifier, CancellationToken cancellationToken = default)
        => RefreshTitleCore(identifier, cancellationToken, isRetry: false);

    private async Task<Game> RefreshTitleCore(string identifier, CancellationToken cancellationToken, bool isRetry)
    {
        try
        {
            Log.Information("Refreshing PSN title {Identifier} for user with trophy data", identifier);

            // Identifiers are persisted with the "psn-" prefix (ProviderIds.PSN); strip it back to
            // the bare NpCommunicationId that PSN's trophy API matches on.
            var npCommunicationId = identifier.StartsWith(ProviderIds.PSN, StringComparison.Ordinal)
                ? identifier[ProviderIds.PSN.Length..]
                : identifier;

            var targetTitle = await FindTrophyTitleAsync(npCommunicationId, cancellationToken);

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
            if (!isRetry && await TryRecreateClient())
                return await RefreshTitleCore(identifier, cancellationToken, isRetry: true);

            _appVm.SetError(ProviderError.AuthExpired("PlayStation").UserMessage);
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to refresh PSN title {Identifier} for user", identifier);
            throw;
        }
    }

    /// <summary>
    /// Finds a single TrophyTitle by NpCommunicationId without downloading the entire library.
    /// Fetches in pages of 50 (PSN returns titles sorted by most-recently-played, so the target
    /// is almost always in the first page). Falls back to a second page if not found.
    /// </summary>
    private async Task<TrophyTitle?> FindTrophyTitleAsync(string npCommunicationId, CancellationToken cancellationToken = default)
    {
        const int pageSize = 50;
        int offset = 0;
        int totalFetched = 0;
        // Search up to 200 recent titles before giving up to avoid an unbounded walk
        const int maxSearch = 200;

        while (totalFetched < maxSearch)
        {
            var page = await _apiClient.GetUserTrophyTitlesAsync("me", limit: pageSize, offset: offset, cancellationToken: cancellationToken);
            if (page.TrophyTitles.Count == 0)
                break;

            var found = page.TrophyTitles.FirstOrDefault(t => t.NpCommunicationId == npCommunicationId);
            if (found != null)
                return found;

            totalFetched += page.TrophyTitles.Count;
            offset += page.TrophyTitles.Count;

            // If PSN returned fewer titles than requested, we've reached the end
            if (page.TrophyTitles.Count < pageSize)
                break;
        }

        Log.Warning("PSN title {NpCommunicationId} not found in first {MaxSearch} titles", npCommunicationId, maxSearch);
        return null;
    }

    /// <summary>
    /// Attempts to re-create the PSN client from the stored NPSSO token.
    /// Returns true if successful, false if the NPSSO itself has expired.
    /// </summary>
    /// <summary>
    /// Attempts to re-create the PSN client from the stored NPSSO token. Callers bound retries with
    /// their own per-operation <c>isRetry</c> flag, so re-auth happens at most once per top-level
    /// call and a persistent 401 (even with a succeeding recreate) can no longer recurse unbounded.
    /// </summary>
    private async Task<bool> TryRecreateClient()
    {
        try
        {
            Log.Information("Re-creating PSN client from stored NPSSO token");
            _apiClient.Dispose();
            _apiClient = await PSNClient.CreateFromNpsso(_npssoToken);
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

            // Get earned trophies and title trophies concurrently — these calls accept
            // NpCommunicationId + platform directly and do not require the trophy library.
            var earnedTask = _apiClient.GetUserEarnedTrophiesAsync(title.NpCommunicationId, platform, "me");
            var titleTrophiesTask = _apiClient.GetTitleTrophiesAsync(title.NpCommunicationId, platform);

            await Task.WhenAll(earnedTask, titleTrophiesTask);

            return ConvertSingleToAluaGame(title, earnedTask.Result, titleTrophiesTask.Result);
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
            Identifier = ProviderIds.PSN + title.NpCommunicationId,
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
        // Create a lookup for earned trophies
        var earnedLookup = earnedTrophies.Trophies.ToDictionary(t => t.TrophyId, t => t);

        // Build achievements as a List first, then wrap once — avoids repeated collection resizes
        var list = new List<Achievement>(titleTrophies.Trophies.Count);

        foreach (var trophy in titleTrophies.Trophies)
        {
            var earnedTrophy = earnedLookup.GetValueOrDefault(trophy.TrophyId);

            // Use null for parse failures so downstream null-checks behave correctly
            double? rarityPercentage = double.TryParse(earnedTrophy?.TrophyEarnedRate, out var rate)
                ? Math.Round(rate, 2)
                : null;

            list.Add(new Achievement
            {
                Id = trophy.TrophyId.ToString(),
                Title = trophy.TrophyName,
                Description = trophy.TrophyDetail,
                IsUnlocked = earnedTrophy?.Earned ?? false,
                UnlockedOn = earnedTrophy?.EarnedDateTime,
                Icon = trophy.TrophyIconUrl,
                IsHidden = trophy.TrophyHidden,
                RarityPercentage = rarityPercentage,
                TrophyType = ParseTrophyKind(trophy.TrophyType),
            });
        }

        var game = ConvertToAluaGame(title);
        game.Achievements = list.ToObservableCollection();

        return game;
    }

    /// <summary>Maps PSN's trophy-type string ("bronze"/"silver"/"gold"/"platinum") to a tier.</summary>
    private static TrophyKind ParseTrophyKind(string? trophyType) => trophyType?.ToLowerInvariant() switch
    {
        "bronze"   => TrophyKind.Bronze,
        "silver"   => TrophyKind.Silver,
        "gold"     => TrophyKind.Gold,
        "platinum" => TrophyKind.Platinum,
        _          => TrophyKind.None
    };

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
