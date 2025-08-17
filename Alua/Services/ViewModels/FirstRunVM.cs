using Alua.UI;
using Microsoft.Identity.Client;
using Serilog;

namespace Alua.Services.ViewModels;

public partial class FirstRunVM : ObservableObject
{
    private readonly SettingsVM _settingsVM;
    private readonly MicrosoftAuthService _msAuthService;

    [ObservableProperty]
    private string? _steamID;

    [ObservableProperty]
    private string? _retroAchievementsUser;

    [ObservableProperty]
    private string? _xboxGamertag;
    
    [ObservableProperty]
    private bool _isXboxAuthenticated;

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
        SteamID = _settingsVM.SteamID;
        RetroAchievementsUser = _settingsVM.RetroAchievementsUsername;
        XboxGamertag = _settingsVM.XboxGamertag;
        
        // Check if we have stored auth data
        Task.Run(async () => 
        {
            if (!string.IsNullOrEmpty(_settingsVM.MicrosoftAuthData))
            {
                var restored = await _msAuthService.RestoreAuthDataAsync(_settingsVM.MicrosoftAuthData);
                if (restored)
                {
                    IsXboxAuthenticated = true;
                    var account = await _msAuthService.GetCurrentAccountAsync();
                    if (account != null)
                    {
                        XboxGamertag = account.Username?.Split('@')[0]; // Extract gamertag from email
                    }
                }
            }
        });
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
                _settingsVM.XboxGamertag = result.Account?.Username?.Split('@')[0]; // Extract gamertag
                XboxGamertag = _settingsVM.XboxGamertag;
                IsXboxAuthenticated = true;
                
                Log.Information("Xbox authentication successful for {Gamertag}", XboxGamertag);
                
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
                            Log.Information("Xbox provider successfully initialized");
                        }
                        else
                        {
                            Log.Warning("Xbox provider initialization failed");
                        }
                    }
                }
                catch (Exception providerEx)
                {
                    Log.Error(providerEx, "Failed to initialize Xbox provider after authentication");
                    // Don't fail the whole authentication if provider init fails
                }
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
                var existingXbox = appVm.Providers.FirstOrDefault(p => p is Providers.XboxService);
                if (existingXbox != null)
                {
                    appVm.Providers.Remove(existingXbox);
                    Log.Information("Removed Xbox provider after sign out");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to remove Xbox provider after sign out");
        }
    }
    
    /// <summary>
    /// Continue to the main UI.
    /// </summary>
    public async Task Continue()
    {
        if (string.IsNullOrWhiteSpace(SteamID) && 
            string.IsNullOrWhiteSpace(RetroAchievementsUser) && 
            !IsXboxAuthenticated)
        {
            ErrorMessage = "Please configure at least one platform.";
            HasError = true;
            return;
        }

        HasError = false;
        ErrorMessage = null;

        _settingsVM.SteamID = SteamID;
        _settingsVM.RetroAchievementsUsername = RetroAchievementsUser;
        _settingsVM.Initialised = true; // Mark first run as complete
        await _settingsVM.Save();
        
        App.Frame.Navigate(typeof(GameList));
    }

    partial void OnSteamIDChanged(string? value) => ClearErrorOnChange();
    partial void OnRetroAchievementsUserChanged(string? value) => ClearErrorOnChange();
    partial void OnXboxGamertagChanged(string? value) => ClearErrorOnChange();
    
    /// <summary>
    /// Resets error messages when a field is changed.
    /// </summary>
    private void ClearErrorOnChange()
    {
        if (HasError)
        {
            HasError = false;
            ErrorMessage = null;
        }
    }
}
