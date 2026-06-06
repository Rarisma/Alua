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

    /// <summary>
    /// The outcome of an HLTB lookup, distinguishing a real "no such game" miss (which is safe to
    /// negative-cache) from a lookup we never actually performed (circuit open / error / cancel),
    /// which must stay retriable so we don't poison the cache with a transient skip.
    /// </summary>
    public enum HltbFetchStatus
    {
        /// A search returned a usable match (<see cref="HltbFetchResult.Data"/> is set).
        Match,
        /// A search completed but HLTB has no entry for this title — safe to negative-cache.
        NoMatch,
        /// The lookup was not performed (circuit open or transient error) — must stay retriable.
        Skipped
    }

    public readonly record struct HltbFetchResult(HltbFetchStatus Status, HowLongToBeatData? Data)
    {
        public static readonly HltbFetchResult Skipped = new(HltbFetchStatus.Skipped, null);
        public static readonly HltbFetchResult NoMatch = new(HltbFetchStatus.NoMatch, null);
        public static HltbFetchResult Match(HowLongToBeatData data) => new(HltbFetchStatus.Match, data);
    }

    /// <summary>
    /// Looks a game up on HowLongToBeat. Never throws except for cancellation, which is rethrown so
    /// the caller can stop a batch promptly (and so a user cancel does not count as a service failure).
    /// </summary>
    public async Task<HltbFetchResult> GetGameData(string gameName, CancellationToken cancellationToken = default)
    {
        if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _circuitOpenUntilTicks))
        {
            _logger.Debug("HLTB circuit open, skipping fetch for {Game}", gameName);
            return HltbFetchResult.Skipped;
        }

        if (string.IsNullOrWhiteSpace(gameName))
        {
            _logger.Warning("Game name is null or empty");
            return HltbFetchResult.Skipped;
        }

        try
        {
            _logger.Information($"Searching HowLongToBeat for: {gameName}");

            var searchResults = await _scraper.Search(gameName, cancellationToken: cancellationToken);
            var resultsList = searchResults?.ToList();

            if (resultsList == null || resultsList.Count == 0)
            {
                _logger.Information($"No results found for: {gameName}");
                Interlocked.Exchange(ref _consecutiveFailures, 0);
                return HltbFetchResult.NoMatch;
            }

            // Find the best match - prefer exact name match, otherwise take first result
            var bestMatch = resultsList.FirstOrDefault(x =>
                string.Equals(x.Title, gameName, StringComparison.OrdinalIgnoreCase))
                ?? resultsList.First();

            _logger.Information($"Found HowLongToBeat data for: {bestMatch.Title}");
            Interlocked.Exchange(ref _consecutiveFailures, 0);

            return HltbFetchResult.Match(new HowLongToBeatData
            {
                MainStory = bestMatch.MainStory,
                MainPlusExtras = bestMatch.MainStoryWithExtras,
                Completionist = bestMatch.Completionist,
                AllStyles = null // Not available in this API version
            });
        }
        catch (OperationCanceledException)
        {
            // User cancelled — not a service failure. Don't trip the breaker or cache anything.
            throw;
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
            return HltbFetchResult.Skipped;
        }
    }

    /// <summary>
    /// Fetches HowLongToBeat data for a game and updates its properties if not already cached.
    /// A genuine no-match is negative-cached (HowLongToBeatLastFetched is stamped) so unindexed
    /// titles stop re-scraping on every scan; a skipped lookup (circuit open / error) is left
    /// unstamped so it is retried. Cancellation propagates to the caller.
    /// </summary>
    public async Task FetchAndUpdateGameData(Game game, CancellationToken cancellationToken = default)
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

        HltbFetchResult result;
        try
        {
            result = await GetGameData(game.Name, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Propagate cancellation so the caller can stop the batch promptly.
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Failed to fetch HowLongToBeat data for {game.Name}");
            return;
        }

        switch (result.Status)
        {
            case HltbFetchStatus.Match:
                var data = result.Data!;
                game.HowLongToBeatMain = data.MainStory;
                game.HowLongToBeatMainExtras = data.MainPlusExtras;
                game.HowLongToBeatCompletionist = data.Completionist;
                game.HowLongToBeatAllStyles = data.AllStyles;
                game.HowLongToBeatLastFetched = DateTime.UtcNow;
                _logger.Information($"Updated HowLongToBeat data for {game.Name}");
                break;

            case HltbFetchStatus.NoMatch:
                // Negative-cache: stamp so this unindexed title isn't re-scraped every scan/refresh.
                game.HowLongToBeatLastFetched = DateTime.UtcNow;
                _logger.Information($"No HowLongToBeat match for {game.Name}; negative-caching for the normal TTL");
                break;

            case HltbFetchStatus.Skipped:
                // Circuit open or transient error — leave unstamped so it's retried next time.
                break;
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
