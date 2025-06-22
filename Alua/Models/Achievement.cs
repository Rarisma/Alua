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
    public string Id { get; set; }
    
    /// <summary>
    /// Title of the Achievement
    /// </summary>
    [JsonInclude, JsonPropertyName("Title")]
    public string Title { get; set; }
    
    /// <summary>
    /// Description of the Achievement
    /// </summary>
    [JsonInclude, JsonPropertyName("Desc")]
    public string Description { get; set; }
    
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
    public string Icon { get; set; }

    /// <summary>
    /// Is the achievement hidden/secret
    /// </summary>
    [JsonInclude, JsonPropertyName("Hidden")]
    public bool IsHidden { get; set; }
    
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
}
