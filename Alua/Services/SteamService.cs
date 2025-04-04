using System.Collections.ObjectModel;
using System.Text.Json;
using Alua.Data;
using Sachya;
using Serilog;
using Steam.Models.SteamCommunity;
using SteamWebAPI2.Utilities;
using SteamWebAPI2.Interfaces;
using Game = Alua.Models.Game;

namespace Alua.Services;
/// <summary>
/// Handles getting data from steam.
/// </summary>
public class SteamService
{
    private readonly string _steamId;
    SteamWebApiClient _apiClient;
    
    public SteamService(string steamId)
    {
        _steamId = steamId;
        _apiClient = new(AppConfig.SteamAPIKey);
    }

    public async Task<List<Game>> GetOwnedGamesAsync()
    {
        if (_steamId is null)
        {
            return new();
        }

        var ownedGamesResponse = await _apiClient.GetOwnedGamesAsync(_steamId, true, true);
        var gamesInfo = ownedGamesResponse.response.games;

        var result = new List<Game>();

        foreach (Sachya.Game gameInfo in gamesInfo)
        {
            var game = new Game
            {
                Name = gameInfo.name,
                Icon = $"http://media.steampowered.com/steamcommunity/public/images/apps/{gameInfo.appid}/{gameInfo.img_icon_url}.jpg",
                Author = string.Empty,
                Platform = Platforms.Steam,
            };

            try
            {

                var response = await  _apiClient.GetPlayerAchievementsAsync(_steamId, gameInfo.appid, "english");
                game.Achievements = new();
                foreach (var ach in response.playerstats.achievements)
                {
                    game.Achievements.Add(new()
                    {
                        Title = ach.name ?? "Achievement Name Unavailable",
                        Description = ach.description ?? "Achievement Description Unavailable",
                        //Icon = ach.,
                        IsUnlocked = ach.achieved == 1,
                        Id = ach.apiname ?? "Achievement ID Unavailable",
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to get achievements for {0}", gameInfo.name);
                game.Achievements = new();
            }

            
            result.Add(game);
        }
        return result;
    }
    
}
