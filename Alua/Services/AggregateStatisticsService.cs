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

    // Guards _isDirty and all cached fields against concurrent reads during a refresh.
    private readonly object _cacheLock = new();
    private bool _isDirty = true;

    private LibraryMetrics _cachedOverall;
    private Dictionary<Platforms, LibraryMetrics> _cachedPerProvider = new();

    public AggregateStatisticsService()
    {
        _settingsVm = Ioc.Default.GetRequiredService<SettingsVM>();
        _settingsVm.PropertyChanged += OnSettingsPropertyChanged;
    }

    /// <summary>Overall library metrics (deduped per <see cref="SettingsVM.CountMultiPlatformOnce"/>).</summary>
    public LibraryMetrics Overall
    {
        get { lock (_cacheLock) { EnsureCalculated(); return _cachedOverall; } }
    }

    /// <summary>Per-platform metrics. Only platforms with at least one game are present.</summary>
    public IReadOnlyDictionary<Platforms, LibraryMetrics> PerProvider
    {
        get { lock (_cacheLock) { EnsureCalculated(); return _cachedPerProvider; } }
    }

    /// <summary>Total number of games in the library.</summary>
    public int TotalGames { get { lock (_cacheLock) { EnsureCalculated(); return _cachedOverall.TotalGames; } } }

    /// <summary>Total number of unlocked achievements across all games.</summary>
    public int UnlockedCount { get { lock (_cacheLock) { EnsureCalculated(); return _cachedOverall.UnlockedAchievements; } } }

    /// <summary>Total number of achievements across all games.</summary>
    public int TotalAchievements { get { lock (_cacheLock) { EnsureCalculated(); return _cachedOverall.TotalAchievements; } } }

    /// <summary>Number of games with 100% achievement completion.</summary>
    public int PerfectGames { get { lock (_cacheLock) { EnsureCalculated(); return _cachedOverall.PerfectGames; } } }

    /// <summary>Overall library completion percentage (0-100).</summary>
    public int PercentComplete { get { lock (_cacheLock) { EnsureCalculated(); return _cachedOverall.PercentComplete; } } }

    /// <summary>Forces recalculation of all statistics on next access.</summary>
    public void Invalidate()
    {
        lock (_cacheLock) { _isDirty = true; }
    }

    /// <summary>Recalculates all statistics and notifies property changes.</summary>
    public void Refresh()
    {
        lock (_cacheLock)
        {
            _isDirty = true;
            EnsureCalculated();
        }

        OnPropertyChanged(nameof(Overall));
        OnPropertyChanged(nameof(PerProvider));
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

        IReadOnlyCollection<Game> source =
            (IReadOnlyCollection<Game>?)_settingsVm.Games?.Values ?? Array.Empty<Game>();
        var now = DateTime.UtcNow;

        _cachedOverall = LibraryMetrics.Compute(source, _settingsVm.CountMultiPlatformOnce, now);

        var perProvider = new Dictionary<Platforms, LibraryMetrics>();
        foreach (var platformGames in source.GroupBy(g => g.Platform))
            perProvider[platformGames.Key] = LibraryMetrics.Compute(platformGames.ToList(), dedupe: true, now);
        _cachedPerProvider = perProvider;

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
