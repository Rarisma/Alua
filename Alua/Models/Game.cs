using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Serialization;
using Alua.Data;
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
    public string Name;
    
    /// <summary>
    /// Game developer
    /// </summary>
    [JsonInclude, JsonPropertyName("Developer")]
    public string Author;
    
    /// <summary>
    /// Icon for the game
    /// </summary>
    [JsonInclude, JsonPropertyName("Icon")]
    public string Icon;
    
    /// <summary>
    /// Total playtime in minutes
    /// </summary>
    [JsonInclude, JsonPropertyName("PlaytimeMinutes")]
    public int PlaytimeMinutes { get; set; }
    
    /// <summary>
    /// All achievements for this game
    /// </summary>
    [JsonInclude, JsonPropertyName("Achievements")]
    public ObservableCollection<Achievement> Achievements;
    
    /// <summary>
    /// Platform where this achievement originated from
    /// </summary>
    [JsonInclude, JsonPropertyName("Source")]
    public Platforms Platform { get; set; }
    
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
    public bool HasAchievements => Achievements != null & Achievements.Count != 0;
    
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
                       $"({(Math.Floor((double)UnlockedCount / Achievements.Count * 100))}%)";
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
            switch (Platform)
            {
                case Platforms.Steam:
                    return "ms-appx:///Assets/Icons/Steam.png";
                case Platforms.RetroAchievements:
                    return "ms-appx:///Assets/Icons/RetroAchievements.png";
                
                //Unknown provider
                default:
                    return "ms-appx:///Assets/Icons/UnknownProvider.png";
            }
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
            // -1 means not tracked
            if (PlaytimeMinutes < 0) { return "Playtime unavailable"; }
            
            // No playtime
            if (PlaytimeMinutes <= 0)  { return "Never played"; }
                
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
