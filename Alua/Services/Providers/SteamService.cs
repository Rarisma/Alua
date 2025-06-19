using System.Text.RegularExpressions;
using Alua.Data;
using CommunityToolkit.Mvvm.DependencyInjection;
using Sachya;
using Game = Alua.Models.Game;

namespace Alua.Services;

public sealed class SteamService : IAchievementProvider<SteamService>
{
    private SteamWebApiClient _apiClient = null!;
    private AppVM _appVm = null!;
    private SettingsVM _settingsVm = null!;
    private string _steamId = string.Empty;

    private SteamService() {}

    /// <summary>
    /// Creates a new instance of the Steam Service
    /// </summary>
    /// <param name="steamIdOrVanityUrl">Steam ID or Vanity URL</param>
    /// <returns>SteamService</returns>
    public static async Task<SteamService> Create(string steamIdOrVanityUrl)
    {
        SteamService steam = new()
        {
            _apiClient = new SteamWebApiClient(AppConfig.SteamAPIKey!),
            _appVm = Ioc.Default.GetRequiredService<AppVM>(),
            _settingsVm = Ioc.Default.GetRequiredService<SettingsVM>()
        };


        // Nothing to do if the caller already supplied a 17‑digit ID
        string raw = steamIdOrVanityUrl.Trim();
        steam._steamId = (Regex.IsMatch(raw, @"^\d{17}$") ? raw : 
            (await steam._apiClient.ResolveVanityUrlAsync(raw)).response?.steamid) ?? string.Empty;
        
        return steam;
    }

    #region Methods
    /// <summary>
    /// Gets a users whole library
    /// </summary>
    /// <returns>Game array</returns>
    public async Task<Game[]> GetLibrary()
    {
        var owned = await _apiClient.GetOwnedGamesAsync(_steamId, includeAppInfo: true, includePlayedFreeGames: true);
        return await ConvertToAluaAsync(owned.response.games);
    }
    
    /// <summary>
    /// Gets recently played games in a users library
    /// </summary>
    /// <returns>Game Array</returns>
    public async Task<Game[]> RefreshLibrary()
    { 
        RecentlyPlayedGamesResult recent = await _apiClient.GetRecentlyPlayedGamesAsync(_steamId, count: 5);

        // skip games without achievements so we don’t waste quota scanning them
        var skip = _settingsVm.Games!
                              .Where(g => g is { HasAchievements: false, Platform: Platforms.Steam })
                              .Select(g => g.Identifier)
                              .ToHashSet(StringComparer.Ordinal);

        recent.response.games.Remove(g => skip.Contains(g.appid.ToString()));
        return await ConvertToAluaAsync(recent.response.games);
    }

    /// <summary>
    /// Updates/Gets data for a title in a users library
    /// </summary>
    /// <param name="identifier"></param>
    /// <returns>Game Object</returns>
    public async Task<Game> RefreshTitle(string identifier)
    {
        int appId = int.Parse(identifier);

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
            Identifier       = identifier,
            Name             = data.name,
            Icon             = iconUrl,
            Author           = string.Empty,
            Platform         = Platforms.Steam,
            PlaytimeMinutes  = data.playtime_forever,  
            Achievements     = (await GetAchievementDataAsync(identifier)).ToObservableCollection()
        };
    }

    #endregion

    #region Helpers
    private async Task<Achievement[]> GetAchievementDataAsync(string identifier)
    {
        int appId = int.Parse(identifier);
        var schema = await _apiClient.GetSchemaForGameAsync(appId);
        var defs   = schema.game.availableGameStats.achievements
            .ToDictionary(a => a.name);

        var progress = await _apiClient.GetPlayerAchievementsAsync(_steamId, appId, language: "english");
        var list = new List<Achievement>(progress.playerstats.achievements.Count);
        foreach (var ach in progress.playerstats.achievements)
        {
            if (defs.TryGetValue(ach.apiname, out var def))
            {
                list.Add(new Achievement
                {
                    Title       = string.IsNullOrWhiteSpace(def.displayName) ? ach.name : def.displayName,
                    Description = def.description,
                    Icon        = ach.achieved == 1 ? def.icon : def.icongray,
                    IsUnlocked  = ach.achieved == 1,
                    Id          = ach.apiname,
                    IsHidden    = def.hidden == 1
                });
            }
            else
            {
                list.Add(new Achievement
                {
                    Title       = ach.name,
                    Description = ach.description,
                    Icon        = $"https://media.steampowered.com/steamcommunity/public/images/apps/{appId}/{ach.apiname}.jpg",
                    IsUnlocked  = ach.achieved == 1,
                    Id          = ach.apiname,
                    IsHidden    = false
                });
            }
        }
        return list.ToArray();
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
                Identifier      = g.appid.ToString(),
                Achievements    = (await GetAchievementDataAsync(g.appid.ToString())).ToObservableCollection()
            });

            _appVm.LoadingGamesSummary = $"Scanned {g.name} ({src.IndexOf(g)}/{src.Count})";
        }

        return result.ToArray();
    }

    #endregion
}
