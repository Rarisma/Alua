using System.Text.Json.Serialization;

//Curtains: opened  Lights: on  Moment: unmissed  Eyes: buttered
namespace Alua.Models;

/// <summary>
/// Base Class for Achievements.
/// </summary>
public class Achievement
{
    /// <summary>
    /// Unique achievement identifier
    /// </summary>
    [JsonInclude, JsonPropertyName("ID")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Title of the Achievement
    /// </summary>
    [JsonInclude, JsonPropertyName("Title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Description of the Achievement
    /// </summary>
    [JsonInclude, JsonPropertyName("Desc")]
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Has the player unlocked this achievement
    /// </summary>
    [JsonInclude, JsonPropertyName("Unlocked")]
    public bool IsUnlocked { get; set; }
    
    /// <summary>
    /// When the player unlocked this achievement (if so)
    /// </summary>
    [JsonInclude, JsonPropertyName("UnlockTime")]
    public DateTime? UnlockedOn { get; set; }
    
    /// <summary>
    /// Achievement Icon URL
    /// </summary>
    [JsonInclude, JsonPropertyName("Icon")]
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// Is the achievement hidden/secret
    /// </summary>
    [JsonInclude, JsonPropertyName("Hidden")]
    public bool IsHidden { get; set; }

    /// <summary>
    /// Provider-agnostic classification flags (missable, progression, win-condition, ...).
    /// Defaults to <see cref="AchievementFlags.None"/>; only providers that expose this
    /// metadata (currently RetroAchievements) populate it. Serializes as an int, so saved
    /// data that predates this field deserializes to <see cref="AchievementFlags.None"/>.
    /// </summary>
    [JsonInclude, JsonPropertyName("Flags")]
    public AchievementFlags Flags { get; set; } = AchievementFlags.None;

    /// <summary>True when this achievement can be permanently missed in a playthrough.</summary>
    [JsonIgnore]
    public bool IsMissable => Flags.HasFlag(AchievementFlags.Missable);
    
    /// <summary>
    /// UI-only flag: true when this achievement belongs to a RetroAchievements set. The GamePage stamps
    /// this when it builds the displayed list (the achievement itself carries no platform), so the
    /// "discuss on RetroAchievements" link is shown only where it's meaningful. Not persisted.
    /// </summary>
    [JsonIgnore]
    public bool IsRA { get; set; }
    [JsonIgnore]
    public bool IsPSN { get; set; }

    /// <summary>True when this achievement marks a story/progression milestone.</summary>
    [JsonIgnore]
    public bool IsProgression => Flags.HasFlag(AchievementFlags.Progression);

    /// <summary>True when this achievement is the set's win condition (beat the game).</summary>
    [JsonIgnore]
    public bool IsWinCondition => Flags.HasFlag(AchievementFlags.WinCondition);
    
    /// <summary>
    /// Percentage of players who have unlocked this achievement (0-100)
    /// </summary>
    [JsonInclude, JsonPropertyName("RarityPercentage")]
    public double? RarityPercentage { get; set; }

    [JsonIgnore]
    public Uri? IconUri => string.IsNullOrWhiteSpace(Icon) ? null
        : Uri.TryCreate(Icon, UriKind.Absolute, out var uri) ? uri : null;

    /// <summary>
    /// Returns true when a valid rarity percentage is available for display.
    /// </summary>
    [JsonIgnore]
    public bool HasRarity => RarityPercentage.HasValue && RarityPercentage.Value > 0;

    /// <summary>
    /// Rarity percentage formatted for display, rounded to 2 decimal places with trailing
    /// zeros trimmed (e.g. 12.3456 -> "12.35", 5.0 -> "5"). Providers other than PSN supply
    /// full-precision values, so rounding here keeps the displayed rarity consistent.
    /// </summary>
    [JsonIgnore]
    public string RarityPercentageText => RarityPercentage.HasValue
        ? RarityPercentage.Value.ToString("0.##")
        : string.Empty;

    /// <summary>
    /// Progress of achievement (if applicable)
    /// </summary>
    [JsonInclude, JsonPropertyName("Progress")]
    public int? CurrentProgress { get; set; }
    
    /// <summary>
    /// Maximum progress of achievement (if applicable)
    /// </summary>
    [JsonInclude, JsonPropertyName("MaxProgress")]
    public int? MaxProgress { get; set; }

    /// <summary>
    /// PlayStation trophy tier. Only PSN exposes this; every other provider leaves it
    /// <see cref="TrophyKind.None"/>. Serialized as an int, so saves that predate this field
    /// deserialize to <see cref="TrophyKind.None"/>.
    /// </summary>
    [JsonInclude, JsonPropertyName("TrophyType")]
    public TrophyKind TrophyType { get; set; } = TrophyKind.None;

    /// <summary>True when a PlayStation trophy tier is known for this achievement.</summary>
    [JsonIgnore]
    public bool HasTrophy => TrophyType != TrophyKind.None;

    /// <summary>Display name for the trophy tier (e.g. "Platinum"); empty when none.</summary>
    [JsonIgnore]
    public string TrophyTypeText => HasTrophy ? TrophyType.ToString() : string.Empty;
}

/// <summary>PlayStation trophy tiers, ordered by prestige. <see cref="TrophyKind.None"/> means the
/// achievement has no trophy tier (e.g. Steam/Xbox/RetroAchievements).</summary>
public enum TrophyKind
{
    None = 0,
    Bronze = 1,
    Silver = 2,
    Gold = 3,
    Platinum = 4
}
