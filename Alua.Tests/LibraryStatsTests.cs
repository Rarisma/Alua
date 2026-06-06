using Alua.Models;
using Alua.Services;
using NUnit.Framework;

namespace Alua.Tests;

[TestFixture]
public class LibraryStatsTests
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
        var stats = LibraryStats.Compute(CrossPlatformPerfect(), countMultiPlatformOnce: true);

        Assert.That(stats.TotalGames, Is.EqualTo(1));
        Assert.That(stats.PerfectGames, Is.EqualTo(1));
        Assert.That(stats.TotalAchievements, Is.EqualTo(10));
        Assert.That(stats.UnlockedCount, Is.EqualTo(10));
        Assert.That(stats.PercentComplete, Is.EqualTo(100));
    }

    [Test]
    public void RawCount_CountsEachPlatformSeparately()
    {
        var stats = LibraryStats.Compute(CrossPlatformPerfect(), countMultiPlatformOnce: false);

        Assert.That(stats.TotalGames, Is.EqualTo(2));
        Assert.That(stats.PerfectGames, Is.EqualTo(2));
        Assert.That(stats.TotalAchievements, Is.EqualTo(20));
        Assert.That(stats.UnlockedCount, Is.EqualTo(20));
    }

    [Test]
    public void DeDup_UsesMostProgressedEditionAsRepresentative()
    {
        var games = new[]
        {
            TestData.Game("Hades", "steam-1", Platforms.Steam, total: 50, unlocked: 49),
            TestData.Game("Hades", "psn-1", Platforms.PlayStation, total: 20, unlocked: 2),
        };

        var stats = LibraryStats.Compute(games, countMultiPlatformOnce: true);

        Assert.That(stats.TotalGames, Is.EqualTo(1));
        Assert.That(stats.TotalAchievements, Is.EqualTo(50), "should reflect the most-progressed edition");
        Assert.That(stats.UnlockedCount, Is.EqualTo(49));
    }

    [Test]
    public void EmptyLibrary_ReturnsZeroes()
    {
        var stats = LibraryStats.Compute(System.Array.Empty<Game>(), countMultiPlatformOnce: true);
        Assert.That(stats, Is.EqualTo(default(LibraryStats)));
    }
}
