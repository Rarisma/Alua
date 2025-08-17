using System;
using System.Linq;
using System.Threading.Tasks;
using Alua.Models;
using HowLongToBeatScraper;

namespace Alua.Services;

public class HowLongToBeatService : IDisposable
{
    private readonly HltbScraper _scraper;
    private readonly Serilog.ILogger _logger;

    public HowLongToBeatService()
    {
        _scraper = new HltbScraper();
        _logger = Serilog.Log.ForContext<HowLongToBeatService>();
    }

    public async Task<HowLongToBeatData?> GetGameData(string gameName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(gameName))
            {
                _logger.Warning("Game name is null or empty");
                return null;
            }

            _logger.Information($"Searching HowLongToBeat for: {gameName}");
            
            var searchResults = await _scraper.Search(gameName);
            var resultsList = searchResults?.ToList();
            
            if (resultsList == null || !resultsList.Any())
            {
                _logger.Information($"No results found for: {gameName}");
                return null;
            }

            // Find the best match - prefer exact name match, otherwise take first result
            var bestMatch = resultsList.FirstOrDefault(x => 
                string.Equals(x.Title, gameName, StringComparison.OrdinalIgnoreCase)) 
                ?? resultsList.First();

            _logger.Information($"Found HowLongToBeat data for: {bestMatch.Title}");
            
            return new HowLongToBeatData
            {
                MainStory = bestMatch.MainStory,
                MainPlusExtras = bestMatch.MainStoryWithExtras,
                Completionist = bestMatch.Completionist,
                AllStyles = null // Not available in this API version
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Error fetching HowLongToBeat data for: {gameName}");
            return null;
        }
    }
    
    /// <summary>
    /// Fetches HowLongToBeat data for a game and updates its properties if not already cached
    /// </summary>
    public async Task FetchAndUpdateGameData(Game game)
    {
        try
        {
            if (game == null || string.IsNullOrWhiteSpace(game.Name))
            {
                return;
            }
            
            // Check if we already have recent data (less than 7 days old)
            if (game.HowLongToBeatLastFetched.HasValue &&
                (DateTime.UtcNow - game.HowLongToBeatLastFetched.Value).TotalDays < 7)
            {
                _logger.Information($"Using cached HowLongToBeat data for {game.Name}");
                return;
            }
            
            var hltbData = await GetGameData(game.Name);
            
            if (hltbData != null)
            {
                game.HowLongToBeatMain = hltbData.MainStory;
                game.HowLongToBeatMainExtras = hltbData.MainPlusExtras;
                game.HowLongToBeatCompletionist = hltbData.Completionist;
                game.HowLongToBeatAllStyles = hltbData.AllStyles;
                game.HowLongToBeatLastFetched = DateTime.UtcNow;
                
                _logger.Information($"Updated HowLongToBeat data for {game.Name}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Failed to fetch HowLongToBeat data for {game?.Name}");
        }
    }
    
    public void Dispose()
    {
        _scraper?.Dispose();
    }
}

public class HowLongToBeatData
{
    public double? MainStory { get; set; }
    public double? MainPlusExtras { get; set; }
    public double? Completionist { get; set; }
    public double? AllStyles { get; set; }
}
