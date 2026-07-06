namespace Alua.Models;
/// <summary>
/// Interface for providers
/// </summary>
public interface IAchievementProvider<TSelf>: IAchievementProvider  where TSelf : IAchievementProvider<TSelf>
{
    /// <summary>
    /// Creates Instance of provider. This is the only member the generic interface adds on top
    /// of <see cref="IAchievementProvider"/>; the library/title methods are inherited as-is.
    /// </summary>
    public static abstract Task<TSelf> Create(string username, CancellationToken cancellationToken = default);
}



public interface IAchievementProvider
{
    /// <summary>
    /// The platform this provider scans. The enum member name doubles as the display name
    /// shown in the scan overlay.
    /// </summary>
    Platforms Platform { get; }

    /// <param name="progress">
    /// Receives per-game scan progress (games completed / total) so callers can drive a
    /// per-provider progress bar while the library is being fetched.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="onGameReady">
    /// Invoked once per game as soon as that game's achievements/trophies finish fetching
    /// (before the rest of the library is done). Lets callers start per-game follow-up work
    /// (e.g. HowLongToBeat lookups) without waiting for the whole provider to complete.
    /// </param>
    Task<Game[]> GetLibrary(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default, Action<Game>? onGameReady = null);

    /// <param name="progress">See <see cref="GetLibrary"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="onGameReady">See <see cref="GetLibrary"/>.</param>
    Task<Game[]> RefreshLibrary(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default, Action<Game>? onGameReady = null);
    Task<Game>   RefreshTitle(string identifier, CancellationToken cancellationToken = default);
}
