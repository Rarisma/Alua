using Alua.Models;

namespace Alua.Tests;

internal static class TestData
{
    /// <summary>Builds a Game with <paramref name="total"/> achievements, the first
    /// <paramref name="unlocked"/> of which are unlocked.</summary>
    public static Game Game(string name, string id, Platforms platform, int total = 0, int unlocked = 0)
    {
        var g = new Game { Name = name, Identifier = id, Platform = platform };
        for (int i = 0; i < total; i++)
            g.Achievements.Add(new Achievement { Id = $"{id}-{i}", IsUnlocked = i < unlocked });
        return g;
    }
}
