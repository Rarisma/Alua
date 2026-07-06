using Alua.Models;
using Alua.Services.ViewModels;
using NUnit.Framework;

namespace Alua.Tests;

[TestFixture]
public class FilterAndSortTests
{
    private static FilterArgs Args(
        string? search = null,
        OrderBy orderBy = OrderBy.Name,
        bool steam = true, bool ra = true, bool psn = true, bool xbox = true,
        EditionDisplayMode editionMode = EditionDisplayMode.Merge, bool showOnlyMerged = false,
        IReadOnlyList<Platforms>? platformPriority = null) =>
        new(HideComplete: false, HideNoAchievements: false, HideUnstarted: false, Reverse: false,
            SearchText: search, OrderBy: orderBy,
            SteamFilter: steam, RAFilter: ra, PSNFilter: psn, XBFilter: xbox,
            EditionDisplayMode: editionMode, ShowOnlyMerged: showOnlyMerged,
            PlatformPriority: platformPriority);

    [Test]
    public void DisablingAPlatform_HidesItsDataEvenOnAMergedCard()
    {
        var games = new[]
        {
            TestData.Game("Control", "steam-1", Platforms.Steam, total: 10, unlocked: 8),
            TestData.Game("Control Ultimate Edition", "psn-1", Platforms.PlayStation, total: 12, unlocked: 3),
        };

        // Steam disabled: the merged Control card must drop its Steam edition and show only PSN.
        var result = LibraryVM.FilterAndSort(games, Args(steam: false));

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].Platform, Is.EqualTo(Platforms.PlayStation),
            "representative must come from an enabled platform");
        Assert.That(result[0].Editions.All(e => e.Platform == Platforms.PlayStation), Is.True);
    }

    [Test]
    public void DisablingAllOfAGroupsPlatforms_RemovesTheCard()
    {
        var games = new[]
        {
            TestData.Game("Control", "steam-1", Platforms.Steam, 10, 8),
            TestData.Game("Control Ultimate Edition", "psn-1", Platforms.PlayStation, 12, 3),
        };

        var result = LibraryVM.FilterAndSort(games, Args(steam: false, psn: false));

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Search_MatchesNonPrimaryEditionTitle()
    {
        var games = new[]
        {
            // Steam "Control" is the most-engaged → becomes the representative (Name "Control").
            TestData.Game("Control", "steam-1", Platforms.Steam, 10, 9),
            TestData.Game("Control Ultimate Edition", "psn-1", Platforms.PlayStation, 12, 1),
        };

        var result = LibraryVM.FilterAndSort(games, Args(search: "Ultimate"));

        Assert.That(result.Count, Is.EqualTo(1),
            "searching a non-primary edition's title should still surface the merged card");
        Assert.That(result[0].Name, Is.EqualTo("Control"));
    }

    [Test]
    public void DontMerge_KeepsEditionsSeparateButKeepsSubsetsAttached()
    {
        var subset = TestData.Game("Control - Subset", "ra-1", Platforms.RetroAchievements, total: 5, unlocked: 1);
        subset.ParentIdentifier = "steam-1";
        var games = new[]
        {
            TestData.Game("Control", "steam-1", Platforms.Steam, total: 10, unlocked: 8),
            TestData.Game("Control Ultimate Edition", "psn-1", Platforms.PlayStation, total: 12, unlocked: 3),
            subset,
        };

        var result = LibraryVM.FilterAndSort(games, Args(editionMode: EditionDisplayMode.DontMerge));

        Assert.That(result.Count, Is.EqualTo(2), "DontMerge must show every title-alike edition as its own card");
        var steamControl = result.Single(g => g.Identifier == "steam-1");
        Assert.That(steamControl.Editions.Select(e => e.Identifier), Does.Contain("ra-1"),
            "the RA subset must still attach to its parent even though title-editions stay separate");
    }

    [Test]
    public void PriorityOnly_ShowsConfiguredPlatformFirst()
    {
        var games = new[]
        {
            TestData.Game("Control", "steam-1", Platforms.Steam, total: 10, unlocked: 1),
            TestData.Game("Control Ultimate Edition", "psn-1", Platforms.PlayStation, total: 12, unlocked: 11),
        };
        var priority = new[] { Platforms.PlayStation, Platforms.Steam, Platforms.Xbox, Platforms.RetroAchievements };

        var result = LibraryVM.FilterAndSort(
            games, Args(editionMode: EditionDisplayMode.PriorityOnly, platformPriority: priority));

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].Platform, Is.EqualTo(Platforms.PlayStation));
    }
}
