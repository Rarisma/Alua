using Alua.Models;
using Alua.Services;
using Alua.Services.ViewModels;
using NUnit.Framework;

namespace Alua.Tests;

[TestFixture]
public class GameGroupingTests
{
    [Test]
    public void NormalizeTitle_StripsEditionQualifiers()
    {
        Assert.That(GameGrouping.NormalizeTitle("Control Ultimate Edition"),
            Is.EqualTo(GameGrouping.NormalizeTitle("Control")));
    }

    [Test]
    public void NormalizeTitle_KeepsSequelsDistinct()
    {
        Assert.That(GameGrouping.NormalizeTitle("Portal"),
            Is.Not.EqualTo(GameGrouping.NormalizeTitle("Portal 2")));
    }

    [Test]
    public void NormalizeTitle_StripsTrademarksAndBrackets()
    {
        Assert.That(GameGrouping.NormalizeTitle("NieR:Automata™ (PS4)"),
            Is.EqualTo(GameGrouping.NormalizeTitle("NieR: Automata")));
    }

    [Test]
    public void Group_MergesEditionsAcrossPlatforms()
    {
        var games = new[]
        {
            new Game { Name = "Control", Identifier = "steam-1", Platform = Platforms.Steam },
            new Game { Name = "Control Ultimate Edition", Identifier = "psn-1", Platform = Platforms.PlayStation },
            new Game { Name = "Portal 2", Identifier = "steam-2", Platform = Platforms.Steam },
        };

        var reps = GameGrouping.Group(games);

        Assert.That(reps.Count, Is.EqualTo(2), "Control editions should collapse into one representative");
        var control = reps.Single(g => GameGrouping.NormalizeTitle(g.Name) == GameGrouping.NormalizeTitle("Control"));
        Assert.That(control.IsMerged, Is.True);
        Assert.That(control.Editions.Count, Is.EqualTo(2));
    }

    [Test]
    public void Group_AggregateMode_SumsCompletionAcrossEditions()
    {
        var games = new[]
        {
            TestData.Game("Control", "steam-1", Platforms.Steam, total: 25, unlocked: 15),
            TestData.Game("Control Ultimate Edition", "psn-1", Platforms.PlayStation, total: 12, unlocked: 10),
        };

        var best = GameGrouping.Group(games, completionMode: MergedCompletionMode.Best).Single();
        Assert.That(best.EffectiveUnlocked, Is.EqualTo(best.UnlockedCount), "Best mode uses the primary's own counts");

        var aggregate = GameGrouping.Group(games, completionMode: MergedCompletionMode.Aggregate).Single();
        Assert.That(aggregate.EffectiveUnlocked, Is.EqualTo(25));
        Assert.That(aggregate.EffectiveTotal, Is.EqualTo(37));
    }

    [Test]
    public void DontMerge_KeepsTitleEditionsSeparateButAttachesSubsets()
    {
        var games = new[]
        {
            TestData.Game("Control", "steam-1", Platforms.Steam, total: 10, unlocked: 8),
            TestData.Game("Control Ultimate Edition", "psn-1", Platforms.PlayStation, total: 12, unlocked: 3),
            new Game { Name = "Control - Subset", Identifier = "ra-1", Platform = Platforms.RetroAchievements, ParentIdentifier = "steam-1" },
        };

        var reps = GameGrouping.Group(games, editionMode: EditionDisplayMode.DontMerge);

        Assert.That(reps.Count, Is.EqualTo(2), "title-alike editions must stay separate in DontMerge mode");
        var steamControl = reps.Single(g => g.Identifier == "steam-1");
        Assert.That(steamControl.IsMerged, Is.True, "the RA subset must still attach to its parent");
        Assert.That(steamControl.Editions.Select(e => e.Identifier), Does.Contain("ra-1"));
        var psnControl = reps.Single(g => g.Identifier == "psn-1");
        Assert.That(psnControl.IsMerged, Is.False, "an edition with no subsets of its own stays a single card");
    }

    [Test]
    public void PriorityOnly_ShowsHighestPriorityPlatformAndDropsAlternates()
    {
        var games = new[]
        {
            TestData.Game("Control", "steam-1", Platforms.Steam, total: 10, unlocked: 2),
            TestData.Game("Control Ultimate Edition", "psn-1", Platforms.PlayStation, total: 12, unlocked: 11),
        };
        var priority = new[] { Platforms.Steam, Platforms.PlayStation, Platforms.Xbox, Platforms.RetroAchievements };

        // Steam ranks above PlayStation even though the PSN copy has far more progress.
        var reps = GameGrouping.Group(games, editionMode: EditionDisplayMode.PriorityOnly, platformPriority: priority);

        Assert.That(reps.Count, Is.EqualTo(1));
        Assert.That(reps[0].Platform, Is.EqualTo(Platforms.Steam));
        Assert.That(reps[0].IsMerged, Is.False, "the dropped alternate must not appear as a tab");
    }

    [Test]
    public void PriorityOnly_SamePlatformTieBreaksToMostProgress()
    {
        var games = new[]
        {
            TestData.Game("Hades", "steam-1", Platforms.Steam, total: 50, unlocked: 10),
            TestData.Game("Hades", "steam-2", Platforms.Steam, total: 50, unlocked: 40),
        };
        var priority = new[] { Platforms.Steam, Platforms.PlayStation, Platforms.Xbox, Platforms.RetroAchievements };

        var reps = GameGrouping.Group(games, editionMode: EditionDisplayMode.PriorityOnly, platformPriority: priority);

        Assert.That(reps.Count, Is.EqualTo(1));
        Assert.That(reps[0].Identifier, Is.EqualTo("steam-2"), "same-platform tie should fall back to most progress");
    }

    [Test]
    public void PriorityOnly_NeverPromotesASubsetOverItsOwnParent()
    {
        var parent = TestData.Game("Final Fantasy VII", "ra-parent", Platforms.RetroAchievements, total: 80, unlocked: 5);
        var subset = TestData.Game("Final Fantasy VII [Subset - Bonus]", "ra-subset", Platforms.RetroAchievements, total: 10, unlocked: 9);
        subset.ParentIdentifier = "ra-parent";
        var games = new[] { parent, subset };
        var priority = new[] { Platforms.Steam, Platforms.PlayStation, Platforms.Xbox, Platforms.RetroAchievements };

        var reps = GameGrouping.Group(games, editionMode: EditionDisplayMode.PriorityOnly, platformPriority: priority);

        Assert.That(reps.Count, Is.EqualTo(1));
        Assert.That(reps[0].Identifier, Is.EqualTo("ra-parent"),
            "the parent must remain the shown card even when its own subset has more progress");
        Assert.That(reps[0].Editions.Select(e => e.Identifier), Does.Contain("ra-subset"));
    }

    [Test]
    public void PriorityOnly_KeepsChosenEditionsOwnSubsets()
    {
        var games = new[]
        {
            TestData.Game("Control", "steam-1", Platforms.Steam, total: 10, unlocked: 2),
            TestData.Game("Control Ultimate Edition", "psn-1", Platforms.PlayStation, total: 12, unlocked: 11),
            new Game { Name = "Control - Subset", Identifier = "ra-1", Platform = Platforms.RetroAchievements, ParentIdentifier = "steam-1" },
        };
        var priority = new[] { Platforms.Steam, Platforms.PlayStation, Platforms.Xbox, Platforms.RetroAchievements };

        var reps = GameGrouping.Group(games, editionMode: EditionDisplayMode.PriorityOnly, platformPriority: priority);

        Assert.That(reps.Count, Is.EqualTo(1));
        Assert.That(reps[0].Platform, Is.EqualTo(Platforms.Steam));
        Assert.That(reps[0].Editions.Select(e => e.Identifier), Does.Contain("ra-1"),
            "the shown edition's own subset must still attach, even though its sibling edition was dropped");
    }

    [Test]
    public void DontMerge_PrimaryEditionStaysFirstEvenWhenItsSubsetHasMoreProgress()
    {
        var parent = TestData.Game("Final Fantasy VII", "ra-parent", Platforms.RetroAchievements, total: 80, unlocked: 5);
        var subset = TestData.Game("Final Fantasy VII [Subset - Bonus]", "ra-subset", Platforms.RetroAchievements, total: 10, unlocked: 9);
        subset.ParentIdentifier = "ra-parent";
        var games = new[] { parent, subset };

        var reps = GameGrouping.Group(games, editionMode: EditionDisplayMode.DontMerge);

        Assert.That(reps.Count, Is.EqualTo(1));
        Assert.That(reps[0].Identifier, Is.EqualTo("ra-parent"));
        Assert.That(reps[0].Editions.First().Identifier, Is.EqualTo("ra-parent"),
            "the parent must stay Editions[0] even though its subset has more progress");
    }

    [Test]
    public void PriorityOnly_PrimaryEditionStaysFirstEvenWhenItsSubsetHasMoreProgress()
    {
        var parent = TestData.Game("Final Fantasy VII", "ra-parent", Platforms.RetroAchievements, total: 80, unlocked: 5);
        var subset = TestData.Game("Final Fantasy VII [Subset - Bonus]", "ra-subset", Platforms.RetroAchievements, total: 10, unlocked: 9);
        subset.ParentIdentifier = "ra-parent";
        var games = new[] { parent, subset };
        var priority = new[] { Platforms.Steam, Platforms.PlayStation, Platforms.Xbox, Platforms.RetroAchievements };

        var reps = GameGrouping.Group(games, editionMode: EditionDisplayMode.PriorityOnly, platformPriority: priority);

        Assert.That(reps.Count, Is.EqualTo(1));
        Assert.That(reps[0].Identifier, Is.EqualTo("ra-parent"));
        Assert.That(reps[0].Editions.First().Identifier, Is.EqualTo("ra-parent"),
            "the parent must stay Editions[0] even though its subset has more progress");
    }
}
