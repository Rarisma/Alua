using System.ComponentModel;
using Alua.Models;
using Alua.Services.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Alua.Services;

/// <summary>
/// Caches aggregate statistics to avoid recalculating on every access.
/// Listens to SettingsVM.Games changes and invalidates cache when needed.
/// </summary>
public sealed partial class AggregateStatisticsService : ObservableObject, IDisposable
{
    private readonly SettingsVM _settingsVm;

    // Guards _isDirty and all _cached* fields against concurrent reads during a refresh.
    private readonly object _cacheLock = new();
    private bool _isDirty = true;

    // Cached values
    private int _cachedTotalGames;
    private int _cachedUnlockedCount;
    private int _cachedTotalAchievements;
    private int _cachedPerfectGames;
    private int _cachedPercentComplete;

    public AggregateStatisticsService()
    {
        _settingsVm = Ioc.Default.GetRequiredService<SettingsVM>();
        _settingsVm.PropertyChanged += OnSettingsPropertyChanged;
    }

    /// <summary>
    /// Total number of games in the library.
    /// </summary>
    public int TotalGames
    {
        get
        {
            lock (_cacheLock) { EnsureCalculated(); return _cachedTotalGames; }
        }
    }

    /// <summary>
    /// Total number of unlocked achievements across all games.
    /// </summary>
    public int UnlockedCount
    {
        get
        {
            lock (_cacheLock) { EnsureCalculated(); return _cachedUnlockedCount; }
        }
    }

    /// <summary>
    /// Total number of achievements across all games.
    /// </summary>
    public int TotalAchievements
    {
        get
        {
            lock (_cacheLock) { EnsureCalculated(); return _cachedTotalAchievements; }
        }
    }

    /// <summary>
    /// Number of games with 100% achievement completion.
    /// </summary>
    public int PerfectGames
    {
        get
        {
            lock (_cacheLock) { EnsureCalculated(); return _cachedPerfectGames; }
        }
    }

    /// <summary>
    /// Overall library completion percentage (0-100).
    /// </summary>
    public int PercentComplete
    {
        get
        {
            lock (_cacheLock) { EnsureCalculated(); return _cachedPercentComplete; }
        }
    }

    /// <summary>
    /// Forces recalculation of all statistics on next access.
    /// </summary>
    public void Invalidate()
    {
        lock (_cacheLock) { _isDirty = true; }
    }

    /// <summary>
    /// Recalculates all statistics and notifies property changes.
    /// Call this after games have been updated to trigger UI refresh.
    /// </summary>
    public void Refresh()
    {
        lock (_cacheLock)
        {
            _isDirty = true;
            EnsureCalculated();
        }

        OnPropertyChanged(nameof(TotalGames));
        OnPropertyChanged(nameof(UnlockedCount));
        OnPropertyChanged(nameof(TotalAchievements));
        OnPropertyChanged(nameof(PerfectGames));
        OnPropertyChanged(nameof(PercentComplete));
    }

    // Must be called under _cacheLock.
    private void EnsureCalculated()
    {
        if (!_isDirty) return;

        // When CountMultiPlatformOnce is on, a game owned on multiple platforms / re-released across
        // editions counts once, so totals and "perfect games" aren't inflated for cross-platform users.
        IReadOnlyCollection<Game> source =
            (IReadOnlyCollection<Game>?)_settingsVm.Games?.Values ?? System.Array.Empty<Game>();
        var stats = LibraryStats.Compute(source, _settingsVm.CountMultiPlatformOnce);

        _cachedTotalGames = stats.TotalGames;
        _cachedUnlockedCount = stats.UnlockedCount;
        _cachedTotalAchievements = stats.TotalAchievements;
        _cachedPerfectGames = stats.PerfectGames;
        _cachedPercentComplete = stats.PercentComplete;

        _isDirty = false;
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsVM.Games)
            || e.PropertyName == nameof(SettingsVM.CountMultiPlatformOnce))
        {
            Refresh();
        }
    }

    public void Dispose()
    {
        _settingsVm.PropertyChanged -= OnSettingsPropertyChanged;
    }
}
