using System;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.DependencyInjection;
using Sachya;
using Serilog;
using Game = Alua.Models.Game;

namespace Alua.Services.Providers;

public sealed partial class SteamService : IAchievementProvider<SteamService>
{
    private SteamWebApiClient _apiClient = null!;
    private ViewModels.AppVM _appVm = null!;
    private ViewModels.SettingsVM _settingsVm = null!;
    private string _steamId = string.Empty;

    private SteamService() {}

    /// <summary>
    /// Creates a new instance of the Steam Service
    /// </summary>
    /// <param name="steamIdOrVanityUrl">Steam ID or Vanity URL</param>
    /// <returns>SteamService</returns>
    public static async Task<SteamService> Create(string steamIdOrVanityUrl)
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
    /// Gets a users whole library
    /// </summary>
    /// <returns>Game array</returns>
    public async Task<Game[]> GetLibrary()
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

            var owned = await _apiClient.GetOwnedGamesAsync(_steamId, includeAppInfo: true, includePlayedFreeGames: true);
            
            // Check if the response is valid
            if (owned?.response?.games == null)
            {
                Log.Error("Steam API returned null response or games list for user {SteamId}", _steamId);
                return [];
            }

            return await ConvertToAluaAsync(owned.response.games);
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
    public async Task<Game[]> RefreshLibrary()
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
            return await ConvertToAluaAsync(recent.response.games);
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
    /// <returns>Game Object</returns>
    public async Task<Game> RefreshTitle(string identifier)
    {
        try
        {
            int appId = int.Parse(identifier.Split("steam-").Last());

            // game schema gives us its name + achievement defs 
            await _apiClient.GetSchemaForGameAsync(appId);
            var data = (await _apiClient.GetOwnedGamesAsync(steamid: _steamId, includeAppInfo: true,
                includePlayedFreeGames: true, [appId])).response.games[0];
            string iconHash = data.img_icon_url;
            string iconUrl  = string.IsNullOrEmpty(iconHash)
                ? string.Empty
                : $"https://media.steampowered.com/steamcommunity/public/images/apps/{appId}/{iconHash}.jpg";

            return new Game
            {
                Identifier       = "steam-"+appId,
                Name             = data.name,
                Icon             = iconUrl,
                Author           = string.Empty,
                Platform         = Platforms.Steam,
                PlaytimeMinutes  = data.playtime_forever,
                Achievements     = (await GetAchievementDataAsync(appId.ToString())).ToObservableCollection(),
                LastUpdated      = DateTime.UtcNow
            };
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
            GameSchemaResult schema = await _apiClient.GetSchemaForGameAsync(appId);

            //Null Check/ Check we actually have achievements to get data for
            if (schema.game.availableGameStats?.achievements == null)
            {
                return [];
            }

            var defs = schema.game.availableGameStats.achievements
                .ToDictionary(a => a.name);

            var progress = await _apiClient.GetPlayerAchievementsAsync(_steamId, appId, language: "english");
            var list = new List<Achievement>(progress.playerstats.achievements.Count);
            foreach (var ach in progress.playerstats.achievements)
            {
                if (defs.TryGetValue(ach.apiname, out var def))
                {
                    list.Add(new Achievement
                    {
                        Title = string.IsNullOrWhiteSpace(def.displayName) ? ach.name : def.displayName,
                        Description = def.description,
                        Icon = ach.achieved == 1 ? def.icon : def.icongray,
                        IsUnlocked = ach.achieved == 1,
                        Id = ach.apiname,
                        IsHidden = def.hidden == 1
                    });
                }
                else
                {
                    list.Add(new Achievement
                    {
                        Title = ach.name,
                        Description = ach.description,
                        Icon =
                            $"https://media.steampowered.com/steamcommunity/public/images/apps/{appId}/{ach.apiname}.jpg",
                        IsUnlocked = ach.achieved == 1,
                        Id = ach.apiname,
                        IsHidden = false
                    });
                }
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
    /// Bridges sachya data to alua.
    /// </summary>
    /// <param name="src"></param>
    /// <returns></returns>
    private async Task<Game[]> ConvertToAluaAsync(List<Sachya.Game> src)
    {
        List<Game> result = new();
        foreach  (Sachya.Game g in src)
        {
            result.Add(new Game
            {
                Name            = g.name,
                Icon            = $"https://media.steampowered.com/steamcommunity/public/images/apps/{g.appid}/{g.img_icon_url}.jpg",
                Author          = string.Empty,
                Platform        = Platforms.Steam,
                PlaytimeMinutes = g.playtime_forever,
                Identifier      = "steam-"+g.appid,
                Achievements    = (await GetAchievementDataAsync(g.appid.ToString())).ToObservableCollection(),
                LastUpdated     = DateTime.UtcNow
            });

            _appVm.LoadingGamesSummary = $"Scanned {g.name} ({src.IndexOf(g)}/{src.Count})";
        }

        return result.ToArray();
    }

    [GeneratedRegex(@"^\d{17}$")]
    private static partial Regex SteamIDRegex();

    #endregion
}
