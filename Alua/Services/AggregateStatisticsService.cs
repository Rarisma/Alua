using System.ComponentModel;
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
            EnsureCalculated();
            return _cachedTotalGames;
        }
    }

    /// <summary>
    /// Total number of unlocked achievements across all games.
    /// </summary>
    public int UnlockedCount
    {
        get
        {
            EnsureCalculated();
            return _cachedUnlockedCount;
        }
    }

    /// <summary>
    /// Total number of achievements across all games.
    /// </summary>
    public int TotalAchievements
    {
        get
        {
            EnsureCalculated();
            return _cachedTotalAchievements;
        }
    }

    /// <summary>
    /// Number of games with 100% achievement completion.
    /// </summary>
    public int PerfectGames
    {
        get
        {
            EnsureCalculated();
            return _cachedPerfectGames;
        }
    }

    /// <summary>
    /// Overall library completion percentage (0-100).
    /// </summary>
    public int PercentComplete
    {
        get
        {
            EnsureCalculated();
            return _cachedPercentComplete;
        }
    }

    /// <summary>
    /// Forces recalculation of all statistics on next access.
    /// </summary>
    public void Invalidate()
    {
        _isDirty = true;
    }

    /// <summary>
    /// Recalculates all statistics and notifies property changes.
    /// Call this after games have been updated to trigger UI refresh.
    /// </summary>
    public void Refresh()
    {
        Invalidate();
        EnsureCalculated();

        OnPropertyChanged(nameof(TotalGames));
        OnPropertyChanged(nameof(UnlockedCount));
        OnPropertyChanged(nameof(TotalAchievements));
        OnPropertyChanged(nameof(PerfectGames));
        OnPropertyChanged(nameof(PercentComplete));
    }

    private void EnsureCalculated()
    {
        if (!_isDirty) return;

        var games = _settingsVm.Games;
        if (games == null || games.Count == 0)
        {
            _cachedTotalGames = 0;
            _cachedUnlockedCount = 0;
            _cachedTotalAchievements = 0;
            _cachedPerfectGames = 0;
            _cachedPercentComplete = 0;
        }
        else
        {
            _cachedTotalGames = games.Count;
            _cachedUnlockedCount = 0;
            _cachedTotalAchievements = 0;
            _cachedPerfectGames = 0;

            foreach (var kvp in games)
            {
                var game = kvp.Value;
                var achievementCount = game.Achievements.Count;
                var unlockedCount = game.UnlockedCount;

                _cachedTotalAchievements += achievementCount;
                _cachedUnlockedCount += unlockedCount;

                if (game.HasAchievements && achievementCount == unlockedCount)
                {
                    _cachedPerfectGames++;
                }
            }

            _cachedPercentComplete = _cachedTotalAchievements == 0
                ? 0
                : _cachedUnlockedCount * 100 / _cachedTotalAchievements;
        }

        _isDirty = false;
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsVM.Games))
        {
            Refresh();
        }
    }

    public void Dispose()
    {
        _settingsVm.PropertyChanged -= OnSettingsPropertyChanged;
    }
}
