using System.Collections.ObjectModel;
using System.Text.Json;
using Alua.Data;
using Serilog;
using Steam.Models.SteamCommunity;
using SteamWebAPI2.Utilities;
using SteamWebAPI2.Interfaces;

namespace Alua.Services;
/// <summary>
/// Handles getting data from steam.
/// </summary>
public class SteamService
{
    private static readonly HttpClient _httpClient = new HttpClient();
    private readonly ulong _steamId;
    private readonly IPlayerService _playerService;
    private readonly ISteamUserStats _userStats;

    public SteamService(ulong steamId)
    {
        _steamId = steamId;
        var steamFactory = new SteamWebInterfaceFactory(AppConfig.SteamAPIKey);
        _playerService = steamFactory.CreateSteamWebInterface<PlayerService>();
        _userStats = steamFactory.CreateSteamWebInterface<SteamUserStats>();
    }

    public async Task<List<Game>> GetOwnedGamesAsync()
    {
        var ownedGamesResponse = await _playerService.GetOwnedGamesAsync(_steamId, includeAppInfo: true, includeFreeGames: true);
        var gamesInfo = ownedGamesResponse.Data?.OwnedGames ?? new List<OwnedGameModel>();

        var result = new List<Game>();

        foreach (var gameInfo in gamesInfo)
        {
            var game = new Game
            {
                Name = gameInfo.Name,
                Icon = $"http://media.steampowered.com/steamcommunity/public/images/apps/{gameInfo.AppId}/{gameInfo.ImgIconUrl}.jpg",
                Author = string.Empty,
                Platform = Platforms.Steam,
                Achievements = (await GetPlayerAchievementsAsync("6AE35F7E837A8C2AE407A31835229537", gameInfo.AppId, _steamId.ToString())).ToObservableCollection()
            };
            result.Add(game);
        }
        return result;
    }
    
public static async Task<List<Achievement>> GetPlayerAchievementsAsync(string apiKey, uint appId, string steamUserId)
{
    try
    {
        // Build the request URL.
        string url = $"http://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v0001/?appid={appId}&key={apiKey}&steamid={steamUserId}&l=en";
        
        // Create a cancellation token with a 5-second timeout.
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var response = await _httpClient.GetAsync(url, cts.Token);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var achievements = new List<Achievement>();

            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.TryGetProperty("playerstats", out var playerstats))
                {
                    // If the API indicates failure (e.g. no achievements available), log and return an empty list.
                    if (playerstats.TryGetProperty("success", out var successElement) &&
                        !successElement.GetBoolean())
                    {
                        Log.Warning("Steam API reports that achievements are not available for app {AppId}: {Error}",
                            appId,
                            playerstats.TryGetProperty("error", out var error) ? error.GetString() : "No error message");
                        return new List<Achievement>();
                    }

                    if (playerstats.TryGetProperty("achievements", out var achievementsJson) &&
                        achievementsJson.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var element in achievementsJson.EnumerateArray())
                        {
                            var achievement = new Achievement
                            {
                                // "apiname" is the unique identifier.
                                Id = element.GetProperty("apiname").GetString(),
                                // Optional language-dependent fields.
                                Title = element.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null,
                                Description = element.TryGetProperty("description", out var descProp) ? descProp.GetString() : null,
                                // "achieved" is returned as 1 or 0.
                                IsUnlocked = element.GetProperty("achieved").GetInt32() == 1,
                                // "unlocktime" is a Unix timestamp (seconds since 1970-01-01).
                                UnlockedOn = element.TryGetProperty("unlocktime", out var unlockTimeProp) &&
                                             unlockTimeProp.GetInt64() > 0
                                             ? UnixTimeStampToDateTime(unlockTimeProp.GetInt64())
                                             : (DateTime?)null,
                                IconUrl = null,
                                IsHidden = false,
                                CurrentProgress = null,
                                MaxProgress = null
                            };

                            achievements.Add(achievement);
                        }
                    }
                }
            }

            return achievements;
        }
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error retrieving achievements for app {AppId}", appId);
        return new List<Achievement>();
    }
}
    private static DateTime UnixTimeStampToDateTime(long unixTimeStamp)
    {
        // Unix timestamp is seconds since January 1, 1970 (UTC).
        return DateTimeOffset.FromUnixTimeSeconds(unixTimeStamp).LocalDateTime;
    }
}
