using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
//When you can't even say my name
namespace Alua.Services.ViewModels;
/// <summary>
/// Stores user data.
/// </summary>
public partial class SettingsVM  : ObservableObject
{
    private readonly object _gamesLock = new object();
    
    #region Build info
    /// <summary>
    /// Shown in settings, and used to track when a full refresh is loaded
    /// </summary>
    [JsonInclude]
    public Version Version => Assembly.GetExecutingAssembly().GetName().Version ?? new();

    /// <summary>
    /// Shown under build number, enables debug mode.
    /// Generally a reference to a song.
    /// </summary>
    [JsonIgnore]
    public string BuildString = "Too Sweet";

    /// <summary>
    /// Rescan forced if opening a settings.json below this version.
    /// This is used so all games have the same data.
    /// E.g. if a new field is being tracked in a new version.
    /// </summary>
    [JsonIgnore]
    public static Version MinimumVersion = new(0,3,0);
    #endregion

    #region Alua Data
    /// <summary>
    /// All games we have data for.
    /// </summary>
    [ObservableProperty, JsonInclude, JsonPropertyName("ScannedGames")]
    private Dictionary<string, Game> _games;
    
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
    /// PSN username of user we are getting data for.
    /// </summary>
    [ObservableProperty, JsonInclude, JsonPropertyName("psnsso")]
    private string? _psnSSO;

    /// <summary>
    /// Microsoft authentication data for Xbox Live integration (serialized)
    /// </summary>
    [ObservableProperty, JsonInclude, JsonPropertyName("microsoftAuthData")]
    private string? _microsoftAuthData;
    
    /// <summary>
    /// Xbox Live gamertag for display purposes
    /// </summary>
    [ObservableProperty, JsonInclude, JsonPropertyName("xboxGamertag")]
    private string? _xboxGamertag;

    /// <summary>
    /// Controls if we show the first run dialog or game list
    /// </summary>
    [ObservableProperty, JsonInclude, JsonPropertyName("Init")]
    private bool _initialised;
    #endregion

    #region FilterData
    [ObservableProperty, JsonPropertyName("FilterHideComplete")]
    private bool _hideComplete;

    [ObservableProperty, JsonPropertyName("FilterHideNoAchievements")]
    private bool _hideNoAchievements;

    [ObservableProperty, JsonPropertyName("FilterHideUnstarted")]
    private bool _hideUnstarted;

    [ObservableProperty, JsonPropertyName("FilterReverse")]
    private bool _reverse;

    [ObservableProperty, JsonPropertyName("FilterOrderBy")]
    private OrderBy _orderBy = OrderBy.Name;

    [ObservableProperty, JsonPropertyName("FilterSingleColumnLayout")]
    private bool _singleColumnLayout;
    #endregion

    public SettingsVM()
    {
        _games = new();
    }

    /// <summary>
    /// Adds or updates a game in the collection and notifies listeners.
    /// </summary>
    /// <param name="game">Game to add or update.</param>
    public void AddOrUpdateGame(Game game)
    {
        lock (_gamesLock)
        {
            Games[game.Identifier] = game;
        }
        OnPropertyChanged(nameof(Games));
    }


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
            
            // Create a copy of the Games dictionary to avoid collection modification during serialization
            Dictionary<string, Game> gamesCopy;
            lock (_gamesLock)
            {
                gamesCopy = new Dictionary<string, Game>(Games);
            }
            
            // Create a copy of the settings object for serialization instead of swapping the dictionary
            var settingsCopy = new SettingsVM
            {
                _games = gamesCopy,
                _steamID = _steamID,
                _retroAchievementsUsername = _retroAchievementsUsername,
                _psnSSO = _psnSSO,
                _microsoftAuthData = _microsoftAuthData,
                _xboxGamertag = _xboxGamertag,
                _initialised = _initialised,
                _hideComplete = _hideComplete,
                _hideNoAchievements = _hideNoAchievements,
                _hideUnstarted = _hideUnstarted,
                _reverse = _reverse,
                _orderBy = _orderBy,
                _singleColumnLayout = _singleColumnLayout
            };
            
            string json = JsonSerializer.Serialize(settingsCopy);
            await File.WriteAllTextAsync(settings.Path, json);
            
            Log.Information("Saved settings.");
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to save settings");
        }
    }

    ///<summary>
    /// Reads settings from disk
    /// </summary>
    public static SettingsVM Load()
    {
        try
        {
            string path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Settings.json");
            //Read from disk.
            Log.Information("Loading settings from path");
            if (File.Exists(path))
            {
                string content = File.ReadAllText(path);
                if (string.IsNullOrEmpty(content))
                {
                    Log.Warning("Settings file exists but is empty, returning default settings.");
                    return new SettingsVM();
                }
                else
                {
                    SettingsVM Model = JsonSerializer.Deserialize<SettingsVM>(content)!;
                    if (MinimumVersion > Model.Version)
                    {
                        //Alua needs to rescan users library.
                        Log.Warning("Minimum version check failed; deleting game data.");
                        Model.Games = [];
                    }
                    else {Log.Information($"Loaded settings file from version {Model.Version.ToString()}");}
                    return Model;
                }
            }
            
            //File doesn't exist.
            Log.Information("Settings file not found.");
            return new SettingsVM();
        }
        catch (Exception ex) //Loading error.
        {
            Log.Error(ex, "Could not load existing settings file");
            return new SettingsVM();
        }
    }
}
