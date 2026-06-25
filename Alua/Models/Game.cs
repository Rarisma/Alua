using System;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

//GARY HIT EM WITH THE
namespace Alua.Models;
/// <summary>
/// Representation of game.
/// </summary>
public partial class Game : ObservableObject
{
    /// <summary>
    /// Name of game
    /// </summary>
    [JsonInclude, JsonPropertyName("GameName")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Game developer
    /// </summary>
    [JsonInclude, JsonPropertyName("Developer")]
    public string Author { get; set; } = string.Empty;

    /// <summary>
    /// Icon for the game
    /// </summary>
    [JsonInclude, JsonPropertyName("Icon")]
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// Last time the game information was updated
    /// </summary>
    [JsonInclude, JsonPropertyName("LastUpdated")]
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last time the game was actually played, sourced from provider data.
    /// Null if the provider doesn't report this or the game has never been played.
    /// </summary>
    [JsonInclude, JsonPropertyName("LastPlayed")]
    public DateTime? LastPlayed { get; set; }

    /// <summary>
    /// Total playtime in minutes
    /// </summary>
    [JsonInclude, JsonPropertyName("PlaytimeMinutes")]
    public int PlaytimeMinutes { get; set; }
    
    /// <summary>
    /// All achievements for this game
    /// </summary>
    [JsonInclude, JsonPropertyName("Achievements")]
    public ObservableCollection<Achievement> Achievements { get; set; } = new();

    /// <summary>
    /// Platform where this achievement originated from
    /// </summary>
    [JsonInclude, JsonPropertyName("Source")]
    public Platforms Platform { get; set; }
    
    [JsonInclude, JsonPropertyName("Identifier")]
    public string Identifier { get; set; } = string.Empty;

    /// <summary>
    /// Identifier of this game's parent, when the provider models it as a child of another game
    /// (currently only RetroAchievements subsets, linked via GameInfoExtended.ParentGameID).
    /// Stored in the same "&lt;prefix&gt;&lt;id&gt;" form as <see cref="Identifier"/> (e.g. "ra-1234").
    /// Null for standalone games. Used by grouping to merge subsets under their parent.
    /// </summary>
    [JsonInclude, JsonPropertyName("ParentIdentifier")]
    public string? ParentIdentifier { get; set; }

    /// <summary>
    /// Time to beat main story in hours
    /// </summary>
    [JsonInclude, JsonPropertyName("HowLongToBeatMain")]
    public double? HowLongToBeatMain { get; set; }
    
    /// <summary>
    /// Time to beat main story + extras in hours
    /// </summary>
    [JsonInclude, JsonPropertyName("HowLongToBeatMainExtras")]
    public double? HowLongToBeatMainExtras { get; set; }
    
    /// <summary>
    /// Time to 100% complete the game in hours
    /// </summary>
    [JsonInclude, JsonPropertyName("HowLongToBeatCompletionist")]
    public double? HowLongToBeatCompletionist { get; set; }
    
    /// <summary>
    /// Average time across all play styles in hours
    /// </summary>
    [JsonInclude, JsonPropertyName("HowLongToBeatAllStyles")]
    public double? HowLongToBeatAllStyles { get; set; }
    
    /// <summary>
    /// Last time HowLongToBeat data was fetched
    /// </summary>
    [JsonInclude, JsonPropertyName("HowLongToBeatLastFetched")]
    public DateTime? HowLongToBeatLastFetched { get; set; }

    #region Grouping (in-memory only)

    /// <summary>
    /// Other games merged under this one (editions / re-releases / RA subsets), including this
    /// game itself, with this game first. Populated at display time by
    /// <see cref="Alua.Services.GameGrouping"/>; empty for unmerged games. Never persisted.
    /// </summary>
    [JsonIgnore]
    public ObservableCollection<Game> Editions { get; set; } = new();

    /// <summary>
    /// True when this card stands in for more than one game and should show edition tabs.
    /// </summary>
    [JsonIgnore]
    public bool IsMerged => Editions.Count > 1;

    /// <summary>
    /// Clean group label chosen during grouping (the shortest edition name, e.g. "Control"
    /// rather than "Control Ultimate Edition"). Falls back to <see cref="Name"/> when unset.
    /// </summary>
    [JsonIgnore]
    public string? MergedDisplayName { get; set; }

    /// <summary>
    /// Title shown on the library card and game-page header. Equals the group label for merged
    /// games and the plain <see cref="Name"/> otherwise.
    /// </summary>
    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(MergedDisplayName) ? Name : MergedDisplayName;

    /// <summary>
    /// Short label for this game's edition tab. Currently just the full <see cref="Name"/>;
    /// the tab also shows <see cref="ProviderImage"/> to disambiguate cross-platform editions.
    /// </summary>
    [JsonIgnore]
    public string EditionLabel => Name;

    /// <summary>
    /// Caption shown on a merged library card to hint that it collapses several editions
    /// (e.g. "3 editions"). Empty for unmerged games.
    /// </summary>
    [JsonIgnore]
    public string EditionsBadgeText => IsMerged ? $"{Editions.Count} editions" : string.Empty;

    /// <summary>
    /// Aggregate unlocked count to show on a merged card, set by <see cref="Alua.Services.GameGrouping"/>
    /// when the user picks MergedCompletionMode.Aggregate. Null means "use this game's own count"
    /// (Best mode / unmerged). In-memory only.
    /// </summary>
    [JsonIgnore]
    public int? DisplayUnlocked { get; set; }

    /// <summary>Aggregate total achievement count to show on a merged card; see <see cref="DisplayUnlocked"/>.</summary>
    [JsonIgnore]
    public int? DisplayTotal { get; set; }

    /// <summary>Unlocked count for the card (aggregate across editions when set, else this game's own).</summary>
    [JsonIgnore]
    public int EffectiveUnlocked => DisplayUnlocked ?? UnlockedCount;

    /// <summary>Total achievement count for the card (aggregate across editions when set, else own).</summary>
    [JsonIgnore]
    public int EffectiveTotal => DisplayTotal ?? Achievements.Count;

    /// <summary>Status text using the effective (possibly aggregated) counts; mirrors <see cref="StatusText"/>.</summary>
    [JsonIgnore]
    public string EffectiveStatusText
    {
        get
        {
            int total = EffectiveTotal;
            int unlocked = EffectiveUnlocked;
            if (total == 0)
                return "No Achievements";
            if (unlocked == total)
                return "100% Complete";
            if (unlocked > 0)
                return $"{Math.Floor((double)unlocked / total * 100)}% ({unlocked} / {total})";
            return $"Not played (0 / {total})";
        }
    }

    #endregion

    #region UI Helpers

    /// <summary>
    /// How many achievements the user has unlocked. Computed directly — providers always
    /// build a fresh Game with the collection set before any read, so a cheap in-memory
    /// Count needs no memoization.
    /// </summary>
    [JsonIgnore]
    public int UnlockedCount => Achievements.Count(x => x.IsUnlocked);

    /// <summary>
    /// Returns true if the game has any achievements at all.
    /// </summary>
    [JsonIgnore]
    public bool HasAchievements => Achievements.Count != 0;

    /// <summary>
    /// Returns true when playtime is tracked for this game (the provider reports a value;
    /// -1 means untracked).
    /// </summary>
    [JsonIgnore]
    public bool HasPlaytime => PlaytimeMinutes >= 0;
    
    /// <summary>
    /// Returns
    /// - No Achievements if the game doesn't have achievements
    /// - 100% Complete if the user has unlocked all achievements
    /// - X of Y (Complete%) if the user has unlocked some achievements
    /// - Not started (X Achievements) if the user hasn't unlocked any achievements
    /// </summary>
    [JsonIgnore]
    public string StatusText
    {
        get
        {
            if (!HasAchievements) // No achievements
            {
                return "No Achievements";
            }
            
            if (UnlockedCount == Achievements.Count && Achievements.Count > 0) // All unlocked
            {
                return "100% Complete";
            }

            if (UnlockedCount > 0) // In progress / Started
            {
                // 10% (10 / 100)
                return $"{Math.Floor((double)UnlockedCount / Achievements.Count * 100)}% ({UnlockedCount} / {Achievements.Count})";
            }

            // Has achievements, but none unlocked
            return $"Not played (0 / {Achievements.Count})";
        }
    }

    /// <summary>
    /// Returns provider (i.e. Steam/RetroAchievements) logo
    /// </summary>
    [JsonIgnore]
    public string ProviderImage
    {
        get
        {
            return Platform switch
            {
                Platforms.Steam => "ms-appx:///Assets/Icons/steam.png",
                Platforms.RetroAchievements => "ms-appx:///Assets/Icons/RetroAchievements.png",
                Platforms.PlayStation => "ms-appx:///Assets/Icons/PSN.png",
                Platforms.Xbox => "ms-appx:///Assets/Icons/xbox.png",
                _ => "ms-appx:///Assets/Icons/UnknownProvider.png"
            };
        }
    }

    [JsonIgnore]
    public Uri? IconUri => TryCreateUri(Icon);

    [JsonIgnore]
    public Uri? ProviderImageUri => TryCreateUri(ProviderImage);
    
    /// <summary>
    /// Returns a formatted string of the total playtime
    /// </summary>
    [JsonIgnore]
    public string PlaytimeText
    {
        get
        {
            switch (PlaytimeMinutes)
            {
                // -1 means not tracked
                case < 0:
                    return "Playtime unavailable";
                // No playtime
                case <= 0:
                    return "Never played";
            }

            int hours = PlaytimeMinutes / 60;
            int minutes = PlaytimeMinutes % 60;

            // Greater than or equal to an hour played.
            if (hours > 0)
            {
                return minutes > 0 ? $"{hours} hr {minutes} min" : $"{hours} hr";
            }
            
            // Less than an hour
            return $"{minutes} min";
        }
    }
    
    /// <summary>
    /// Returns formatted HowLongToBeat main story time
    /// </summary>
    [JsonIgnore]
    public string HowLongToBeatMainText => FormatHowLongToBeatTime(HowLongToBeatMain, "Main Story");
    
    /// <summary>
    /// Returns formatted HowLongToBeat completionist time
    /// </summary>
    [JsonIgnore]
    public string HowLongToBeatCompletionistText => FormatHowLongToBeatTime(HowLongToBeatCompletionist, "100% Complete");
    
    /// <summary>
    /// Returns true if HowLongToBeat data is available
    /// </summary>
    [JsonIgnore]
    public bool HasHowLongToBeatData => HowLongToBeatMain.HasValue || HowLongToBeatCompletionist.HasValue;
    
    /// <summary>
    /// Formats HowLongToBeat time values for display
    /// </summary>
    private string FormatHowLongToBeatTime(double? hours, string label)
    {
        if (!hours.HasValue || hours.Value <= 0)
            return string.Empty;
        
        if (hours.Value < 1)
        {
            int minutes = (int)(hours.Value * 60);
            return $"{label}: {minutes} min";
        }
        
        int wholeHours = (int)hours.Value;
        int remainingMinutes = (int)((hours.Value - wholeHours) * 60);
        
        if (remainingMinutes > 0)
            return $"{label}: {wholeHours}h {remainingMinutes}m";
        
        return $"{label}: {wholeHours}h";
    }

    private static Uri? TryCreateUri(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
    }
    #endregion
}
