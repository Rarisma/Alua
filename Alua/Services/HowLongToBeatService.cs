using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alua.Models;
using HowLongToBeatScraper;

namespace Alua.Services;

public class HowLongToBeatService : IDisposable
{
    private readonly HltbScraper _scraper;
    private readonly Serilog.ILogger _logger;

    // HLTB scrapes a third-party website that breaks periodically. When it's down,
    // every game we look up burns a timeout. Trip after a run of failures and back off.
    // _consecutiveFailures and _circuitOpenUntilTicks are accessed via Interlocked for lock-free coherence.
    private static int _consecutiveFailures;
    private static long _circuitOpenUntilTicks = DateTime.MinValue.Ticks;
    private const int FailureThreshold = 5;
    private static readonly TimeSpan CircuitOpenDuration = TimeSpan.FromMinutes(30);

    public HowLongToBeatService()
    {
        _scraper = new HltbScraper();
        _logger = Serilog.Log.ForContext<HowLongToBeatService>();
    }

    public async Task<HowLongToBeatData?> GetGameData(string gameName)
    {
        if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _circuitOpenUntilTicks))
        {
            _logger.Debug("HLTB circuit open, skipping fetch for {Game}", gameName);
            return null;
        }

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
                Interlocked.Exchange(ref _consecutiveFailures, 0);
                return null;
            }

            // Find the best match - prefer exact name match, otherwise take first result
            var bestMatch = resultsList.FirstOrDefault(x =>
                string.Equals(x.Title, gameName, StringComparison.OrdinalIgnoreCase))
                ?? resultsList.First();

            _logger.Information($"Found HowLongToBeat data for: {bestMatch.Title}");
            Interlocked.Exchange(ref _consecutiveFailures, 0);

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
            var failures = Interlocked.Increment(ref _consecutiveFailures);
            if (failures >= FailureThreshold)
            {
                var openUntil = DateTime.UtcNow + CircuitOpenDuration;
                Interlocked.Exchange(ref _circuitOpenUntilTicks, openUntil.Ticks);
                _logger.Warning("HLTB failed {Count} times in a row; opening circuit until {Until:O}", failures, openUntil);
            }
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
