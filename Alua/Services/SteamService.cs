using System.Collections.ObjectModel;
using Alua.Data;
using Sachya;
using Serilog;
using Game = Alua.Models.Game;

namespace Alua.Services;
/// <summary>
/// Handles getting data from steam.
/// </summary>
public class SteamService(string steamId)
{
    private readonly SteamWebApiClient _apiClient = new(AppConfig.SteamAPIKey!);

    /// <summary>
    /// Gets users whole library and achievements.
    /// </summary>
    /// <returns>List of games.</returns>
    public async Task<List<Game>> GetOwnedGamesAsync()
    {
        var ownedGamesResponse = await _apiClient.GetOwnedGamesAsync(steamId, true, true);
        List<Sachya.Game> gamesInfo = ownedGamesResponse.response.games;
        
        return await ConvertToAlua(gamesInfo);
    }

    /// <summary>
    /// Returns players recently played games.
    /// </summary>
    /// <returns>List of games user has played recently</returns>
    public async Task<List<Game>> GetRecentlyPlayedGames()
    {
        RecentlyPlayedGamesResult games = await _apiClient.GetRecentlyPlayedGamesAsync(steamId,20);
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
    var result = new List<Game>();

    foreach (var gameInfo in gamesInfo)
    {
        var game = new Game
        {
            Name = gameInfo.name,
            Icon = $"https://media.steampowered.com/steamcommunity/public/images/apps/{gameInfo.appid}/{gameInfo.img_icon_url}.jpg",
            Author = string.Empty,
            Platform = Platforms.Steam,
        };

        try
        {
            // Retrieve the game schema that contains achievement definitions (including icon URLs)
            var schema = await _apiClient.GetSchemaForGameAsync(gameInfo.appid);
            var achievementDefinitions = schema?.game?.availableGameStats?.achievements?
                .ToDictionary(a => a.name) ?? new Dictionary<string, AchievementDefinition>();

            // Retrieve player's achievement progress
            var achievementsResponse = await _apiClient.GetPlayerAchievementsAsync(steamId, gameInfo.appid, "english");
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
                    });
                }
                else
                {
                    // Fallback: build the icon URL using the achievement API name directly
                    string iconUrl = $"https://media.steampowered.com/steamcommunity/public/images/apps/{gameInfo.appid}/{ach.apiname}.jpg";
                    game.Achievements.Add(new Achievement()
                    {
                        Title = ach.name,
                        Description = ach.description,
                        Icon = iconUrl,
                        IsUnlocked = ach.achieved == 1,
                        Id = ach.apiname,
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get achievements for {0}", gameInfo.name);
            game.Achievements = new ObservableCollection<Achievement>();
        }

        result.Add(game);
    }
    return result;
}

    
}
