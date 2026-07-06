using Alua.Models;
using Alua.Services;
using NUnit.Framework;

namespace Alua.Tests;

[TestFixture]
public class LibraryMetricsTests
{
    // Same game owned on two platforms, both 100% complete.
    private static Game[] CrossPlatformPerfect() =>
    [
        TestData.Game("Control", "steam-1", Platforms.Steam, total: 10, unlocked: 10),
        TestData.Game("Control", "psn-1", Platforms.PlayStation, total: 10, unlocked: 10),
    ];

    [Test]
    public void CountMultiPlatformOnce_DeDuplicatesGamesAndPerfects()
    {
        var m = LibraryMetrics.Compute(CrossPlatformPerfect(), dedupe: true, DateTime.Now);

        Assert.That(m.TotalGames, Is.EqualTo(1));
        Assert.That(m.PerfectGames, Is.EqualTo(1));
        Assert.That(m.TotalAchievements, Is.EqualTo(10));
        Assert.That(m.UnlockedAchievements, Is.EqualTo(10));
        Assert.That(m.PercentComplete, Is.EqualTo(100));
    }

    [Test]
    public void RawCount_CountsEachPlatformSeparately()
    {
        var m = LibraryMetrics.Compute(CrossPlatformPerfect(), dedupe: false, DateTime.Now);

        Assert.That(m.TotalGames, Is.EqualTo(2));
        Assert.That(m.PerfectGames, Is.EqualTo(2));
        Assert.That(m.TotalAchievements, Is.EqualTo(20));
        Assert.That(m.UnlockedAchievements, Is.EqualTo(20));
    }

    [Test]
    public void DeDup_UsesMostProgressedEditionAsRepresentative()
    {
        var games = new[]
        {
            TestData.Game("Hades", "steam-1", Platforms.Steam, total: 50, unlocked: 49),
            TestData.Game("Hades", "psn-1", Platforms.PlayStation, total: 20, unlocked: 2),
        };

        var m = LibraryMetrics.Compute(games, dedupe: true, DateTime.Now);

        Assert.That(m.TotalGames, Is.EqualTo(1));
        Assert.That(m.TotalAchievements, Is.EqualTo(50), "should reflect the most-progressed edition");
        Assert.That(m.UnlockedAchievements, Is.EqualTo(49));
    }

    [Test]
    public void EmptyLibrary_ReturnsZeroes()
    {
        var m = LibraryMetrics.Compute(Array.Empty<Game>(), dedupe: true, DateTime.Now);
        Assert.That(m, Is.EqualTo(default(LibraryMetrics)));
    }

    [Test]
    public void GamesUnstarted_CountsZeroUnlockRegardlessOfAchievementPresence()
    {
        var games = new[]
        {
            TestData.Game("No achievements", "steam-1", Platforms.Steam, total: 0, unlocked: 0),
            TestData.Game("Not started", "steam-2", Platforms.Steam, total: 10, unlocked: 0),
            TestData.Game("Started", "steam-3", Platforms.Steam, total: 10, unlocked: 1),
        };

        var m = LibraryMetrics.Compute(games, dedupe: false, DateTime.Now);

        Assert.That(m.GamesUnstarted, Is.EqualTo(2));
        Assert.That(m.GamesStarted, Is.EqualTo(1));
        Assert.That(m.GamesWithAchievements, Is.EqualTo(2));
    }

    [Test]
    public void AverageGameCompletionPercent_OnlyCountsGamesWithAchievements()
    {
        var games = new[]
        {
            TestData.Game("No achievements", "steam-1", Platforms.Steam, total: 0, unlocked: 0),
            TestData.Game("Half done", "steam-2", Platforms.Steam, total: 10, unlocked: 5),
        };

        var m = LibraryMetrics.Compute(games, dedupe: false, DateTime.Now);

        Assert.That(m.AverageGameCompletionPercent, Is.EqualTo(50.0).Within(0.001));
    }

    [Test]
    public void RecentUnlocks_CountWithinWindowAndFindMostRecent()
    {
        var now = new DateTime(2026, 7, 6, 12, 0, 0, DateTimeKind.Utc);
        var game = TestData.Game("Recent", "steam-1", Platforms.Steam, total: 3, unlocked: 3);
        game.Achievements[0].UnlockedOn = now.AddDays(-2);
        game.Achievements[1].UnlockedOn = now.AddDays(-20);
        game.Achievements[2].UnlockedOn = now.AddDays(-100);

        var m = LibraryMetrics.Compute(new[] { game }, dedupe: false, now);

        Assert.That(m.UnlocksLast7Days, Is.EqualTo(1));
        Assert.That(m.UnlocksLast30Days, Is.EqualTo(2));
        Assert.That(m.MostRecentUnlockDate, Is.EqualTo(now.AddDays(-2)));
        Assert.That(m.MostRecentUnlockGameName, Is.EqualTo("Recent"));
    }

    [Test]
    public void MostPlayedGame_ReportsHighestPlaytime()
    {
        var a = TestData.Game("A", "steam-1", Platforms.Steam);
        a.PlaytimeMinutes = 120;
        var b = TestData.Game("B", "steam-2", Platforms.Steam);
        b.PlaytimeMinutes = 500;

        var m = LibraryMetrics.Compute(new[] { a, b }, dedupe: false, DateTime.Now);

        Assert.That(m.MostPlayedGameName, Is.EqualTo("B"));
        Assert.That(m.MostPlayedGameMinutes, Is.EqualTo(500));
        Assert.That(m.TotalPlaytimeMinutes, Is.EqualTo(620));
    }
}
