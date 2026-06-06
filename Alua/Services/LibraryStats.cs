using Alua.Models;

namespace Alua.Services;

/// <summary>
/// Aggregate library statistics. Pure value type with no dependency on view models or DI, so it can
/// be computed on any thread and unit-tested directly.
/// </summary>
public readonly record struct LibraryStats(
    int TotalGames,
    int UnlockedCount,
    int TotalAchievements,
    int PerfectGames,
    int PercentComplete)
{
    /// <summary>
    /// Computes aggregate stats over <paramref name="games"/>.
    /// When <paramref name="countMultiPlatformOnce"/> is true, a game owned on several platforms (or
    /// re-released across editions/subsets) counts once: each merge group contributes its
    /// most-engaged edition's achievement counts, so totals and "perfect games" are not inflated by
    /// cross-platform ownership. When false, every game counts on its own (raw library totals).
    /// </summary>
    public static LibraryStats Compute(IReadOnlyCollection<Game> games, bool countMultiPlatformOnce)
    {
        if (games == null || games.Count == 0)
            return default;

        int totalGames = 0, unlocked = 0, totalAchievements = 0, perfect = 0;

        void Accumulate(Game representative)
        {
            totalGames++;
            int achievements = representative.Achievements.Count;
            int unlockedHere = representative.UnlockedCount;
            totalAchievements += achievements;
            unlocked += unlockedHere;
            if (representative.HasAchievements && achievements == unlockedHere)
                perfect++;
        }

        if (countMultiPlatformOnce)
        {
            // De-duplicate by merge key without disturbing the library's display grouping. The
            // representative mirrors GameGrouping's primary choice (the edition with the most
            // progress), so a group's stats reflect the user's furthest progress on that game.
            foreach (var group in GameGrouping.KeyGames(games))
            {
                Game representative = null!;
                int bestUnlocked = -1, bestAchievements = -1;
                foreach (var edition in group)
                {
                    int u = edition.UnlockedCount;
                    int a = edition.Achievements.Count;
                    if (u > bestUnlocked
                        || (u == bestUnlocked && a > bestAchievements))
                    {
                        bestUnlocked = u;
                        bestAchievements = a;
                        representative = edition;
                    }
                }
                Accumulate(representative);
            }
        }
        else
        {
            foreach (var game in games)
                Accumulate(game);
        }

        int percent = totalAchievements == 0 ? 0 : unlocked * 100 / totalAchievements;
        return new LibraryStats(totalGames, unlocked, totalAchievements, perfect, percent);
    }
}
