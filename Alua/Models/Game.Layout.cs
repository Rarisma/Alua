using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;

namespace Alua.Models;

/// <summary>
/// Presentation-only helpers for <see cref="Game"/> that depend on Uno UI types
/// (<see cref="GridLength"/>). Kept in a separate partial so the core model stays free of
/// Microsoft.UI.Xaml and can be unit-tested (e.g. <see cref="Alua.Services.GameGrouping"/>)
/// without loading Uno.WinUI in a headless test host.
/// </summary>
public partial class Game
{
    /// <summary>
    /// Star-based width for the "unlocked" portion of the filled-background progress indicator.
    /// </summary>
    [JsonIgnore]
    public GridLength UnlockedColumnWidth
    {
        get
        {
            if (!HasAchievements || UnlockedCount == 0)
                return new GridLength(0);
            return new GridLength(UnlockedCount, GridUnitType.Star);
        }
    }

    /// <summary>
    /// Star-based width for the "locked" remainder of the filled-background progress indicator.
    /// </summary>
    [JsonIgnore]
    public GridLength LockedColumnWidth
    {
        get
        {
            if (!HasAchievements)
                return new GridLength(1, GridUnitType.Star);
            var locked = Achievements.Count - UnlockedCount;
            if (locked <= 0)
                return new GridLength(0);
            return new GridLength(locked, GridUnitType.Star);
        }
    }
}
