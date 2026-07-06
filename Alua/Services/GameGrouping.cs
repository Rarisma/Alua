using System.Collections.ObjectModel;
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

    private static readonly IReadOnlyList<Platforms> DefaultPlatformPriority =
        new[] { Platforms.Steam, Platforms.PlayStation, Platforms.Xbox, Platforms.RetroAchievements };

    /// <summary>
    /// Groups <paramref name="games"/> into one representative per card, per <paramref name="editionMode"/>.
    /// </summary>
    /// <param name="games">Snapshot of all games. The objects are mutated (display fields only).</param>
    /// <param name="include">
    /// Optional per-edition filter (e.g. "platform is enabled"). A group with no matching editions
    /// drops out entirely. Parent resolution still uses the full set, so a filtered-out parent can
    /// still key its visible children.
    /// </param>
    /// <param name="editionMode">
    /// <see cref="EditionDisplayMode.DontMerge"/>: every title-alike edition is its own card; RA
    /// subsets still attach to their parent. <see cref="EditionDisplayMode.Merge"/>: title-alike
    /// editions collapse into one card, primary = most progress, all editions shown as tabs.
    /// <see cref="EditionDisplayMode.PriorityOnly"/>: same grouping as Merge, but primary = the
    /// highest-<paramref name="platformPriority"/> platform (ties broken by progress); other
    /// editions are dropped entirely (no tabs), though the shown edition's own RA subsets remain.
    /// </param>
    /// <param name="completionMode">Only consulted in <see cref="EditionDisplayMode.Merge"/>.</param>
    /// <param name="platformPriority">
    /// Only consulted in <see cref="EditionDisplayMode.PriorityOnly"/>; defaults to
    /// Steam, PlayStation, Xbox, RetroAchievements when null.
    /// </param>
    public static List<Game> Group(
        IReadOnlyCollection<Game> games,
        Func<Game, bool>? include = null,
        EditionDisplayMode editionMode = EditionDisplayMode.Merge,
        MergedCompletionMode completionMode = MergedCompletionMode.Best,
        IReadOnlyList<Platforms>? platformPriority = null)
    {
        // Reset display state from any prior pass so stale merges never linger. Assign a fresh
        // list rather than clearing in place: an already-open game page may still hold a reference
        // to the previous Editions list, and clearing it would empty that page's tab strip.
        foreach (var g in games)
        {
            g.Editions = new ObservableCollection<Game>();
            g.MergedDisplayName = null;
            g.DisplayUnlocked = null;
            g.DisplayTotal = null;
        }

        var byId = games
            .Where(g => !string.IsNullOrEmpty(g.Identifier))
            .GroupBy(g => g.Identifier)
            .ToDictionary(grp => grp.Key, grp => grp.First());

        if (editionMode == EditionDisplayMode.DontMerge)
            return GroupSubsetsOnly(games, byId, include);

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

        var priority = platformPriority ?? DefaultPlatformPriority;
        var rank = new Dictionary<Platforms, int>();
        for (int i = 0; i < priority.Count; i++)
            rank.TryAdd(priority[i], i);
        int Rank(Game g) => rank.TryGetValue(g.Platform, out var r) ? r : int.MaxValue;

        var representatives = new List<Game>(groups.Count);
        foreach (var group in groups.Values)
        {
            var members = include == null ? group : group.Where(include).ToList();
            if (members.Count == 0)
                continue;

            if (members.Count == 1)
            {
                representatives.Add(members[0]);
                continue;
            }

            if (editionMode == EditionDisplayMode.PriorityOnly)
            {
                // Primary must be a root-level edition — an RA subset can never be promoted over
                // its own parent, even if the subset has more progress. Fall back to all members
                // only if no root candidate survived filtering (e.g. the root's platform is disabled).
                var rootCandidates = members.Where(m => ReferenceEquals(ResolveSubsetRoot(m, byId), m)).ToList();
                var candidates = rootCandidates.Count > 0 ? rootCandidates : members;

                var primary = candidates
                    .OrderBy(Rank)
                    .ThenByDescending(g => g.UnlockedCount)
                    .ThenByDescending(g => g.Achievements.Count)
                    .ThenByDescending(g => g.PlaytimeMinutes)
                    .ThenByDescending(g => g.LastPlayed ?? DateTime.MinValue)
                    .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                    .First();

                // Alternates on other platforms are dropped entirely, but the chosen edition's own
                // RA subsets (resolved root == primary) are never hideable, so keep those as tabs.
                var ownSubsets = members
                    .Where(m => !ReferenceEquals(m, primary) && ReferenceEquals(ResolveSubsetRoot(m, byId), primary))
                    .ToList();
                if (ownSubsets.Count > 0)
                {
                    var orderedSubsets = ownSubsets
                        .OrderByDescending(g => g.UnlockedCount)
                        .ThenByDescending(g => g.Achievements.Count)
                        .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase);
                    primary.Editions = new[] { primary }.Concat(orderedSubsets).ToObservableCollection();
                }

                representatives.Add(primary);
                continue;
            }

            // Merge mode: primary = the edition the user has engaged with most.
            var ordered = members
                .OrderByDescending(g => g.UnlockedCount)
                .ThenByDescending(g => g.Achievements.Count)
                .ThenByDescending(g => g.PlaytimeMinutes)
                .ThenByDescending(g => g.LastPlayed ?? DateTime.MinValue)
                .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var mergedPrimary = ordered[0];

            // Card / header label = the shortest (cleanest) edition name, e.g. "Control".
            mergedPrimary.MergedDisplayName = members
                .OrderBy(g => g.Name.Length)
                .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .First().Name;

            mergedPrimary.Editions = ordered.ToObservableCollection();

            if (completionMode == MergedCompletionMode.Aggregate)
            {
                mergedPrimary.DisplayUnlocked = members.Sum(g => g.UnlockedCount);
                mergedPrimary.DisplayTotal = members.Sum(g => g.Achievements.Count);
            }

            representatives.Add(mergedPrimary);
        }

        return representatives;
    }

    /// <summary>
    /// <see cref="EditionDisplayMode.DontMerge"/> grouping: buckets purely by subset-root identity
    /// (no title normalization), so title-alike editions never merge, but an RA subset always
    /// attaches to its resolved parent as an edition tab.
    /// </summary>
    private static List<Game> GroupSubsetsOnly(
        IReadOnlyCollection<Game> games,
        IReadOnlyDictionary<string, Game> byId,
        Func<Game, bool>? include)
    {
        var byRoot = new Dictionary<Game, List<Game>>();
        foreach (var game in games)
        {
            var root = ResolveSubsetRoot(game, byId);
            if (!byRoot.TryGetValue(root, out var members))
                byRoot[root] = members = new List<Game>();
            members.Add(game);
        }

        var representatives = new List<Game>(byRoot.Count);
        foreach (var (root, members) in byRoot)
        {
            var visible = include == null ? members : members.Where(include).ToList();
            if (visible.Count == 0)
                continue;

            if (visible.Count == 1)
            {
                representatives.Add(visible[0]);
                continue;
            }

            // The root has at least one subset attached. Prefer the root itself as the card even
            // if it's not first in visible order; fall back to whichever subset survived filtering
            // if the root itself was filtered out (e.g. its platform is disabled).
            var primary = visible.Contains(root) ? root : visible[0];
            primary.MergedDisplayName = primary.Name;
            var rest = visible
                .Where(g => !ReferenceEquals(g, primary))
                .OrderByDescending(g => g.UnlockedCount)
                .ThenByDescending(g => g.Achievements.Count)
                .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase);
            primary.Editions = new[] { primary }.Concat(rest).ToObservableCollection();

            representatives.Add(primary);
        }

        return representatives;
    }

    /// <summary>
    /// Follows the RA subset <see cref="Game.ParentIdentifier"/> chain to the ultimate in-library
    /// ancestor. A game with no parent (or an unresolvable one) is its own root.
    /// </summary>
    private static Game ResolveSubsetRoot(Game game, IReadOnlyDictionary<string, Game> byId)
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
        return root;
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
    /// The title-merge key for a game: its subset root's normalized title, or the root's raw
    /// identifier when the title normalizes to nothing. RA subsets therefore always key alongside
    /// their parent's title group, regardless of the parent's own title-merge outcome.
    /// </summary>
    private static string GroupKey(Game game, IReadOnlyDictionary<string, Game> byId)
    {
        var root = ResolveSubsetRoot(game, byId);
        var normalized = NormalizeTitle(root.Name);
        return normalized.Length > 0 ? "name:" + normalized : "id:" + root.Identifier;
    }
}
