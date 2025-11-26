using System;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Windows.Foundation.Metadata;

//GARY HIT EM WITH THE
namespace Alua.Models;
/// <summary>
/// Representation of game.
/// </summary>
public class Game
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

    #region UI Helpers

    // Cache for computed properties
    private int? _cachedUnlockedCount;

    /// <summary>
    /// How many achievements the user has unlocked
    /// </summary>
    [JsonIgnore]
    public int UnlockedCount
    {
        get
        {
            _cachedUnlockedCount ??= Achievements.Count(x => x.IsUnlocked);
            return _cachedUnlockedCount.Value;
        }
    }

    /// <summary>
    /// Invalidates the cached UnlockedCount. Call when achievements change.
    /// </summary>
    public void InvalidateUnlockedCount() => _cachedUnlockedCount = null;
    
    /// <summary>
    /// Returns true if the user has unlocked any achievements
    /// </summary>
    [JsonIgnore]
    public bool HasAchievements => Achievements.Count != 0;
    /// <summary>
    /// Returns true if the user has unlocked any achievements
    /// </summary>
    [JsonIgnore]
    public bool HasPlaytime => PlaytimeMinutes != -1;
    
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

            if (UnlockedCount == Achievements.Count) //100%
            {
                return $"100% Complete ({Achievements.Count} Achievements)";
            }
            
            if (UnlockedCount > 0) // In progress
            {
                return $"{UnlockedCount} / {Achievements.Count} " +
                       $"({Math.Floor((double)UnlockedCount / Achievements.Count * 100)}%)";
            }

            // Has achievements, but none unlocked
            return $"Not Started ({Achievements.Count} Achievements)";
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
                Platforms.Steam => "ms-appx:///Assets/Icons/Steam.png",
                Platforms.RetroAchievements => "ms-appx:///Assets/Icons/RetroAchievements.png",
                Platforms.PlayStation => "ms-appx:///Assets/Icons/PSN.png",
                Platforms.Xbox => "ms-appx:///Assets/Icons/Xbox.png",
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
