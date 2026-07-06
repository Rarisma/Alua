using System.Collections.ObjectModel;
using Alua.Helpers;
using Alua.Models;
using Alua.Services;
using Alua.Services.Providers;
using Serilog;

//Some things can never be fixed, they must be destroyed.
namespace Alua.Services.ViewModels;
/// <summary>
/// Main VM, Yeah it kinda breaks MVVM, but I don't care.
/// </summary>
public partial class AppVM : ObservableRecipient
{
    /// <summary>
    /// Tracks the scanning/refresh progress of a single provider.
    /// </summary>
    public sealed partial class ProviderScanStatus : ObservableObject
    {
        [ObservableProperty] private string _providerName = string.Empty;
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasGames))] private int _gameCount;
        [ObservableProperty] private string _status = string.Empty;
        [ObservableProperty] private double _progress;

        /// <summary>True when games have been found (GameCount > 0).</summary>
        public bool HasGames => GameCount > 0;

        /// <summary>Creates a new status item for the given provider.</summary>
        public static ProviderScanStatus Create(IAchievementProvider provider) =>
            new() { ProviderName = provider.Platform.ToString() };
    }

    private readonly object _providersLock = new();
    private readonly SettingsVM _settingsVM;

    [ObservableProperty]
    private BatchObservableCollection<Game> _filteredGames = new();

    [ObservableProperty]
    private Game _selectedGame = new();

    [ObservableProperty]
    private string _loadingGamesSummary = string.Empty;

    [ObservableProperty]
    private bool _isScanningOrRefreshing;

    /// <summary>Per-provider scan/refresh status items bound in the loading overlay.</summary>
    public ObservableCollection<ProviderScanStatus> ProviderScanStatuses { get; } = new();

    [ObservableProperty]
    private bool _initialLoadCompleted;

    // Filter properties to persist between page changes
    [ObservableProperty]
    private bool _hideComplete;

    [ObservableProperty]
    private bool _hideNoAchievements;

    [ObservableProperty]
    private bool _hideUnstarted;

    [ObservableProperty]
    private bool _reverse;

    [ObservableProperty]
    private OrderBy _orderBy = OrderBy.Name;

    [ObservableProperty]
    private bool _singleColumnLayout;

    [ObservableProperty]
    private CardProgressStyle _cardProgressStyle;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private Visibility _commandBarVisibility = Visibility.Visible;

    [ObservableProperty]
    private Visibility _gameListControlsVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    /// <summary>True when the library has no games at all (nothing scanned yet).</summary>
    [ObservableProperty]
    private bool _libraryIsEmpty;

    /// <summary>True when games exist but the current filters/search hide all of them.</summary>
    [ObservableProperty]
    private bool _hasNoVisibleGames;

    private List<IAchievementProvider> _providers = new();

    /// <summary>
    /// Initialises AppVM with an injected <see cref="SettingsVM"/> dependency.
    /// The DI container (which registers both as singletons) will supply it automatically.
    /// </summary>
    public AppVM(SettingsVM settingsVM)
    {
        _settingsVM = settingsVM;
    }

    /// <summary>
    /// Sets an error message to display to the user
    /// </summary>
    public void SetError(string message)
    {
        ErrorMessage = message;
        HasError = true;
        Log.Warning("User error displayed: {Message}", message);
    }

    /// <summary>
    /// Clears the current error message
    /// </summary>
    public void ClearError()
    {
        ErrorMessage = string.Empty;
        HasError = false;
    }

    /// <summary>
    /// Thread-safe access to providers list
    /// </summary>
    public List<IAchievementProvider> Providers
    {
        get
        {
            lock (_providersLock)
            {
                return _providers.ToList(); // Return a copy for thread safety
            }
        }
        set
        {
            lock (_providersLock)
            {
                _providers = value ?? new List<IAchievementProvider>();
            }
        }
    }

    /// <summary>
    /// Thread-safe method to add a provider
    /// </summary>
    public void AddProvider(IAchievementProvider provider)
    {
        lock (_providersLock)
        {
            _providers.Add(provider);
        }
    }

    /// <summary>
    /// Thread-safe method to remove a provider
    /// </summary>
    public bool RemoveProvider(IAchievementProvider provider)
    {
        lock (_providersLock)
        {
            return _providers.Remove(provider);
        }
    }

    /// <summary>
    /// Thread-safe method to find and remove a provider by type
    /// </summary>
    public bool RemoveProviderOfType<T>() where T : IAchievementProvider
    {
        lock (_providersLock)
        {
            var provider = _providers.OfType<T>().FirstOrDefault();
            if (provider != null)
            {
                return _providers.Remove(provider);
            }
            return false;
        }
    }

    /// <summary>
    /// Thread-safe method to get a provider by type
    /// </summary>
    public T? GetProvider<T>() where T : class, IAchievementProvider
    {
        lock (_providersLock)
        {
            return _providers.OfType<T>().FirstOrDefault();
        }
    }

    /// <summary>
    /// Adds or updates the Xbox provider when authentication changes
    /// </summary>
    public async Task<bool> ConfigureXboxProvider(string? microsoftAuthData)
    {
        try
        {
            // Remove existing Xbox provider if any
            if (RemoveProviderOfType<XboxService>())
            {
                Log.Information("Removed existing Xbox provider");
            }

            if (string.IsNullOrWhiteSpace(microsoftAuthData))
            {
                Log.Information("No Microsoft auth data provided, Xbox provider not configured");
                return false;
            }

            Log.Information("Configuring Xbox Live provider");

            // Create Microsoft auth service and restore authentication
            var msAuthService = new MicrosoftAuthService();
            var restored = await msAuthService.RestoreAuthDataAsync(microsoftAuthData);

            if (!restored)
            {
                Log.Warning("Failed to restore Xbox authentication from stored data");
                return false;
            }

            var accessToken = msAuthService.GetCachedAccessToken();
            if (string.IsNullOrEmpty(accessToken))
            {
                Log.Warning("Xbox authentication data exists but access token is expired or unavailable");
                return false;
            }

            return await ConfigureXboxProviderCore(accessToken, msAuthService);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to configure Xbox provider");
            return false;
        }
    }

    /// <summary>
    /// Adds or updates the Xbox provider using a fresh access token
    /// </summary>
    public async Task<bool> ConfigureXboxProviderWithToken(string accessToken, MicrosoftAuthService authService)
    {
        try
        {
            // Remove existing Xbox provider if any
            if (RemoveProviderOfType<XboxService>())
            {
                Log.Information("Removed existing Xbox provider");
            }

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                Log.Information("No access token provided, Xbox provider not configured");
                return false;
            }

            Log.Information("Configuring Xbox Live provider with access token");

            return await ConfigureXboxProviderCore(accessToken, authService);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to configure Xbox provider with token");
            return false;
        }
    }

    /// <summary>
    /// Core Xbox provider configuration: creates the <see cref="XboxService"/>, wires up the
    /// token-refresh callback, and registers the provider.  All three public entry-points
    /// (ConfigureXboxProvider, ConfigureXboxProviderWithToken, ConfigureProviders) delegate here.
    /// </summary>
    private async Task<bool> ConfigureXboxProviderCore(string accessToken, MicrosoftAuthService msAuthService)
    {
        var xbox = await XboxService.Create(accessToken);

        if (xbox == null)
        {
            Log.Warning("Xbox provider creation returned null; skipping registration");
            return false;
        }

        if (xbox._apiClient != null)
        {
            xbox._apiClient.SetTokenRefreshCallback(async () =>
            {
                Log.Information("Refreshing Xbox Live access token");
                var newToken = await msAuthService.RefreshAccessTokenAsync();
                if (!string.IsNullOrEmpty(newToken))
                {
                    _settingsVM.MicrosoftAuthData = await msAuthService.SerializeAuthDataAsync();
                    await _settingsVM.Save();
                }
                return newToken;
            });
        }

        AddProvider(xbox);
        Log.Information("Successfully configured Xbox Live provider");
        return true;
    }

    public async Task ConfigureProviders()
    {
        try
        {
            Log.Information("Configuring providers");
            ClearError();
            Providers = new();
            var errors = new List<string>();

            if (!string.IsNullOrWhiteSpace(_settingsVM.SteamID))
            {
                if (string.IsNullOrWhiteSpace(AppConfig.SteamAPIKey))
                {
                    Log.Warning("Steam configured but no API key provided; skipping");
                    errors.Add("Steam: API key required. Add it in Settings.");
                }
                else
                {
                    try
                    {
                        Log.Information("Configuring steam achievements");
                        var steam = await SteamService.Create(_settingsVM.SteamID);
                        AddProvider(steam);
                        Log.Information("Successfully configured steam achievements");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to configure Steam provider");
                        errors.Add("Steam: Unable to connect. Check your Steam ID.");
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(_settingsVM.RetroAchievementsUsername))
            {
                if (string.IsNullOrWhiteSpace(AppConfig.RAAPIKey))
                {
                    Log.Warning("RetroAchievements configured but no API key provided; skipping");
                    errors.Add("RetroAchievements: API key required. Add it in Settings.");
                }
                else
                {
                    try
                    {
                        Log.Information("Configuring retro achievements");
                        var ra = await RetroAchievementsService.Create(_settingsVM.RetroAchievementsUsername);
                        AddProvider(ra);
                        Log.Information("Successfully configured retro achievements");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to configure RetroAchievements provider");
                        errors.Add("RetroAchievements: Unable to connect. Check your username.");
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(_settingsVM.PsnSSO))
            {
                try
                {
                    Log.Information("Configuring PSN achievements");
                    var psn = await PSNService.Create(_settingsVM.PsnSSO);
                    AddProvider(psn);
                    Log.Information("Successfully configured PSN achievements");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to configure PSN provider");
                    errors.Add("PlayStation: Unable to connect. Your NPSSO token may have expired.");
                }
            }

            if (!string.IsNullOrWhiteSpace(_settingsVM.MicrosoftAuthData))
            {
                Log.Information("Configuring Xbox Live achievements");

                // Create Microsoft auth service and restore authentication
                var msAuthService = new MicrosoftAuthService();
                var restored = await msAuthService.RestoreAuthDataAsync(_settingsVM.MicrosoftAuthData);

                if (restored)
                {
                    var accessToken = msAuthService.GetCachedAccessToken();
                    if (!string.IsNullOrEmpty(accessToken))
                    {
                        try
                        {
                            await ConfigureXboxProviderCore(accessToken, msAuthService);

                            // Update gamertag from Xbox service if available
                            var xbox = GetProvider<XboxService>();
                            if (xbox != null && !string.IsNullOrEmpty(xbox.Gamertag))
                            {
                                _settingsVM.XboxGamertag = xbox.Gamertag;
                            }

                            Log.Information("Successfully configured Xbox Live achievements with gamertag: {Gamertag}", _settingsVM.XboxGamertag);
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Failed to create Xbox service with restored token, clearing stored auth data");
                            errors.Add("Xbox: Unable to connect. Please sign in again.");
                            // Clear invalid auth data so user can re-authenticate
                            _settingsVM.MicrosoftAuthData = null;
                            _settingsVM.XboxGamertag = null;
                            await _settingsVM.Save();
                        }
                    }
                    else
                    {
                        Log.Warning("Xbox authentication data exists but access token is expired or unavailable");
                        errors.Add("Xbox: Session expired. Please sign in again in Settings.");
                    }
                }
                else
                {
                    Log.Warning("Failed to restore Xbox authentication from stored data - token may be expired");
                    errors.Add("Xbox: Session expired. Please sign in again in Settings.");
                }
            }

            // Report any provider configuration errors to the user
            if (errors.Any())
            {
                SetError(string.Join("\n", errors));
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialise all providers successfully.");
            SetError("Failed to connect to some services. Check Settings for details.");
        }
    }

}
