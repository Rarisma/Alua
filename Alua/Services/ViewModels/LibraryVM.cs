using Alua.Models;
using static Alua.Services.ViewModels.OrderBy;

namespace Alua.Services.ViewModels;

/// <summary>
/// Captures all filter/sort/search parameters needed to compute a filtered game list.
/// Passed from the UI thread to <see cref="LibraryVM.FilterAndSort"/> which runs on a background thread.
/// </summary>
public sealed record FilterArgs(
    bool HideComplete,
    bool HideNoAchievements,
    bool HideUnstarted,
    bool Reverse,
    string? SearchText,
    OrderBy OrderBy,
    bool SteamFilter,
    bool RAFilter,
    bool PSNFilter,
    bool XBFilter);

/// <summary>
/// ViewModel that owns per-platform filter toggles and the pure filter/sort logic
/// for the library game list.
/// </summary>
public partial class LibraryVM : ObservableObject
{
    /// <summary>
    /// Are steam games shown
    /// </summary>
    [ObservableProperty] private bool _steamFilter = true;

    /// <summary>
    /// Are RetroAchievement games shown
    /// </summary>
    [ObservableProperty] private bool _RAFilter = true;

    /// <summary>
    /// Are PSN games shown
    /// </summary>
    [ObservableProperty] private bool _PSNFilter = true;

    /// <summary>
    /// Are Xbox Games shown?
    /// </summary>
    [ObservableProperty] private bool _XBFilter = true;

    /// <summary>
    /// Applies all filter, search, sort, and reverse operations to <paramref name="games"/>
    /// and returns the resulting ordered list. Safe to call from a background thread.
    /// </summary>
    /// <param name="games">Snapshot of games to filter; must not be mutated while this runs.</param>
    /// <param name="args">Captured filter/sort parameters.</param>
    public static List<Game> FilterAndSort(IReadOnlyCollection<Game> games, FilterArgs args)
    {
        IEnumerable<Game> list = games;

        // Completion / achievement / started filters
        if (args.HideComplete)
            list = list.Where(g => g.UnlockedCount < g.Achievements.Count);
        if (args.HideNoAchievements)
            list = list.Where(g => g.HasAchievements);
        if (args.HideUnstarted)
            list = list.Where(g => g.UnlockedCount > 0);

        // Text search
        if (!string.IsNullOrWhiteSpace(args.SearchText))
            list = list.Where(g => g.Name.Contains(args.SearchText, StringComparison.OrdinalIgnoreCase));

        // Pre-filter for HLTB sort modes (omit games without the required data)
        if (args.OrderBy == HowLongToBeatMain)
            list = list.Where(g => g.HowLongToBeatMain.HasValue);
        else if (args.OrderBy == HowLongToBeatCompletionist)
            list = list.Where(g => g.HowLongToBeatCompletionist.HasValue);

        // Sort
        list = args.OrderBy switch
        {
            OrderBy.Name             => list.OrderBy(g => g.Name),
            NameReverse              => list.OrderByDescending(g => g.Name),
            // Guard against zero-achievement games to avoid NaN / DivideByZero
            CompletionPct            => list.OrderByDescending(g =>
                                           g.Achievements.Count == 0
                                               ? 0.0
                                               : (double)g.UnlockedCount / g.Achievements.Count),
            TotalCount               => list.OrderBy(g => g.Achievements.Count),
            UnlockedCount            => list.OrderBy(g => g.UnlockedCount),
            Playtime                 => list.OrderBy(g => g.PlaytimeMinutes),
            LastPlayed               => list.OrderByDescending(g => g.LastPlayed ?? DateTime.MinValue),
            HowLongToBeatMain        => list.OrderBy(g => g.HowLongToBeatMain!.Value),
            HowLongToBeatCompletionist => list.OrderBy(g => g.HowLongToBeatCompletionist!.Value),
            _                        => list
        };

        // Reverse the sort if requested
        if (args.Reverse)
            list = list.Reverse();

        // Per-platform filtering — use TryGetValue so unknown Platforms pass through
        var enabled = new Dictionary<Platforms, bool>
        {
            { Platforms.Steam,             args.SteamFilter },
            { Platforms.RetroAchievements, args.RAFilter    },
            { Platforms.PlayStation,       args.PSNFilter   },
            { Platforms.Xbox,              args.XBFilter    },
        };

        list = list.Where(g => !enabled.TryGetValue(g.Platform, out var show) || show);

        return list.ToList();
    }
}
