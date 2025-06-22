using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

//When you can't even say my name
namespace Alua.Services.ViewModels;
/// <summary>
/// Main VM.
/// </summary>
public partial class SettingsVM  : ObservableObject
{
    /// <summary>
    /// Shown in settings
    /// </summary>
    [JsonIgnore]
    public string BuildNumber = "0.3.0";

    /// <summary>
    /// Shown under build number, enables debug mode.
    /// </summary>
    [JsonIgnore]
    public string BuildString = "I can't even park";
    
    /// <summary>
    /// All games we have data for.
    /// </summary>
    [ObservableProperty, JsonInclude, JsonPropertyName("ScannedGames")]
    private List<Game>? _games;
    
    /// <summary>
    /// Steam ID of user we are getting data for.
    /// </summary>
    [ObservableProperty, JsonInclude, JsonPropertyName("SteamUsername")]
    private string? _steamID;
    
    /// <summary>
    /// Steam ID of user we are getting data for.
    /// </summary>
    [ObservableProperty, JsonInclude, JsonPropertyName("RAUsername")]
    private string? _retroAchievementsUsername;

    /// <summary>
    /// Controls if we show the first run dialog or game list
    /// </summary>
    [ObservableProperty, JsonInclude, JsonPropertyName("Init")]    
    private bool _initialised;

    
    /// <summary>
    /// Saves settings to disk
    /// </summary>
    public async Task Save()
    {
        try
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
        catch (Exception e)
        {
            Log.Error(e, "Failed to save settings");
        }
    }

    /// <summary>
    /// Reads settings from disk
    /// </summary>
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
                if (string.IsNullOrEmpty(content))
                {
                    Log.Warning("Settings file is empty, returning default settings.");
                    return new SettingsVM();
                }
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
