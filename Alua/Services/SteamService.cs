using System.Collections.ObjectModel;
using System.Net.Http.Json;
using Alua.Data;
using CommunityToolkit.Mvvm.DependencyInjection;
using Sachya;
using Serilog;
using System.Text.RegularExpressions;
using Game = Alua.Models.Game;

namespace Alua.Services;

/// <summary>
/// Handles getting data from steam.
/// </summary>
public class SteamService
{
    private readonly SteamWebApiClient _apiClient = new(AppConfig.SteamAPIKey!);
    private string _steamId = string.Empty;
    
    private SteamService() { }
    
    /// <summary>
    /// Initializes a new instance of the SteamService.
    /// Automatically resolves vanity URLs to Steam IDs if needed.
    /// </summary>
    public static async Task<SteamService> CreateAsync(string steamIdOrVanityUrl)
    {
        SteamService service = new();
        service._steamId = await service.ResolveVanityUrlIfNeeded(steamIdOrVanityUrl);
        return service;
    }
    /// <summary>
    /// Determines if input is a Steam ID or vanity URL and resolves accordingly.
    /// </summary>
    /// <param name="steamIdOrVanityUrl">Either a Steam ID or vanity URL username</param>
    /// <returns>The resolved Steam ID</returns>
    private async Task<string> ResolveVanityUrlIfNeeded(string steamIdOrVanityUrl)
    {
        // If already a 17-digit Steam ID, return as-is
        if (Regex.IsMatch(steamIdOrVanityUrl, "^\\d{17}$"))
            return steamIdOrVanityUrl;
        try
        {
            var response = await _apiClient.ResolveVanityUrlAsync(steamIdOrVanityUrl);
            if (response.response?.success == 1 && !string.IsNullOrEmpty(response.response.steamid))
            {
                Log.Information("Successfully resolved vanity URL {0} to Steam ID {1}", steamIdOrVanityUrl, response.response.steamid);
                return response.response.steamid;
            }
            Log.Warning("Failed to resolve vanity URL {0}. Using as-is.", steamIdOrVanityUrl);
            return steamIdOrVanityUrl;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error resolving vanity URL {0}. Using as-is.", steamIdOrVanityUrl);
            return steamIdOrVanityUrl;
        }
    }

    /// <summary>
    /// Gets users whole library and achievements.
    /// </summary>
    /// <returns>List of games.</returns>
    public async Task<List<Game>> GetOwnedGamesAsync()
    {
        Ioc.Default.GetRequiredService<AppVM>().GamesFoundMessage = "Preparing to scan your steam library...";
        OwnedGamesResult ownedGamesResponse = await _apiClient.GetOwnedGamesAsync(_steamId, true, true);
        var gamesInfo = ownedGamesResponse.response.games;
        
        return await ConvertToAlua(gamesInfo);
    }

    /// <summary>
    /// Returns players recently played games.
    /// </summary>
    /// <returns>List of games user has played recently</returns>
    public async Task<List<Game>> GetRecentlyPlayedGames()
    {
        Ioc.Default.GetRequiredService<AppVM>().GamesFoundMessage = $"Preparing to update your steam library...";
        RecentlyPlayedGamesResult games = await _apiClient.GetRecentlyPlayedGamesAsync(_steamId, 20);
        return await ConvertToAlua(games.response.games);
    }

    /// <summary>
    /// Converts steam objects into Alua objects and gets steam achievement data
    /// </summary>
    /// <remarks>One day Sachya should have abstractions like Alua does
    /// until Sachya fully supports all providers, it's not really worth building
    /// a full abstraction wrapper.
    /// </remarks>
    /// <returns></returns>
    private async Task<List<Game>> ConvertToAlua(List<Sachya.Game> gamesInfo)
    {
        //TODO: Move parts to Sachya
        var result = new List<Game>();
        AppVM appVM = Ioc.Default.GetRequiredService<AppVM>();
        foreach (var gameInfo in gamesInfo)
        {
            var game = new Game
            {
                Name = gameInfo.name,
                Icon = $"https://media.steampowered.com/steamcommunity/public/images/apps/{gameInfo.appid}/{gameInfo.img_icon_url}.jpg",
                Author = string.Empty,
                Platform = Platforms.Steam,
                PlaytimeMinutes = gameInfo.playtime_forever, // Total playtime in minutes
            };

            try
            {
                // Retrieve the game schema that contains achievement definitions (including icon URLs)
                var schema = await _apiClient.GetSchemaForGameAsync(gameInfo.appid);
                var achievementDefinitions = schema?.game?.availableGameStats?.achievements?
                    .ToDictionary(a => a.name) ?? new Dictionary<string, AchievementDefinition>();

                // Retrieve player's achievement progress
                var achievementsResponse = await _apiClient.GetPlayerAchievementsAsync(_steamId, gameInfo.appid, "english");
                game.Achievements = new ObservableCollection<Achievement>();

                foreach (var ach in achievementsResponse.playerstats.achievements)
                {
                    if (achievementDefinitions.TryGetValue(ach.apiname, out var definition))
                    {
                        // Use colored icon if unlocked, otherwise use grayed out icon.
                        string iconUrl = ach.achieved == 1 ? definition.icon : definition.icongray;

                        game.Achievements.Add(new Achievement
                        {
                            Title = !string.IsNullOrWhiteSpace(definition.displayName) ? definition.displayName : ach.name,
                            Description = definition.description,
                            Icon = iconUrl,
                            IsUnlocked = ach.achieved == 1,
                            Id = ach.apiname,
                            IsHidden = definition.hidden == 1 // Set IsHidden based on the hidden flag from Steam
                        });
                        
                    }
                    else
                    {
                        // Fallback: build the icon URL using the achievement API name directly
                        string iconUrl = $"https://media.steampowered.com/steamcommunity/public/images/apps/{gameInfo.appid}/{ach.apiname}.jpg";
                        game.Achievements.Add(new Achievement
                        {
                            Title = ach.name,
                            Description = ach.description,
                            Icon = iconUrl,
                            IsUnlocked = ach.achieved == 1,
                            Id = ach.apiname,
                            IsHidden = false  
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to get achievements for {0}", gameInfo.name);
                game.Achievements = [];
            }

            result.Add(game);
            appVM.LoadingGamesSummary = $"Scanned {game.Name} ( {result.Count} / {gamesInfo.Count})";
        }
        return result;
    }
}
