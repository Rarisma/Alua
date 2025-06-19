namespace Alua.Models;
/// <summary>
/// Interface for providers
/// </summary>
public interface IAchievementProvider<TSelf>: IAchievementProvider  where TSelf : IAchievementProvider<TSelf>
{
    /// <summary>
    /// Creates Instance of provider
    /// </summary>
    public static abstract Task<TSelf> Create(string username);
    
    /// <summary>
    /// Gets the users whole library
    /// </summary>
    /// <returns>Array of Games</returns>
    public Task<Game[]> GetLibrary();
    
    /// <summary>
    /// Gets recently played games by the user
    /// </summary>
    /// <returns>Array of games</returns>
    public Task<Game[]> RefreshLibrary();
    
    /// <summary>
    /// Updates data for a single title
    /// </summary>
    /// <param name="identifier">Game Identifier</param>
    /// <returns>Game Object</returns>
    public Task<Game> RefreshTitle(string identifier);
}


public interface IAchievementProvider
{
    Task<Game[]> GetLibrary();
    Task<Game[]> RefreshLibrary();
    Task<Game>   RefreshTitle(string identifier);
}
