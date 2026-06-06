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

        var best = GameGrouping.Group(games, mode: MergedCompletionMode.Best).Single();
        Assert.That(best.EffectiveUnlocked, Is.EqualTo(best.UnlockedCount), "Best mode uses the primary's own counts");

        var aggregate = GameGrouping.Group(games, mode: MergedCompletionMode.Aggregate).Single();
        Assert.That(aggregate.EffectiveUnlocked, Is.EqualTo(25));
        Assert.That(aggregate.EffectiveTotal, Is.EqualTo(37));
    }
}
