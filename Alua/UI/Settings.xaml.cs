using System.ComponentModel;
using Alua.Services;
using Alua.Services.Providers;
using Alua.Services.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Serilog;
using SettingsVM = Alua.Services.ViewModels.SettingsVM;

namespace Alua.UI;

public sealed partial class Settings : Page, INotifyPropertyChanged
{
    private SettingsVM _settingsVM = Ioc.Default.GetRequiredService<SettingsVM>();
    private MicrosoftAuthService _msAuthService;
    private PSNAuthService _psnAuthService;
    private bool _isXboxAuthenticated;
    private bool _isAuthenticating;
    private bool _isPsnConnected;
    private bool _isPsnAuthenticating;
    
    public bool IsXboxAuthenticated
    {
        get => _isXboxAuthenticated;
        set
        {
            _isXboxAuthenticated = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsXboxAuthenticated)));
        }
    }
    
    public bool IsAuthenticating
    {
        get => _isAuthenticating;
        set
        {
            _isAuthenticating = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAuthenticating)));
        }
    }

    public bool IsPsnConnected
    {
        get => _isPsnConnected;
        set
        {
            _isPsnConnected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPsnConnected)));
        }
    }

    public bool IsPsnAuthenticating
    {
        get => _isPsnAuthenticating;
        set
        {
            _isPsnAuthenticating = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPsnAuthenticating)));
        }
    }

    public bool IsSteamApiKeyConfigured => !string.IsNullOrWhiteSpace(_settingsVM.UserSteamApiKey);
    public bool IsRetroApiKeyConfigured => !string.IsNullOrWhiteSpace(_settingsVM.UserRetroApiKey);
    public string SteamApiKeyButtonText => IsSteamApiKeyConfigured ? "Change Steam API key" : "Set up Steam API key";
    public string RetroApiKeyButtonText => IsRetroApiKeyConfigured ? "Change RetroAchievements API key" : "Set up RetroAchievements API key";

    private void RefreshApiKeyState()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSteamApiKeyConfigured)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRetroApiKeyConfigured)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SteamApiKeyButtonText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RetroApiKeyButtonText)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        RefreshApiKeyState();
    }

    public Settings()
    {
        InitializeComponent();
        _msAuthService = new MicrosoftAuthService();
        _psnAuthService = new PSNAuthService();

        // Check PSN connection state
        IsPsnConnected = !string.IsNullOrWhiteSpace(_settingsVM.PsnSSO);

        // Defer async auth restore to Loaded so it runs on the UI thread
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Restore Xbox auth state; RestoreAuthDataAsync may involve network I/O
            if (!string.IsNullOrEmpty(_settingsVM.MicrosoftAuthData))
            {
                var restored = await _msAuthService.RestoreAuthDataAsync(_settingsVM.MicrosoftAuthData);
                // Marshal the property write back to the UI thread so PropertyChanged
                // is raised on the correct thread (bound controls require this).
                DispatcherQueue.TryEnqueue(() => IsXboxAuthenticated = restored);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to restore Xbox auth data on Settings page load");
        }
    }
    
    private async Task AuthenticateXbox()
    {
        try
        {
            IsAuthenticating = true;
            
            Log.Information("Starting Xbox authentication from settings");
            var result = await _msAuthService.AuthenticateAsync();
            
            if (result != null)
            {
                // Store the auth data
                _settingsVM.MicrosoftAuthData = await _msAuthService.SerializeAuthDataAsync();
                IsXboxAuthenticated = true;

                // Initialize Xbox provider immediately with the current access token
                try
                {
                    var appVm = Ioc.Default.GetService<AppVM>();
                    if (appVm != null && result.AccessToken != null)
                    {
                        Log.Information("Initializing Xbox provider after authentication from settings");
                        var providerConfigured = await appVm.ConfigureXboxProviderWithToken(result.AccessToken, _msAuthService);
                        if (providerConfigured)
                        {
                            // Get the real gamertag from the Xbox service
                            var xboxService = appVm.GetProvider<XboxService>();
                            _settingsVM.XboxGamertag = xboxService?.Gamertag
                                ?? result.Account?.Username?.Split('@')[0];

                            Log.Information("Xbox provider initialized. Gamertag: {Gamertag}", _settingsVM.XboxGamertag);

                            // Show success message
                            var dialog = new ContentDialog
                            {
                                XamlRoot = this.XamlRoot,
                                Title = "Xbox Connected",
                                Content = $"Successfully connected to Xbox Live as {_settingsVM.XboxGamertag}. Your Xbox games will appear in the game list.",
                                PrimaryButtonText = "OK"
                            };
                            await dialog.ShowAsync();
                        }
                        else
                        {
                            _settingsVM.XboxGamertag = result.Account?.Username?.Split('@')[0];
                            Log.Warning("Xbox provider initialization failed, using email fallback for gamertag");
                        }
                    }
                }
                catch (Exception providerEx)
                {
                    Log.Error(providerEx, "Failed to initialize Xbox provider after authentication");
                    _settingsVM.XboxGamertag = result.Account?.Username?.Split('@')[0];
                }

                await _settingsVM.Save();
                Log.Information("Xbox authentication successful for {Gamertag}", _settingsVM.XboxGamertag);
            }
            else
            {
                Log.Warning("Xbox authentication failed");
                
                // Show error message
                var dialog = new ContentDialog
                {
                    XamlRoot = this.XamlRoot,
                    Title = "Authentication Failed",
                    Content = "Failed to authenticate with Xbox Live. Please try again.",
                    PrimaryButtonText = "OK"
                };
                await dialog.ShowAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during Xbox authentication");
            
            // Show error dialog
            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = "Error",
                Content = $"An error occurred during authentication: {ex.Message}",
                PrimaryButtonText = "OK"
            };
            await dialog.ShowAsync();
        }
        finally
        {
            IsAuthenticating = false;
        }
    }
    
    private async Task SignOutXbox()
    {
        await _msAuthService.SignOutAsync();
        _settingsVM.MicrosoftAuthData = null;
        _settingsVM.XboxGamertag = null;
        IsXboxAuthenticated = false;
        await _settingsVM.Save();
        Log.Information("Signed out from Xbox Live");
        
        // Remove Xbox provider
        try
        {
            var appVm = Ioc.Default.GetService<AppVM>();
            if (appVm != null && appVm.RemoveProviderOfType<XboxService>())
            {
                Log.Information("Removed Xbox provider after sign out");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to remove Xbox provider after sign out");
        }
    }

    private async Task AuthenticatePsn()
    {
        try
        {
            IsPsnAuthenticating = true;

            Log.Information("Starting PSN authentication from settings");
            var npsso = await _psnAuthService.AuthenticateAsync();

            if (!string.IsNullOrEmpty(npsso))
            {
                // Store the NPSSO token
                _settingsVM.PsnSSO = npsso;

                // Initialize PSN provider immediately
                try
                {
                    var appVm = Ioc.Default.GetService<AppVM>();
                    if (appVm != null)
                    {
                        Log.Information("Initializing PSN provider after authentication from settings");
                        appVm.RemoveProviderOfType<PSNService>();
                        var psn = await PSNService.Create(npsso);
                        appVm.AddProvider(psn);

                        Log.Information("PSN provider initialized successfully");

                        var dialog = new ContentDialog
                        {
                            XamlRoot = this.XamlRoot,
                            Title = "PlayStation Connected",
                            Content = "Successfully connected to PlayStation Network. Your PSN games will appear in the game list.",
                            PrimaryButtonText = "OK"
                        };
                        await dialog.ShowAsync();
                    }
                }
                catch (Exception providerEx)
                {
                    Log.Error(providerEx, "Failed to initialize PSN provider after authentication");
                }

                IsPsnConnected = true;
                await _settingsVM.Save();
                Log.Information("PSN authentication successful");
            }
            else
            {
                Log.Warning("PSN authentication cancelled or failed");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during PSN authentication");

            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = "Error",
                Content = $"An error occurred during PSN authentication: {ex.Message}",
                PrimaryButtonText = "OK"
            };
            await dialog.ShowAsync();
        }
        finally
        {
            IsPsnAuthenticating = false;
        }
    }

    private async Task SignOutPsn()
    {
        _settingsVM.PsnSSO = null;
        IsPsnConnected = false;
        await _settingsVM.Save();
        Log.Information("Signed out from PlayStation Network");

        // Remove PSN provider
        try
        {
            var appVm = Ioc.Default.GetService<AppVM>();
            if (appVm != null && appVm.RemoveProviderOfType<PSNService>())
            {
                Log.Information("Removed PSN provider after sign out");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to remove PSN provider after sign out");
        }
    }

    private async Task FullRescan()
    {
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = "Full Rescan",
                Content = "This will rescan all configured platforms for games and achievements. This may take several minutes. Do you want to continue?",
                PrimaryButtonText = "Yes",
                SecondaryButtonText = "Cancel"
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                // Navigate to GameList page which will trigger the scan
                App.Frame.Navigate(typeof(Library));
                
                // Get the AppVM and trigger a full scan
                var appVm = Ioc.Default.GetService<AppVM>();
                if (appVm != null)
                {
                    // Clear games to force a full rescan
                    _settingsVM.Games.Clear();
                    await _settingsVM.Save();
                    
                    Log.Information("Full rescan initiated from settings");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during full rescan");
            
            var errorDialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = "Error",
                Content = $"An error occurred during the rescan: {ex.Message}",
                PrimaryButtonText = "OK"
            };
            await errorDialog.ShowAsync();
        }
    }

    private void SetupSteamApiKey() =>
        App.Frame.Navigate(typeof(ApiKeySetup), ApiKeyProvider.Steam);

    private void SetupRetroApiKey() =>
        App.Frame.Navigate(typeof(ApiKeySetup), ApiKeyProvider.RetroAchievements);

    #region Debug helpers
    /// <summary>
    /// Shows set up page again.
    /// </summary>
    private void ShowInitialPage() => App.Frame.Navigate(typeof(Initialize));
    
    /// <summary>
    /// Shows log for session in a dialog
    /// </summary>
    private async Task ShowLogs()
    {
        var folder = ApplicationData.Current.LocalFolder.Path;
        string log;
        try
        {
            var latest = Directory.GetFiles(folder, "alua*.log")
                                  .OrderByDescending(File.GetLastWriteTimeUtc)
                                  .FirstOrDefault();
            if (latest == null)
            {
                log = "No log files were found.";
            }
            else
            {
                // Logs may be rotated mid-session; read with FileShare.ReadWrite.
                await using var stream = new FileStream(latest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                log = await reader.ReadToEndAsync();
            }
        }
        catch (Exception ex)
        {
            log = $"Failed to read log: {ex.Message}";
        }

        ContentDialog dialog = new()
        {
            XamlRoot = App.Frame.XamlRoot,
            Title = "Logs",
            Height = 600,
            Width = 600,
            PrimaryButtonText = "Close",
            Content = new ScrollViewer
            {
                Content = new TextBlock
                {
                    Text = log,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(10)
                },
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            },
            Resources = { ["ContentDialogMaxWidth"] = 1080 }
        };
        await dialog.ShowAsync();
    }
    #endregion
}
