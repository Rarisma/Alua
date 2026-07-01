using Alua.Models;
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

    // Session cache for title history — avoids redundant full-library fetches within a session.
    private (List<TitleHistory> Titles, DateTime FetchedAt)? _titleHistoryCache;
    private static readonly TimeSpan TitleHistoryCacheTtl = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The authenticated user's Xbox gamertag
    /// </summary>
    public string? Gamertag => _apiClient?.Gamertag;

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

            Log.Information("XboxService created successfully. XUID: {XUID}, Gamertag: {Gamertag}", xbox._xuid, xbox.Gamertag);

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
    public async Task<Game[]> GetLibrary(CancellationToken cancellationToken = default, Action<Game>? onGameReady = null)
    {
        try
        {
            Log.Information("Starting GetLibrary for Xbox user {XUID}", _xuid);

            if (string.IsNullOrEmpty(_xuid) || _apiClient == null)
            {
                Log.Error("XUID or API client is not initialized in GetLibrary");
                return [];
            }

            Log.Debug("Fetching title history for full library scan");
            var titles = await GetCachedTitleHistoryAsync();

            if (titles == null)
            {
                Log.Error("Xbox API returned null response or titles list for user {XUID}", _xuid);
                return [];
            }

            Log.Information("Found {Count} titles in library", titles.Count);
            var result = await ConvertToAluaAsync(titles, cancellationToken, onGameReady);
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
    public async Task<Game[]> RefreshLibrary(CancellationToken cancellationToken = default, Action<Game>? onGameReady = null)
    {
        try
        {
            Log.Information("Starting RefreshLibrary for Xbox user {XUID}", _xuid);

            if (string.IsNullOrEmpty(_xuid) || _apiClient == null)
            {
                Log.Error("XUID or API client is not initialized in RefreshLibrary");
                return [];
            }

            var titles = await GetCachedTitleHistoryAsync();

            var skip = _settingsVm.Games.Values!
                .Where(g => g is { HasAchievements: false, Platform: Platforms.Xbox })
                .Select(g => g.Identifier)
                .ToHashSet(StringComparer.Ordinal);

            Log.Debug("Skipping {Count} games without achievements", skip.Count);

            // Get the 5 most recently played games
            var recentTitles = titles
                .Where(t => !skip.Contains($"{ProviderIds.Xbox}{t.TitleId}"))
                .Take(5)
                .ToList();

            Log.Information("Found {Count} recent titles to refresh after filtering", recentTitles.Count);

            var result = await ConvertToAluaAsync(recentTitles, cancellationToken, onGameReady);
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
    public async Task<Game> RefreshTitle(string identifier, CancellationToken cancellationToken = default)
    {
        try
        {
            Log.Information("Starting RefreshTitle for identifier: {Identifier}", identifier);
            string titleId = identifier.Split(ProviderIds.Xbox).Last();

            var titles = await GetCachedTitleHistoryAsync();
            var title = titles.FirstOrDefault(t => t.TitleId == titleId);

            if (title == null)
            {
                Log.Error("Title {TitleId} not found in user's library", titleId);
                throw new InvalidOperationException($"Title {titleId} not found in user's library");
            }

            Log.Information("Found title: {TitleName} (ID: {TitleId})", title.Name, titleId);

            var achievements = await GetAchievementDataAsync(titleId, title.Achievement, cancellationToken);
            Log.Information("Retrieved {Count} achievements for title {TitleId}", achievements.Length, titleId);

            var game = new Game
            {
                Identifier = $"{ProviderIds.Xbox}{titleId}",
                Name = title.Name,
                Icon = title.DisplayImage,
                Author = string.Empty,
                Platform = Platforms.Xbox,
                PlaytimeMinutes = -1,
                Achievements = achievements.ToObservableCollection(),
                LastUpdated = DateTime.UtcNow,
                LastPlayed = title.Details?.LastTimePlayed
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
    /// Returns the cached title history if still fresh; otherwise fetches from the API and updates the cache.
    /// Both RefreshLibrary and RefreshTitle share this cache to avoid redundant full-library fetches.
    /// </summary>
    private async Task<List<TitleHistory>> GetCachedTitleHistoryAsync()
    {
        var now = DateTime.UtcNow;
        if (_titleHistoryCache.HasValue && now - _titleHistoryCache.Value.FetchedAt < TitleHistoryCacheTtl)
        {
            Log.Debug("Using cached Xbox title history ({Count} titles)", _titleHistoryCache.Value.Titles.Count);
            return _titleHistoryCache.Value.Titles;
        }

        Log.Debug("Fetching fresh Xbox title history for user {XUID}", _xuid);
        var response = await _apiClient.GetTitleHistoryAsync(_xuid);
        var titles = response?.Titles ?? [];
        _titleHistoryCache = (titles, now);
        return titles;
    }

    /// <summary>
    /// Gets achievement data for a title using the achievements endpoint.
    /// Uses ProgressState for definitive unlock detection.
    /// Each API call is individually wrapped so failures cascade to the placeholder fallback.
    /// </summary>
    private async Task<AluaAchievement[]> GetAchievementDataAsync(
        string titleId,
        TitleHistoryAchievement? titleAchievementInfo,
        CancellationToken cancellationToken = default)
    {
        Log.Information("Getting achievement data for title {TitleId}", titleId);

        // Primary: achievements endpoint — returns full data with ProgressState, Progressions, rarity
        try
        {
            var response = await _apiClient.GetMultipleTitleAchievementsAsync(titleId);

            if (response?.Achievements != null && response.Achievements.Any())
            {
                Log.Information("Retrieved {Count} achievements from achievements endpoint for title {TitleId}",
                    response.Achievements.Count, titleId);

                var result = response.Achievements.Select(ach =>
                {
                    bool isUnlocked = ach.ProgressState == "Achieved";
                    DateTime? unlockedOn = null;
                    if (isUnlocked && ach.Progressions?.Count > 0)
                    {
                        var timestamp = ach.Progressions
                            .Where(p => p.TimeUnlocked != default(DateTime))
                            .Select(p => (DateTime?)p.TimeUnlocked)
                            .FirstOrDefault();
                        if (timestamp.HasValue)
                            unlockedOn = timestamp.Value;
                    }

                    return new AluaAchievement
                    {
                        Title = ach.Name ?? "Unknown Achievement",
                        Description = ach.IsSecret && !isUnlocked
                            ? (ach.LockedDescription ?? "Secret achievement")
                            : (ach.Description ?? string.Empty),
                        Icon = ach.Icon ?? string.Empty,
                        IsUnlocked = isUnlocked,
                        UnlockedOn = unlockedOn,
                        Id = $"{ProviderIds.Xbox}{titleId}-{ach.Id}",
                        IsHidden = ach.IsSecret,
                        RarityPercentage = ach.RarityPercentage
                    };
                }).ToArray();

                // Xbox 360 v1 API only returns unlocked achievements.
                // If we got fewer than the total, fill in locked placeholders.
                var totalExpected = titleAchievementInfo?.TotalAchievements ?? result.Length;
                if (result.Length < totalExpected)
                {
                    Log.Information("Got {Got} achievements but title reports {Total} — adding {Missing} locked placeholders for title {TitleId}",
                        result.Length, totalExpected, totalExpected - result.Length, titleId);

                    var locked = Enumerable.Range(0, totalExpected - result.Length)
                        .Select(i => new AluaAchievement
                        {
                            Title = $"Locked Achievement {i + 1}",
                            Description = "Locked",
                            Icon = string.Empty,
                            IsUnlocked = false,
                            Id = $"{ProviderIds.Xbox}{titleId}-locked-{i}",
                            IsHidden = false,
                            RarityPercentage = null
                        });

                    result = result.Concat(locked).ToArray();
                }

                return result;
            }

            Log.Information("Achievements endpoint returned no data for title {TitleId}", titleId);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Achievements endpoint failed for title {TitleId}, falling back to placeholders", titleId);
        }

        // Fallback: placeholder achievements from title-level counts (always reachable)
        if (titleAchievementInfo != null && titleAchievementInfo.TotalAchievements > 0)
        {
            Log.Warning("Creating {Count} placeholder achievements for Xbox title {TitleId} ({Unlocked}/{Total} unlocked)",
                titleAchievementInfo.TotalAchievements, titleId,
                titleAchievementInfo.CurrentAchievements, titleAchievementInfo.TotalAchievements);

            return Enumerable.Range(0, titleAchievementInfo.TotalAchievements)
                .Select(i => new AluaAchievement
                {
                    Title = $"Achievement {i + 1}",
                    Description = $"Progress: {titleAchievementInfo.CurrentGamerscore}/{titleAchievementInfo.TotalGamerscore} Gamerscore",
                    Icon = string.Empty,
                    IsUnlocked = i < titleAchievementInfo.CurrentAchievements,
                    Id = $"{ProviderIds.Xbox}{titleId}-{i}",
                    IsHidden = false,
                    RarityPercentage = null
                }).ToArray();
        }

        Log.Warning("No achievement data found for Xbox title {TitleId}", titleId);
        return [];
    }

    /// <summary>
    /// Converts title history entries to Alua Game objects with rate-limited achievement fetching.
    /// </summary>
    private async Task<Game[]> ConvertToAluaAsync(List<TitleHistory> src, CancellationToken cancellationToken = default, Action<Game>? onGameReady = null)
    {
        Log.Information("Starting ConvertToAluaAsync with {Count} titles", src.Count);

        // Filter to only include games with achievements
        var gamesWithAchievements = src.Where(t =>
            t.Achievement != null &&
            t.Achievement.TotalGamerscore > 0).ToList();

        Log.Information("Filtered to {Count} games with achievements (from {Total} total)",
            gamesWithAchievements.Count, src.Count);

        using var executor = new RateLimitedExecutor(3, "Xbox");
        var games = await executor.ExecuteAllAsync(
            gamesWithAchievements,
            async (title, ct) =>
            {
                ct.ThrowIfCancellationRequested();

                var achievements = await GetAchievementDataAsync(title.TitleId, title.Achievement, ct);

                return new Game
                {
                    Name = title.Name,
                    Icon = title.DisplayImage ?? string.Empty,
                    Author = string.Empty,
                    Platform = Platforms.Xbox,
                    PlaytimeMinutes = -1,
                    Identifier = $"{ProviderIds.Xbox}{title.TitleId}",
                    Achievements = achievements.ToObservableCollection(),
                    LastUpdated = DateTime.UtcNow,
                    LastPlayed = title.Details?.LastTimePlayed
                };
            },
            progressCallback: (current, total) => _appVm.LoadingGamesSummary = $"Scanned Xbox ({current}/{total})",
            onItemCompleted: onGameReady,
            cancellationToken: cancellationToken
        );

        Log.Information("ConvertToAluaAsync completed. Converted {Count} games", games.Length);
        return games;
    }

    #endregion
}
