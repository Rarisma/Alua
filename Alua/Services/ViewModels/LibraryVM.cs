using Alua.Models;
using Alua.Services;
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
    bool XBFilter,
    bool MergeEditions,
    bool ShowOnlyMerged,
    MergedCompletionMode MergedCompletionMode = MergedCompletionMode.Best);

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
    /// Whether duplicate / edition / subset games are collapsed into one card with per-edition tabs.
    /// </summary>
    [ObservableProperty] private bool _mergeEditions = true;

    /// <summary>
    /// Debug filter: show only games that merged into multiple editions / subsets. Transient
    /// (not persisted) — resets on restart. Forces grouping even when <see cref="MergeEditions"/>
    /// is off, so it surfaces what would merge.
    /// </summary>
    [ObservableProperty] private bool _showOnlyMerged;

    /// <summary>
    /// Applies all filter, search, sort, and reverse operations to <paramref name="games"/>
    /// and returns the resulting ordered list. Safe to call from a background thread.
    /// </summary>
    /// <param name="games">Snapshot of games to filter; must not be mutated while this runs.</param>
    /// <param name="args">Captured filter/sort parameters.</param>
    public static List<Game> FilterAndSort(IReadOnlyCollection<Game> games, FilterArgs args)
    {
        // Whether a game's platform is currently enabled by the per-platform toggles. Unknown
        // platforms always pass through.
        bool Allowed(Platforms p) => p switch
        {
            Platforms.Steam             => args.SteamFilter,
            Platforms.RetroAchievements => args.RAFilter,
            Platforms.PlayStation       => args.PSNFilter,
            Platforms.Xbox              => args.XBFilter,
            _                           => true
        };

        // Collapse duplicate / edition / subset games into one representative per group first;
        // every subsequent filter and sort then operates on the (primary) representative's stats.
        // The platform filter is applied at the *edition* level: a disabled platform's editions are
        // excluded before the representative is chosen, so disabling a platform truly hides its data
        // even on a merged card (and a group left with no enabled editions disappears). When grouping
        // is off it applies per game. The debug "show only merged" filter forces grouping even when
        // merging is off, so it can surface which games would merge.
        bool group = args.MergeEditions || args.ShowOnlyMerged;
        IReadOnlyCollection<Game> source = group
            ? GameGrouping.Group(games, g => Allowed(g.Platform), args.MergedCompletionMode)
            : games.Where(g => Allowed(g.Platform)).ToList();

        IEnumerable<Game> list = source;

        // Debug: keep only games that merged into multiple editions / subsets.
        if (args.ShowOnlyMerged)
            list = list.Where(g => g.IsMerged);

        // Completion / achievement / started filters
        if (args.HideComplete)
            list = list.Where(g => g.UnlockedCount < g.Achievements.Count);
        if (args.HideNoAchievements)
            list = list.Where(g => g.HasAchievements);
        if (args.HideUnstarted)
            list = list.Where(g => g.UnlockedCount > 0);

        // Text search — matches across all editions of a merged card, not just the representative,
        // so searching a non-primary edition's title still finds the card.
        if (!string.IsNullOrWhiteSpace(args.SearchText))
            list = list.Where(g => MatchesSearch(g, args.SearchText));

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

        return list.ToList();
    }

    /// <summary>
    /// True when the search term matches a game's name, its merged display name, or the name of any
    /// edition collapsed under it — so a non-primary edition's title still surfaces the merged card.
    /// </summary>
    private static bool MatchesSearch(Game game, string term)
    {
        if (game.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrEmpty(game.MergedDisplayName)
            && game.MergedDisplayName.Contains(term, StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (var edition in game.Editions)
            if (edition.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
