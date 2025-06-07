using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
//When you can't even say my name
namespace Alua.Services;
/// <summary>
/// Main VM.
/// </summary>
public class SettingsVM  : ObservableRecipient
{
    /// <summary>
    /// Shown in settings
    /// </summary>
    [JsonIgnore]
    public string BuildNumber = "0.2.0";

    /// <summary>
    /// Shown under build number, enables debug mode.
    /// </summary>
    [JsonIgnore]
    public string BuildString = "Perpetually at risk";
    
    private List<Game>? _games;
    /// <summary>
    /// All games we have data for.
    /// </summary>
    [JsonInclude, JsonPropertyName("Games")]
    public List<Game>? Games
    {
        get => _games;
        set => SetProperty( ref _games, value);
    }

    private string _steamID;
    /// <summary>
    /// Steam ID of user we are getting data for.
    /// </summary>
    [JsonInclude, JsonPropertyName("SteamID")]
    public string SteamID
    {
        get => _steamID;
        set => SetProperty(ref _steamID, value);
    }
    
    private string _retroAchievementsUsername;
    /// <summary>
    /// Steam ID of user we are getting data for.
    /// </summary>
    [JsonInclude, JsonPropertyName("RAUsername")]
    public string RetroAchivementsUsername
    {
        get => _retroAchievementsUsername;
        set => SetProperty(ref _retroAchievementsUsername, value);
    }
    
    private bool _initialised;
    /// <summary>
    /// Controls if we show the first run dialog or gamelist
    /// </summary>
    [JsonInclude, JsonPropertyName("Initialised")]
    public bool Initialised
    {
        get => _initialised;
        set => SetProperty(ref _initialised, value);
    }
    
    /// <summary>
    /// Saves settings to disk
    /// </summary>
    public async Task Save()
    {
        //Get folder
        StorageFile settings = await ApplicationData.Current.LocalFolder.CreateFileAsync("Settings.json",
            CreationCollisionOption.ReplaceExisting);
        
        //Write to disk.
        Log.Information($"Saving settings to {settings.Path}");
        string json = JsonSerializer.Serialize(this);
        await File.WriteAllTextAsync(settings.Path, json);
        Log.Information("Saved settings.");

    }

    /// <summary>
    /// Reads settings from disk
    /// </summary>
    /// <returns></returns>
    public static SettingsVM Load()
    {
        try
        {
            string path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Settings.json");
            //Read from disk.
            Log.Information($"Loading settings from path");
            if (File.Exists(path))
            {
                string content = File.ReadAllText(path);
                return JsonSerializer.Deserialize<SettingsVM>(content)!;
            }
            
            //File doesn't exist.
            Log.Information("Settings file not found.");
            return new SettingsVM();
        }
        //Loading error.
        catch (Exception ex)
        {
            Log.Error(ex, "Could not load settings");
            return new SettingsVM();
        }
    }
}
