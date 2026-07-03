using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using Alua.Services;
using CommunityToolkit.Mvvm.DependencyInjection;
using Sachya;
using Sachya.Clients;
using Sachya.Definitions.Steam;
using Serilog;
using Game = Alua.Models.Game;
// We never lost control
namespace Alua.Services.Providers;

public sealed partial class SteamService : IAchievementProvider<SteamService>
{
    private static readonly TimeSpan BlacklistCacheTtl = TimeSpan.FromDays(7);

    // Instance-level blacklist so it is GC-eligible when the provider is replaced.
    private HashSet<uint>? _nonGameBlacklist;
    private readonly SemaphoreSlim _blacklistLock = new(1, 1);

    private SteamWebApiClient _apiClient = null!;
    private ViewModels.AppVM _appVm = null!;
    private ViewModels.SettingsVM _settingsVm = null!;
    private string _steamId = string.Empty;

    private SteamService() {}

    /// <summary>
    /// Creates a new instance of the Steam Service
    /// </summary>
    /// <param name="steamIdOrVanityUrl">Steam ID or Vanity URL</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>SteamService</returns>
    public static async Task<SteamService> Create(string steamIdOrVanityUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            SteamService steam = new()
            {
                _apiClient = new SteamWebApiClient(AppConfig.SteamAPIKey!),
                _appVm = Ioc.Default.GetRequiredService<ViewModels.AppVM>(),
                _settingsVm = Ioc.Default.GetRequiredService<ViewModels.SettingsVM>()
            };


            // Resolve Steam ID if needed.
            string raw = steamIdOrVanityUrl.Trim();
            if (SteamIDRegex().IsMatch(raw))
            {
                steam._steamId = raw;
            }
            else
            {
                var response = await steam._apiClient.ResolveVanityUrlAsync(raw);
                steam._steamId = response.response!.steamid!;
            }

            return steam;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create SteamService");
            throw;
        }
    }

    #region Methods
    /// <summary>
    /// Gets a users whole library including family shared games with achievements.
    /// Family-shared games are detected by checking recently-played titles not in the
    /// owned set; ConvertToAluaAsync skips games with no achievement data, so no
    /// per-game pre-check is needed here.
    /// </summary>
    /// <returns>Game array</returns>
    public async Task<Game[]> GetLibrary(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate required data before making API call
            if (string.IsNullOrEmpty(_steamId))
            {
                Log.Error("Steam ID is null or empty");
                return [];
            }

            if (_apiClient == null)
            {
                Log.Error("Steam API client is null");
                return [];
            }

            var ownedTask = _apiClient.GetOwnedGamesAsync(_steamId, includeAppInfo: true, includePlayedFreeGames: true);
            var recentTask = _apiClient.GetRecentlyPlayedGamesAsync(_steamId, count: 100);

            await Task.WhenAll(ownedTask, recentTask);

            var owned = ownedTask.Result;
            var recentlyPlayed = recentTask.Result;

            // Check if the response is valid
            if (owned?.response?.games == null)
            {
                Log.Error("Steam API returned null response or games list for user {SteamId}", _steamId);
                return [];
            }

            // Create a set of owned game IDs for quick lookup
            var ownedGameIds = new HashSet<int>(owned.response.games.Select(g => g.appid));

            // Find family shared games (played but not owned). We pass them directly into
            // ConvertToAluaAsync — GetAchievementDataAsync returns [] when there is no
            // achievement data, so games the user cannot access are silently skipped.
            var familySharedGames = recentlyPlayed?.response?.games?
                .Where(g => !ownedGameIds.Contains(g.appid))
                .ToList() ?? [];

            if (familySharedGames.Count > 0)
                Log.Information("Found {Count} potentially family-shared games (not owned but recently played)", familySharedGames.Count);

            // Combine owned and candidate family shared games
            var allGames = owned.response.games.Concat(familySharedGames).ToList();

            return await ConvertToAluaAsync(allGames, progress, cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get Steam library for user {SteamId}", _steamId);
            return [];
        }
    }

    /// <summary>
    /// Gets recently played games in a users library
    /// </summary>
    /// <returns>Game Array</returns>
    public async Task<Game[]> RefreshLibrary(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate required data before making API call
            if (string.IsNullOrEmpty(_steamId))
            {
                Log.Error("Steam ID is null or empty in RefreshLibrary");
                return [];
            }

            if (_apiClient == null)
            {
                Log.Error("Steam API client is null in RefreshLibrary");
                return [];
            }

            RecentlyPlayedGamesResult recent = await _apiClient.GetRecentlyPlayedGamesAsync(_steamId, count: 5);

            var skip = _settingsVm.Games.Values!
                .Where(g => g is { HasAchievements: false, Platform: Platforms.Steam })
                .Select(g => g.Identifier)
                .ToHashSet(StringComparer.Ordinal);

            recent.response.games.Remove(g => skip.Contains(g.appid.ToString()));

            if (recent.response.games.Count == 0) return [];

            // Re-fetch via GetOwnedGamesAsync to get rtime_last_played
            var appIds = recent.response.games.Select(g => g.appid).ToArray();
            var owned = await _apiClient.GetOwnedGamesAsync(_steamId, includeAppInfo: true, includePlayedFreeGames: true, appIds);
            return await ConvertToAluaAsync(owned.response.games, progress, cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "failed to refresh steam library");
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
            int appId = int.Parse(identifier.Split(ProviderIds.Steam).Last());

            var data = (await _apiClient.GetOwnedGamesAsync(steamid: _steamId, includeAppInfo: true,
                includePlayedFreeGames: true, [appId])).response.games[0];
            string iconUrl = $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/header.jpg";

            var game = new Game
            {
                Identifier       = ProviderIds.Steam+appId,
                Name             = data.name,
                Icon             = iconUrl,
                Author           = string.Empty,
                Platform         = Platforms.Steam,
                PlaytimeMinutes  = data.playtime_forever,
                Achievements     = (await GetAchievementDataAsync(appId.ToString())).ToObservableCollection(),
                LastUpdated      = DateTime.UtcNow,
                LastPlayed       = data.rtime_last_played > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(data.rtime_last_played).UtcDateTime
                    : null
            };

            return game;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to refresh " + identifier);
            throw;
        }
    }

    #endregion

    #region Helpers
    private async Task<Achievement[]> GetAchievementDataAsync(string identifier)
    {
        try
        {
            int appId = int.Parse(identifier);

            // Fire all three independent API requests concurrently
            var schemaTask = _apiClient.GetSchemaForGameAsync(appId);
            var progressTask = _apiClient.GetPlayerAchievementsAsync(_steamId, appId, language: "english");
            var globalStatsTask = _apiClient.GetGlobalAchievementPercentagesForAppAsync(appId);

            await Task.WhenAll(schemaTask, progressTask, globalStatsTask);

            GameSchemaResult schema = schemaTask.Result;

            //Null Check/ Check we actually have achievements to get data for
            if (schema.game.availableGameStats?.achievements == null)
            {
                return [];
            }

            var schemaAchievements = schema.game.availableGameStats.achievements;
            var defs = schemaAchievements.ToDictionary(a => a.name);

            var progress = progressTask.Result;

            var globalStats = globalStatsTask.Result;
            var rarityAchievements = globalStats.achievementpercentages?.achievements;
            var rarityData = rarityAchievements != null
                ? new Dictionary<string, double>(rarityAchievements.Count)
                : new Dictionary<string, double>();
            if (rarityAchievements != null)
                foreach (var a in rarityAchievements)
                    rarityData[a.name] = (double)a.percent;

            var list = new List<Achievement>(progress.playerstats.achievements.Count);
            foreach (var ach in progress.playerstats.achievements)
            {
                Achievement achievement;
                if (defs.TryGetValue(ach.apiname, out var def))
                {
                    achievement = new Achievement
                    {
                        Title = string.IsNullOrWhiteSpace(def.displayName) ? ach.name : def.displayName,
                        Description = def.description,
                        Icon = ach.achieved == 1 ? def.icon : def.icongray,
                        IsUnlocked = ach.achieved == 1,
                        UnlockedOn = ach.achieved == 1 && ach.unlocktime > 0
                            ? DateTimeOffset.FromUnixTimeSeconds(ach.unlocktime).UtcDateTime
                            : null,
                        Id = ach.apiname,
                        IsHidden = def.hidden == 1
                    };
                }
                else
                {
                    achievement = new Achievement
                    {
                        Title = ach.name,
                        Description = ach.description,
                        Icon =
                            $"https://media.steampowered.com/steamcommunity/public/images/apps/{appId}/{ach.apiname}.jpg",
                        IsUnlocked = ach.achieved == 1,
                        UnlockedOn = ach.achieved == 1 && ach.unlocktime > 0
                            ? DateTimeOffset.FromUnixTimeSeconds(ach.unlocktime).UtcDateTime
                            : null,
                        Id = ach.apiname,
                        IsHidden = false
                    };
                }

                // Set percentage if available
                if (rarityData.TryGetValue(ach.apiname, out double percentage))
                {
                    achievement.RarityPercentage = percentage;
                }

                list.Add(achievement);
            }

            return list.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get achievement data for game {Identifier}", identifier);
            return [];
        }
    }

    /// <summary>
    /// Gets or refreshes the non-game blacklist. Uses a JSON cache file with a 7-day TTL.
    /// The blacklist is derived from IStoreService/GetAppList: all app IDs minus game-only app IDs.
    /// </summary>
    private async Task<HashSet<uint>> GetNonGameBlacklistAsync()
    {
        if (_nonGameBlacklist != null)
            return _nonGameBlacklist;

        await _blacklistLock.WaitAsync();
        try
        {
            if (_nonGameBlacklist != null)
                return _nonGameBlacklist;

            var cachePath = Path.Combine(
                Windows.Storage.ApplicationData.Current.LocalFolder.Path,
                "steam_nongame_blacklist.json");

            // Try loading from cache
            if (File.Exists(cachePath))
            {
                try
                {
                    var cacheJson = await File.ReadAllTextAsync(cachePath);
                    using var doc = JsonDocument.Parse(cacheJson);
                    var generated = doc.RootElement.GetProperty("generated").GetDateTimeOffset();

                    if (DateTimeOffset.UtcNow - generated < BlacklistCacheTtl)
                    {
                        var cached = new HashSet<uint>();
                        foreach (var item in doc.RootElement.GetProperty("appids").EnumerateArray())
                            cached.Add(item.GetUInt32());
                        _nonGameBlacklist = cached;
                        Log.Information("Loaded Steam non-game blacklist from cache ({Count} entries)", cached.Count);
                        return _nonGameBlacklist;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to read blacklist cache, will regenerate");
                }
            }

            // Build blacklist: all apps minus game-only apps
            Log.Information("Building Steam non-game blacklist from IStoreService/GetAppList...");
            var allTask = _apiClient.GetAppListAsync(
                includeGames: true, includeDlc: true, includeSoftware: true,
                includeVideos: true, includeHardware: true);
            var gamesTask = _apiClient.GetAppListAsync(
                includeGames: true, includeDlc: false, includeSoftware: false,
                includeVideos: false, includeHardware: false);

            await Task.WhenAll(allTask, gamesTask);

            var allIds = allTask.Result;
            var gameIds = new HashSet<uint>(gamesTask.Result);
            var blacklist = new HashSet<uint>(allIds.Where(id => !gameIds.Contains(id)));

            Log.Information("Built non-game blacklist: {AllCount} total apps, {GameCount} games, {BlacklistCount} blacklisted",
                allIds.Count, gameIds.Count, blacklist.Count);

            // Write to cache
            try
            {
                using var stream = File.Create(cachePath);
                using var writer = new Utf8JsonWriter(stream);
                writer.WriteStartObject();
                writer.WriteString("generated", DateTimeOffset.UtcNow);
                writer.WriteStartArray("appids");
                foreach (var id in blacklist)
                    writer.WriteNumberValue(id);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to write blacklist cache");
            }

            _nonGameBlacklist = blacklist;
            return _nonGameBlacklist;
        }
        finally
        {
            _blacklistLock.Release();
        }
    }

    /// <summary>
    /// Bridges sachya data to alua. Parallelized with rate limiting.
    /// Filters out non-game apps (software, DLC, etc.) using the blacklist.
    /// </summary>
    private async Task<Game[]> ConvertToAluaAsync(List<Sachya.Definitions.Steam.Game> src, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (src.Count == 0) return [];

        // Load blacklist before processing so it's ready for filtering
        var blacklist = await GetNonGameBlacklistAsync();
        var filtered = src.Where(g => !blacklist.Contains((uint)g.appid)).ToList();
        var skipped = src.Count - filtered.Count;
        if (skipped > 0)
            Log.Information("Filtered out {Count} non-game apps from Steam library", skipped);

        if (filtered.Count == 0) return [];

        // Use RateLimitedExecutor for parallel processing with concurrency limit of 5
        using var executor = new RateLimitedExecutor(5, "SteamConvert");

        var games = await executor.ExecuteAllAsync(
            filtered,
            async (g, ct) =>
            {
                ct.ThrowIfCancellationRequested();

                var game = new Game
                {
                    Name            = g.name,
                    Icon            = $"https://cdn.akamai.steamstatic.com/steam/apps/{g.appid}/header.jpg",
                    Author          = string.Empty,
                    Platform        = Platforms.Steam,
                    PlaytimeMinutes = g.playtime_forever,
                    Identifier      = ProviderIds.Steam+g.appid,
                    Achievements    = (await GetAchievementDataAsync(g.appid.ToString())).ToObservableCollection(),
                    LastUpdated     = DateTime.UtcNow,
                    LastPlayed      = g.rtime_last_played > 0
                        ? DateTimeOffset.FromUnixTimeSeconds(g.rtime_last_played).UtcDateTime
                        : null
                };

                return game;
            },
            (current, total) => progress?.Report(new ScanProgress(current, total)),
            cancellationToken
        );

        return games;
    }

    [GeneratedRegex(@"^\d{17}$")]
    private static partial Regex SteamIDRegex();

    #endregion
}
