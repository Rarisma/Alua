using Alua.Services.Providers;
using Alua.UI;
using Microsoft.Identity.Client;
using Microsoft.UI.Dispatching;
using Serilog;

namespace Alua.Services.ViewModels;

public partial class FirstRunVM : ObservableObject
{
    private readonly SettingsVM _settingsVM;
    private readonly MicrosoftAuthService _msAuthService;
    private readonly PSNAuthService _psnAuthService;

    [ObservableProperty]
    private string? _steamID; 

    [ObservableProperty]
    private string? _retroAchievementsUser;

    [ObservableProperty]
    private string? _xboxGamertag;

    [ObservableProperty]
    private bool _isXboxAuthenticated;

    [ObservableProperty]
    private bool _isPsnAuthenticated;

    [ObservableProperty]
    private bool _isPsnAuthenticating;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _isAuthenticating;
    
    public FirstRunVM(SettingsVM settingsVM)
    {
        _settingsVM = settingsVM;
        _msAuthService = new MicrosoftAuthService();
        _psnAuthService = new PSNAuthService();
        SteamID = _settingsVM.SteamID;
        RetroAchievementsUser = _settingsVM.RetroAchievementsUsername;
        XboxGamertag = _settingsVM.XboxGamertag;
        IsPsnAuthenticated = !string.IsNullOrWhiteSpace(_settingsVM.PsnSSO);

        // Restore Xbox auth state asynchronously without blocking the UI thread.
        _ = InitXboxAuthAsync();
    }

    /// <summary>
    /// Restores stored Xbox authentication state. Property writes are marshalled to the UI
    /// thread via DispatcherQueue so CommunityToolkit observable setters fire correctly.
    /// </summary>
    private async Task InitXboxAuthAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_settingsVM.MicrosoftAuthData))
                return;

            var restored = await _msAuthService.RestoreAuthDataAsync(_settingsVM.MicrosoftAuthData);
            if (!restored)
                return;

            var gamertag = _settingsVM.XboxGamertag;
            var dispatcher = DispatcherQueue.GetForCurrentThread();
            if (dispatcher != null)
            {
                dispatcher.TryEnqueue(() =>
                {
                    IsXboxAuthenticated = true;
                    XboxGamertag = gamertag;
                });
            }
            else
            {
                // Dispatcher not available (e.g. unit test host); set directly.
                IsXboxAuthenticated = true;
                XboxGamertag = gamertag;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to restore Xbox authentication state during FirstRunVM initialization");
        }
    }
    
    /// <summary>
    /// Authenticate with Microsoft for Xbox Live access
    /// </summary>
    public async Task AuthenticateXbox()
    {
        try
        {
            IsAuthenticating = true;
            HasError = false;
            ErrorMessage = null;
            
            Log.Information("Starting Xbox authentication");
            var result = await _msAuthService.AuthenticateAsync();
            
            if (result != null)
            {
                // Store the auth data
                _settingsVM.MicrosoftAuthData = await _msAuthService.SerializeAuthDataAsync();
                IsXboxAuthenticated = true;

                // Initialize Xbox provider immediately with the current access token
                try
                {
                    var appVm = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetService<AppVM>();
                    if (appVm != null && result.AccessToken != null)
                    {
                        Log.Information("Initializing Xbox provider after authentication");
                        var providerConfigured = await appVm.ConfigureXboxProviderWithToken(result.AccessToken, _msAuthService);
                        if (providerConfigured)
                        {
                            // Get the real gamertag from the Xbox service
                            var xboxService = appVm.GetProvider<Providers.XboxService>();
                            _settingsVM.XboxGamertag = xboxService?.Gamertag
                                ?? result.Account?.Username?.Split('@')[0];
                            XboxGamertag = _settingsVM.XboxGamertag;
                            Log.Information("Xbox provider initialized. Gamertag: {Gamertag}", XboxGamertag);
                        }
                        else
                        {
                            _settingsVM.XboxGamertag = result.Account?.Username?.Split('@')[0];
                            XboxGamertag = _settingsVM.XboxGamertag;
                            Log.Warning("Xbox provider initialization failed, using email fallback for gamertag");
                        }
                    }
                }
                catch (Exception providerEx)
                {
                    Log.Error(providerEx, "Failed to initialize Xbox provider after authentication");
                    _settingsVM.XboxGamertag = result.Account?.Username?.Split('@')[0];
                    XboxGamertag = _settingsVM.XboxGamertag;
                }

                Log.Information("Xbox authentication successful for {Gamertag}", XboxGamertag);
            }
            else
            {
                ErrorMessage = "Xbox authentication failed. Please try again.";
                HasError = true;
                Log.Warning("Xbox authentication failed");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during Xbox authentication");
            ErrorMessage = $"Authentication error: {ex.Message}";
            HasError = true;
        }
        finally
        {
            IsAuthenticating = false;
        }
    }
    
    /// <summary>
    /// Authenticate with PlayStation Network via WebView
    /// </summary>
    public async Task AuthenticatePsn()
    {
        try
        {
            IsPsnAuthenticating = true;
            HasError = false;
            ErrorMessage = null;

            Log.Information("Starting PSN authentication");
            var npsso = await _psnAuthService.AuthenticateAsync();

            if (!string.IsNullOrEmpty(npsso))
            {
                _settingsVM.PsnSSO = npsso;
                IsPsnAuthenticated = true;

                // Initialize PSN provider immediately
                try
                {
                    var appVm = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetService<AppVM>();
                    if (appVm != null)
                    {
                        Log.Information("Initializing PSN provider after authentication");
                        var psn = await PSNService.Create(npsso);
                        appVm.AddProvider(psn);
                        Log.Information("PSN provider initialized successfully");
                    }
                }
                catch (Exception providerEx)
                {
                    Log.Error(providerEx, "Failed to initialize PSN provider after authentication");
                }

                Log.Information("PSN authentication successful");
            }
            else
            {
                ErrorMessage = "PSN authentication was cancelled or failed. Please try again.";
                HasError = true;
                Log.Warning("PSN authentication cancelled or failed");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during PSN authentication");
            ErrorMessage = $"PSN authentication error: {ex.Message}";
            HasError = true;
        }
        finally
        {
            IsPsnAuthenticating = false;
        }
    }

    /// <summary>
    /// Sign out from PlayStation Network
    /// </summary>
    public async Task SignOutPsn()
    {
        _settingsVM.PsnSSO = null;
        IsPsnAuthenticated = false;
        Log.Information("Signed out from PlayStation Network");

        // Remove PSN provider
        try
        {
            var appVm = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetService<AppVM>();
            if (appVm != null)
            {
                appVm.RemoveProviderOfType<PSNService>();
                Log.Information("Removed PSN provider after sign out");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to remove PSN provider after sign out");
        }
    }

    /// <summary>
    /// Sign out from Xbox Live
    /// </summary>
    public async Task SignOutXbox()
    {
        await _msAuthService.SignOutAsync();
        _settingsVM.MicrosoftAuthData = null;
        _settingsVM.XboxGamertag = null;
        XboxGamertag = null;
        IsXboxAuthenticated = false;
        Log.Information("Signed out from Xbox Live");
        
        // Remove Xbox provider
        try
        {
            var appVm = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetService<AppVM>();
            if (appVm != null)
            {
                appVm.RemoveProviderOfType<XboxService>();
                Log.Information("Removed Xbox provider after sign out");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to remove Xbox provider after sign out");
        }
    }
    
    /// <summary>True once a Steam Web API key has been saved (mirrors the Settings indicator).</summary>
    public bool IsSteamConfigured => !string.IsNullOrWhiteSpace(_settingsVM.UserSteamApiKey);

    /// <summary>True once a RetroAchievements Web API key has been saved.</summary>
    public bool IsRetroConfigured => !string.IsNullOrWhiteSpace(_settingsVM.UserRetroApiKey);

    /// <summary>
    /// Open the Steam API key setup page — the same flow Settings uses.
    /// </summary>
    public void SetupSteam() =>
        App.Frame.Navigate(typeof(ApiKeySetup), ApiKeyProvider.Steam);

    /// <summary>
    /// Open the RetroAchievements API key setup page — the same flow Settings uses.
    /// </summary>
    public void SetupRetro() =>
        App.Frame.Navigate(typeof(ApiKeySetup), ApiKeyProvider.RetroAchievements);

    /// <summary>
    /// Re-reads the API-key "Connected" indicators, e.g. after returning from the setup page.
    /// </summary>
    public void RefreshApiKeyState()
    {
        OnPropertyChanged(nameof(IsSteamConfigured));
        OnPropertyChanged(nameof(IsRetroConfigured));
    }

    /// <summary>
    /// Continue to the main UI.
    /// </summary>
    public async Task Continue()
    {
        if (string.IsNullOrWhiteSpace(_settingsVM.SteamID) &&
            string.IsNullOrWhiteSpace(_settingsVM.RetroAchievementsUsername) &&
            !IsPsnAuthenticated &&
            !IsXboxAuthenticated)
        {
            ErrorMessage = "Please configure at least one platform.";
            HasError = true;
            return;
        }

        HasError = false;
        ErrorMessage = null;
        _settingsVM.Initialised = true; // Mark first run as complete
        await _settingsVM.Save();
        
        App.Frame.Navigate(typeof(Library));
    }

    partial void OnSteamIDChanged(string? value) => ClearErrorOnChange();
    partial void OnRetroAchievementsUserChanged(string? value) => ClearErrorOnChange();
    partial void OnXboxGamertagChanged(string? value) => ClearErrorOnChange();
    partial void OnIsPsnAuthenticatedChanged(bool value) => ClearErrorOnChange();
    
    /// <summary>
    /// Resets error messages when a field is changed.
    /// </summary>yes
    private void ClearErrorOnChange()
    {
        if (HasError)
        {
            HasError = false;
            ErrorMessage = null;
        }
    }
}
