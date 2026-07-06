using Alua.Models;

namespace Alua.Services;

/// <summary>
/// Aggregate library metrics. Pure value type with no dependency on view models or DI, so it can
/// be computed on any thread and unit-tested directly.
/// </summary>
public readonly record struct LibraryMetrics(
    int TotalGames,
    int GamesWithAchievements,
    int GamesStarted,
    int GamesUnstarted,
    int PerfectGames,
    int UnlockedAchievements,
    int TotalAchievements,
    int PercentComplete,
    double AverageGameCompletionPercent,
    int TotalPlaytimeMinutes,
    string? MostPlayedGameName,
    int MostPlayedGameMinutes,
    string? MostRecentUnlockGameName,
    DateTime? MostRecentUnlockDate,
    int UnlocksLast7Days,
    int UnlocksLast30Days)
{
    /// <summary>
    /// Computes aggregate metrics over <paramref name="games"/>, as of <paramref name="now"/>.
    /// When <paramref name="dedupe"/> is true, a game re-released across editions/subsets (or, for
    /// the overall/all-platforms call, owned on several platforms) counts once: each merge group
    /// contributes its most-engaged edition's stats, mirroring <see cref="GameGrouping"/>'s primary
    /// selection. When false, every game counts on its own.
    /// </summary>
    public static LibraryMetrics Compute(IReadOnlyCollection<Game> games, bool dedupe, DateTime now)
    {
        if (games == null || games.Count == 0)
            return default;

        var representatives = new List<Game>();
        if (dedupe)
        {
            foreach (var group in GameGrouping.KeyGames(games))
            {
                Game representative = null!;
                int bestUnlocked = -1, bestAchievements = -1;
                foreach (var edition in group)
                {
                    int u = edition.UnlockedCount;
                    int a = edition.Achievements.Count;
                    if (u > bestUnlocked || (u == bestUnlocked && a > bestAchievements))
                    {
                        bestUnlocked = u;
                        bestAchievements = a;
                        representative = edition;
                    }
                }
                representatives.Add(representative);
            }
        }
        else
        {
            representatives.AddRange(games);
        }

        int totalGames = representatives.Count;
        int gamesWithAchievements = 0, gamesStarted = 0, perfect = 0;
        int unlocked = 0, totalAchievements = 0, totalPlaytime = 0;
        double completionPercentSum = 0;
        int completionPercentCount = 0;
        Game? mostPlayed = null;
        string? mostRecentUnlockGame = null;
        DateTime? mostRecentUnlockDate = null;
        int last7 = 0, last30 = 0;

        foreach (var g in representatives)
        {
            int achievements = g.Achievements.Count;
            int unlockedHere = g.UnlockedCount;
            totalAchievements += achievements;
            unlocked += unlockedHere;
            totalPlaytime += g.PlaytimeMinutes;

            if (achievements > 0)
            {
                gamesWithAchievements++;
                completionPercentSum += (double)unlockedHere / achievements * 100.0;
                completionPercentCount++;
                if (unlockedHere == achievements)
                    perfect++;
            }

            if (unlockedHere > 0)
                gamesStarted++;

            if (mostPlayed == null || g.PlaytimeMinutes > mostPlayed.PlaytimeMinutes)
                mostPlayed = g;

            foreach (var achievement in g.Achievements)
            {
                if (achievement.UnlockedOn is not { } unlockedOn)
                    continue;

                if (mostRecentUnlockDate == null || unlockedOn > mostRecentUnlockDate)
                {
                    mostRecentUnlockDate = unlockedOn;
                    mostRecentUnlockGame = g.Name;
                }

                var age = now - unlockedOn;
                if (age <= TimeSpan.FromDays(7)) last7++;
                if (age <= TimeSpan.FromDays(30)) last30++;
            }
        }

        int percentComplete = totalAchievements == 0 ? 0 : unlocked * 100 / totalAchievements;
        double averageCompletion = completionPercentCount == 0 ? 0 : completionPercentSum / completionPercentCount;

        return new LibraryMetrics(
            TotalGames: totalGames,
            GamesWithAchievements: gamesWithAchievements,
            GamesStarted: gamesStarted,
            GamesUnstarted: totalGames - gamesStarted,
            PerfectGames: perfect,
            UnlockedAchievements: unlocked,
            TotalAchievements: totalAchievements,
            PercentComplete: percentComplete,
            AverageGameCompletionPercent: averageCompletion,
            TotalPlaytimeMinutes: totalPlaytime,
            MostPlayedGameName: mostPlayed?.Name,
            MostPlayedGameMinutes: mostPlayed?.PlaytimeMinutes ?? 0,
            MostRecentUnlockGameName: mostRecentUnlockGame,
            MostRecentUnlockDate: mostRecentUnlockDate,
            UnlocksLast7Days: last7,
            UnlocksLast30Days: last30);
    }
}
