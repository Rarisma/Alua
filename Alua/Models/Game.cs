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
    public string Name { get; set; }
    
    /// <summary>
    /// Game developer
    /// </summary>
    [JsonInclude, JsonPropertyName("Developer")]
    public string Author { get; set; }
    
    /// <summary>
    /// Icon for the game
    /// </summary>
    [JsonInclude, JsonPropertyName("Icon")]
    public string Icon { get; set; }

    /// <summary>
    /// Last time the game information was updated
    /// </summary>
    [JsonInclude, JsonPropertyName("LastUpdated")]
    public DateTime LastUpdated { get; set; }
    
    /// <summary>
    /// Total playtime in minutes
    /// </summary>
    [JsonInclude, JsonPropertyName("PlaytimeMinutes")]
    public int PlaytimeMinutes { get; set; }
    
    /// <summary>
    /// All achievements for this game
    /// </summary>
    [JsonInclude, JsonPropertyName("Achievements")]
    public ObservableCollection<Achievement> Achievements { get; set; }
    
    /// <summary>
    /// Platform where this achievement originated from
    /// </summary>
    [JsonInclude, JsonPropertyName("Source")]
    public Platforms Platform { get; set; }
    
    [JsonInclude, JsonPropertyName("Identifier")]
    public string Identifier { get; set; }

    // Add parameterless constructor for JSON deserialization
    public Game()
    {
        Name = string.Empty;
        Author = string.Empty;
        Icon = string.Empty;
        Achievements = new ObservableCollection<Achievement>();
        Identifier = string.Empty;
        LastUpdated = DateTime.UtcNow;
    }

    #region UI Helpers
    /// <summary>
    /// How many achievements the user has unlocked
    /// </summary>
    [JsonIgnore]
    public int UnlockedCount => Achievements.Count(x => x.IsUnlocked);
    
    /// <summary>
    /// Returns true if the user has unlocked any achievements
    /// </summary>
    [JsonIgnore]
    public bool HasAchievements => Achievements.Count != 0;
    
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
    #endregion
}
