namespace Alua.Models;
/// <summary>
/// Interface for providers
/// </summary>
public interface IAchievementProvider<TSelf>: IAchievementProvider  where TSelf : IAchievementProvider<TSelf>
{
    /// <summary>
    /// Creates Instance of provider
    /// </summary>
    public static abstract Task<TSelf> Create(string username, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the users whole library
    /// </summary>
    /// <returns>Array of Games</returns>
    new Task<Game[]> GetLibrary(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recently played games by the user
    /// </summary>
    /// <returns>Array of games</returns>
    new Task<Game[]> RefreshLibrary(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates data for a single title
    /// </summary>
    /// <param name="identifier">Game Identifier</param>
    /// <returns>Game Object</returns>
    new Task<Game> RefreshTitle(string identifier, CancellationToken cancellationToken = default);
}



public interface IAchievementProvider
{
    Task<Game[]> GetLibrary(CancellationToken cancellationToken = default);
    Task<Game[]> RefreshLibrary(CancellationToken cancellationToken = default);
    Task<Game>   RefreshTitle(string identifier, CancellationToken cancellationToken = default);
}
