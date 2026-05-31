// ReSharper disable UnusedMember.Global
namespace Alua.Models;

/// <summary>
/// Provider-agnostic classification flags for an achievement/trophy.
/// Modelled as a bit-flags enum so a single achievement can carry more than one
/// classification in the future, and so it serializes to/from JSON as a plain int
/// (old saved data with no value deserializes to <see cref="None"/> — no migration needed).
/// </summary>
[Flags]
public enum AchievementFlags
{
    /// <summary>Standard achievement with no special classification.</summary>
    None = 0,

    /// <summary>
    /// Can be permanently missed in a single playthrough (e.g. RetroAchievements "missable").
    /// </summary>
    Missable = 1 << 0,

    /// <summary>
    /// Marks story/progression milestones (e.g. RetroAchievements "progression").
    /// </summary>
    Progression = 1 << 1,

    /// <summary>
    /// Completing the set's win condition / beating the game (e.g. RetroAchievements "win_condition").
    /// </summary>
    WinCondition = 1 << 2,

    // Reserved for future provider-specific classifications (e.g. PSN trophy grades,
    // Xbox unlock metadata): allocate the next free bit (1 << 3, 1 << 4, ...).
}
