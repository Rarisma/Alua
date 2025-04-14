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
    [JsonInclude]
    [JsonPropertyName("GameName")]
    public string Name;
    
    /// <summary>
    /// Game developer
    /// </summary>
    [JsonInclude]
    [JsonPropertyName("Developer")]
    public string Author;
    
    /// <summary>
    /// Icon for the game
    /// </summary>
    [JsonInclude]
    [JsonPropertyName("Icon")]
    public string Icon;
    
    /// <summary>
    /// How many achievements the user has unlocked
    /// </summary>
    public int UnlockedCount => Achievements.Count(x => x.IsUnlocked);
    
    /// <summary>
    /// All achievements for this game
    /// </summary>
    [JsonInclude]
    [JsonPropertyName("Achievements")]
    public ObservableCollection<Achievement> Achievements;
    
    /// <summary>
    /// Platform where this achievement originated from
    /// </summary>
    [JsonInclude]
    [JsonPropertyName("Source")]
    public Platforms Platform { get; set; }
    
    
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
            if (!HasAchievements)
            {
                return "No Achievements";
            }

            if (UnlockedCount == Achievements.Count) //100%
            {
                return $"100% Complete ({Achievements.Count} Achievements)";
            }

            if (UnlockedCount > 0)
            {
                return $"{UnlockedCount} / {Achievements.Count} " +
                       $"({(Math.Floor((double)UnlockedCount / Achievements.Count * 100))}%)";
            }

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
                default:
                    return "ms-appx:///Assets/Icons/UnknownProvider.png";
            }
        }
    }
}
