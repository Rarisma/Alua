using System.ComponentModel;
using Alua.Services;
using Alua.Services.Providers;
using Alua.Services.ViewModels;
using Alua.UI.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using SettingsVM = Alua.Services.ViewModels.SettingsVM;

namespace Alua.UI;

public sealed partial class Settings : Page, INotifyPropertyChanged
{
    private SettingsVM _settingsVM = Ioc.Default.GetRequiredService<SettingsVM>();
    private MicrosoftAuthService _msAuthService;
    private bool _isXboxAuthenticated;
    private bool _isAuthenticating;
    
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
    
    public event PropertyChangedEventHandler? PropertyChanged;
    
    public Settings()
    {
        InitializeComponent();
        _msAuthService = new MicrosoftAuthService();
        
        // Check if we have stored auth data
        Task.Run(async () => 
        {
            if (!string.IsNullOrEmpty(_settingsVM.MicrosoftAuthData))
            {
                var restored = await _msAuthService.RestoreAuthDataAsync(_settingsVM.MicrosoftAuthData);
                IsXboxAuthenticated = restored;
            }
        });
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
                _settingsVM.XboxGamertag = result.Account?.Username?.Split('@')[0]; // Extract gamertag
                IsXboxAuthenticated = true;
                
                await _settingsVM.Save();
                Log.Information("Xbox authentication successful for {Gamertag}", _settingsVM.XboxGamertag);
                
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
                            Log.Information("Xbox provider successfully initialized");
                            
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
                            Log.Warning("Xbox provider initialization failed");
                        }
                    }
                }
                catch (Exception providerEx)
                {
                    Log.Error(providerEx, "Failed to initialize Xbox provider after authentication");
                }
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

    #region Debug helpers
    /// <summary>
    /// Shows set up page again.
    /// </summary>
    private void ShowInitialPage() => App.Frame.Navigate(typeof(Initalize));
    
    /// <summary>
    /// Shows log for session in a dialog
    /// </summary>
    private async Task ShowLogs()
    {
        string log = await File.ReadAllTextAsync(Path.Combine(ApplicationData.Current.LocalFolder.Path, "alua" +
            DateTime.Now.ToString("yyyyMMdd") + ".log"));
        
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
