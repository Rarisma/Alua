using System.Text.RegularExpressions;
using Alua.Models;
using Alua.Services.ViewModels;

namespace Alua.Services;

/// <summary>
/// Collapses duplicate / re-released / subset games into a single representative ("primary")
/// game that carries the rest in its <see cref="Game.Editions"/> list, for display as one
/// library card with per-edition tabs.
///
/// Grouping is purely a display concern: it mutates only the in-memory <c>[JsonIgnore]</c>
/// members on <see cref="Game"/> (<see cref="Game.Editions"/>, <see cref="Game.MergedDisplayName"/>)
/// and never touches persisted data, so it is safe to recompute on every refresh.
/// </summary>
public static class GameGrouping
{
    // Bracketed / parenthetical segments: "[Subset - Bonus]", "(PS5)", "(2016)", "{...}".
    private static readonly Regex BracketsRegex = new(@"[\(\[\{][^\)\]\}]*[\)\]\}]", RegexOptions.Compiled);

    // Anything that isn't a lowercase letter, digit, or apostrophe becomes a space.
    private static readonly Regex NonWordRegex = new(@"[^a-z0-9']+", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Trailing "edition" qualifiers to peel off the end of a title, tried in order and repeated
    /// until the title stops changing. Multi-word phrases come first so they strip as a unit
    /// before the generic "&lt;word&gt; edition" rule. Crucially this never removes digits or
    /// "season"/"episode" tokens, so sequels ("Portal" vs "Portal 2") and seasons
    /// ("Story Mode Season 1" vs "Season 2") stay distinct.
    /// </summary>
    private static readonly Regex[] EditionSuffixes =
    [
        new(@"\bgame of the year edition$", RegexOptions.Compiled),
        new(@"\bgame of the year$", RegexOptions.Compiled),
        new(@"\bgoty edition$", RegexOptions.Compiled),
        new(@"\bdirector'?s cut$", RegexOptions.Compiled),
        new(@"\bfinal cut$", RegexOptions.Compiled),
        new(@"\bdefinitive edition$", RegexOptions.Compiled),
        new(@"\bcomplete edition$", RegexOptions.Compiled),
        new(@"\bremastered edition$", RegexOptions.Compiled),
        new(@"\bspecial edition$", RegexOptions.Compiled),
        new(@"\banniversary edition$", RegexOptions.Compiled),
        new(@"\blegendary edition$", RegexOptions.Compiled),
        // Generic "<word> edition" — covers Ultimate / Deluxe / Java / Bedrock / Gold / ...
        new(@"\b[a-z0-9']+ edition$", RegexOptions.Compiled),
        new(@"\bedition$", RegexOptions.Compiled),
        // Standalone trailing qualifiers
        new(@"\bremastered$", RegexOptions.Compiled),
        new(@"\bremaster$", RegexOptions.Compiled),
        new(@"\bredux$", RegexOptions.Compiled),
        new(@"\bgoty$", RegexOptions.Compiled),
        new(@"\banniversary$", RegexOptions.Compiled),
        new(@"\bdefinitive$", RegexOptions.Compiled),
        new(@"\benhanced$", RegexOptions.Compiled),
        new(@"\bdeluxe$", RegexOptions.Compiled),
        new(@"\bultimate$", RegexOptions.Compiled),
    ];

    /// <summary>
    /// Reduces a game title to a comparison key by stripping bracketed tags, trademark symbols,
    /// punctuation, and trailing edition qualifiers. Two titles that normalize to the same
    /// non-empty string are treated as the same game.
    /// </summary>
    public static string NormalizeTitle(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var s = BracketsRegex.Replace(name, " ");
        s = s.Replace("™", "").Replace("®", "").Replace("©", ""); // ™ ® ©
        s = s.ToLowerInvariant();
        s = NonWordRegex.Replace(s, " ");
        s = WhitespaceRegex.Replace(s, " ").Trim();
        if (s.Length == 0)
            return string.Empty;

        // Peel trailing edition qualifiers until stable (handles e.g. "X Ultimate Edition").
        bool changed = true;
        int guard = 0;
        while (changed && guard++ < 10)
        {
            changed = false;
            foreach (var rx in EditionSuffixes)
            {
                var next = rx.Replace(s, "").Trim();
                next = WhitespaceRegex.Replace(next, " ").Trim();
                // Never strip the title away to nothing (e.g. a game literally named "Ultimate").
                if (next.Length > 0 && next != s)
                {
                    s = next;
                    changed = true;
                }
            }
        }

        return s;
    }

    /// <summary>
    /// Groups <paramref name="games"/> into one representative per merged title. The returned
    /// list contains the chosen primary of each group, with <see cref="Game.Editions"/> and
    /// <see cref="Game.MergedDisplayName"/> populated on merged primaries.
    /// </summary>
    /// <param name="games">Snapshot of all games. The objects are mutated (display fields only).</param>
    /// <param name="include">
    /// Optional per-edition filter (e.g. "platform is enabled"). When supplied, only matching
    /// editions are considered when choosing a group's representative, building its edition list,
    /// and its display name — so disabling a platform actually hides that platform's data even on a
    /// merged card. A group with no matching editions drops out entirely. Parent resolution still
    /// uses the full set, so a filtered-out parent can still key its visible children.
    /// </param>
    public static List<Game> Group(
        IReadOnlyCollection<Game> games,
        Func<Game, bool>? include = null,
        MergedCompletionMode mode = MergedCompletionMode.Best)
    {
        // Reset display state from any prior pass so stale merges never linger. Assign a fresh
        // list rather than clearing in place: an already-open game page may still hold a reference
        // to the previous Editions list, and clearing it would empty that page's tab strip.
        foreach (var g in games)
        {
            g.Editions = new List<Game>();
            g.MergedDisplayName = null;
            g.DisplayUnlocked = null;
            g.DisplayTotal = null;
        }

        // Index by identifier so a child (RA subset) can resolve to its parent. Identifiers are
        // unique in the source dictionary; GroupBy().First() is defensive against accidental dupes.
        var byId = games
            .Where(g => !string.IsNullOrEmpty(g.Identifier))
            .GroupBy(g => g.Identifier)
            .ToDictionary(grp => grp.Key, grp => grp.First());

        var groups = new Dictionary<string, List<Game>>(StringComparer.Ordinal);

        foreach (var game in games)
        {
            var key = GroupKey(game, byId);
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<Game>();
                groups[key] = list;
            }
            list.Add(game);
        }

        var representatives = new List<Game>(groups.Count);
        foreach (var group in groups.Values)
        {
            // Restrict to the editions we're allowed to display (e.g. on an enabled platform).
            // A group with no displayable editions drops out entirely.
            var members = include == null ? group : group.Where(include).ToList();
            if (members.Count == 0)
                continue;

            if (members.Count == 1)
            {
                representatives.Add(members[0]);
                continue;
            }

            // Primary = the edition the user has engaged with most.
            var ordered = members
                .OrderByDescending(g => g.UnlockedCount)
                .ThenByDescending(g => g.Achievements.Count)
                .ThenByDescending(g => g.PlaytimeMinutes)
                .ThenByDescending(g => g.LastPlayed ?? DateTime.MinValue)
                .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var primary = ordered[0];

            // Card / header label = the shortest (cleanest) edition name, e.g. "Control".
            primary.MergedDisplayName = members
                .OrderBy(g => g.Name.Length)
                .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .First().Name;

            primary.Editions = ordered;

            // In Aggregate mode the card reports completion summed across the group's editions; in
            // Best mode it leaves the display counts null so the card uses the primary's own counts.
            if (mode == MergedCompletionMode.Aggregate)
            {
                primary.DisplayUnlocked = members.Sum(g => g.UnlockedCount);
                primary.DisplayTotal = members.Sum(g => g.Achievements.Count);
            }

            representatives.Add(primary);
        }

        return representatives;
    }

    /// <summary>
    /// Buckets <paramref name="games"/> by the same merge key <see cref="Group"/> uses, WITHOUT
    /// mutating any display state (<see cref="Game.Editions"/> / <see cref="Game.MergedDisplayName"/>).
    /// Safe to call off the display path — used by statistics to de-duplicate multi-platform
    /// ownership without disturbing the library's current grouping.
    /// </summary>
    public static ILookup<string, Game> KeyGames(IReadOnlyCollection<Game> games)
    {
        var byId = games
            .Where(g => !string.IsNullOrEmpty(g.Identifier))
            .GroupBy(g => g.Identifier)
            .ToDictionary(grp => grp.Key, grp => grp.First());

        return games.ToLookup(g => GroupKey(g, byId), StringComparer.Ordinal);
    }

    /// <summary>
    /// The merge key for a game: an RA subset (or any child) resolves to the normalized title of
    /// its in-library parent (following the parent chain to the root); everything else keys on its
    /// own normalized title. Falls back to the raw identifier when a title normalizes to nothing.
    /// </summary>
    private static string GroupKey(Game game, IReadOnlyDictionary<string, Game> byId)
    {
        var root = game;
        int guard = 0;
        while (!string.IsNullOrEmpty(root.ParentIdentifier)
               && byId.TryGetValue(root.ParentIdentifier!, out var parent)
               && !ReferenceEquals(parent, root)
               && guard++ < 8)
        {
            root = parent;
        }

        var normalized = NormalizeTitle(root.Name);
        return normalized.Length > 0 ? "name:" + normalized : "id:" + root.Identifier;
    }
}
