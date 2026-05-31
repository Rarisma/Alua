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
    Task<Game[]> GetLibrary(CancellationToken cancellationToken = default);
    Task<Game[]> RefreshLibrary(CancellationToken cancellationToken = default);
    Task<Game>   RefreshTitle(string identifier, CancellationToken cancellationToken = default);
}
