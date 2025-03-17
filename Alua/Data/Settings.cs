using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace Alua.Data;

/// <summary>
/// User settings
/// </summary>
public class Settings
{
    /// <summary>
    /// All games we have data for.
    /// </summary>
    [JsonInclude]
    [JsonPropertyName("Games")]
    public List<Game> AllGames = new();
    
    /// <summary>
    /// Steam ID of user we are getting data for.
    /// </summary>
    [JsonInclude]
    [JsonPropertyName("SteamID")]
    public uint SteamID { get; set; }


    /// <summary>
    /// Saves settings to disk
    /// </summary>
    public async Task Save(Settings settingsModel)
    {
        //Get folder
        StorageFile settings = await ApplicationData.Current.LocalFolder.CreateFileAsync("Settings.json",
            CreationCollisionOption.ReplaceExisting);
        
        //Write to disk.
        Log.Information($"Saving settings to {settings.Path}");
        string json = JsonSerializer.Serialize(settingsModel);
        await File.WriteAllTextAsync(settings.Path, json);
        Log.Information("Saved settings.");

    }

    /// <summary>
    /// Reads settings from disk
    /// </summary>
    /// <returns></returns>
    public static async Task<Settings> Load()
    {
        try
        {
            //Get folder
            StorageFile settings = await ApplicationData.Current.LocalFolder.CreateFileAsync("Settings.json",
                CreationCollisionOption.OpenIfExists);

            //Read from disk.
            Log.Information($"Loading settings from {settings.Path}");
            if (File.Exists(settings.Path))
            {
                return JsonSerializer.Deserialize<Settings>(await File.ReadAllTextAsync(settings.Path));
            }
            
            //File doesn't exist.
            Log.Information("Settings file not found.");
            return new();
        }
        //Loading error.
        catch (Exception ex)
        {
            Log.Error(ex, "Could not load settings");
            return new();
        }

    }
}
