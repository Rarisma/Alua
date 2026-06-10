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

        // Expose the SettingsVM to this page's resource scope so the preview-card DataTemplate can
        // bind appearance preferences via {Binding ..., Source={StaticResource Settings}} (Uno does
        // not resolve App-level resources from inside a page DataTemplate).
        Resources["Settings"] = _settingsVM;

        _msAuthService = new MicrosoftAuthService();
        _psnAuthService = new PSNAuthService();

        // Check PSN connection state
        IsPsnConnected = !string.IsNullOrWhiteSpace(_settingsVM.PsnSSO);

        // Defer async auth restore to Loaded so it runs on the UI thread
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Enum appearance ComboBoxes are driven imperatively (their indices map 1:1 to the enum).
        InitAppearanceControls();
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

    // ---- Appearance settings + live preview ----

    private List<Game>? _previewGames;

    /// <summary>
    /// Up to five real games from the user's library for the appearance preview, chosen to span the
    /// card states the appearance settings affect: one with no playtime, one partially complete,
    /// plus the most-played others. Empty until the library has been scanned.
    /// </summary>
    public List<Game> PreviewGames => _previewGames ??= BuildPreviewGames();

    /// <summary>True when there are real games to preview (drives the "scan first" placeholder).</summary>
    public bool HasPreviewGames => PreviewGames.Count > 0;

    private List<Game> BuildPreviewGames()
    {
        var all = _settingsVM.Games.Values.ToList();
        if (all.Count == 0)
            return new List<Game>();

        var picks = new List<Game>();
        void Add(Game? g) { if (g != null && !picks.Contains(g)) picks.Add(g); }

        // Favour variety: one never-played game and one started-but-incomplete game...
        Add(all.FirstOrDefault(g => g.PlaytimeMinutes <= 0));
        Add(all.FirstOrDefault(g => g.HasAchievements && g.UnlockedCount > 0 && g.UnlockedCount < g.Achievements.Count));
        // ...then fill up to five with the most-played remaining games (recognizable, real icons).
        foreach (var g in all.OrderByDescending(g => g.PlaytimeMinutes))
        {
            if (picks.Count >= 5) break;
            Add(g);
        }

        return picks.Take(5).ToList();
    }

    /// <summary>Swaps the preview card template to match the chosen progress style (mirrors the
    /// library's own template swap so the preview shows the real card for that style).</summary>
    private void UpdatePreviewTemplate()
    {
        var key = _settingsVM.CardProgressStyle == CardProgressStyle.FilledBackground
            ? "PreviewFillCardTemplate"
            : "PreviewCardTemplate";
        if (Resources[key] is DataTemplate template)
            PreviewItems.ItemTemplate = template;
    }

    /// <summary>Sets the enum ComboBoxes' selection from the persisted values (indices map 1:1).</summary>
    private void InitAppearanceControls()
    {
        ProgressStyleCombo.SelectedIndex = (int)_settingsVM.CardProgressStyle;
        TextAlignmentCombo.SelectedIndex = (int)_settingsVM.CardTextAlignment;
        MergedModeCombo.SelectedIndex = (int)_settingsVM.MergedCompletionMode;
        UpdatePreviewTemplate();
    }

    // Toggles/sliders bind TwoWay to SettingsVM; these handlers only persist the change.
    private async void AppearanceToggled(object sender, RoutedEventArgs e) => await _settingsVM.Save();

    private async void AppearanceSliderChanged(object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) => await _settingsVM.Save();

    private async void ProgressStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedIndex: >= 0 } cb)
        {
            _settingsVM.CardProgressStyle = (CardProgressStyle)cb.SelectedIndex;
            UpdatePreviewTemplate();
            await _settingsVM.Save();
        }
    }

    private async void TextAlignmentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedIndex: >= 0 } cb)
        {
            _settingsVM.CardTextAlignment = (CardTextAlignment)cb.SelectedIndex;
            await _settingsVM.Save();
        }
    }

    private async void MergedModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedIndex: >= 0 } cb)
        {
            _settingsVM.MergedCompletionMode = (MergedCompletionMode)cb.SelectedIndex;
            await _settingsVM.Save();
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

    private void NavigationOptionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        _settingsVM.PlatformSettingsVisibility = Visibility.Collapsed;
        _settingsVM.UISettingsVisibility = Visibility.Collapsed;
        _settingsVM.MetricsSettingsVisibility = Visibility.Collapsed;
        _settingsVM.DebugSettingsVisibility = Visibility.Collapsed;

        switch (args.SelectedItemContainer?.Tag)
        {
            case "PlatformsSettings":
                _settingsVM.PlatformSettingsVisibility = Visibility.Visible;
                break;
            case "UISettings":
                _settingsVM.UISettingsVisibility = Visibility.Visible;
                break;
            case "MetricsSettings":
                _settingsVM.MetricsSettingsVisibility = Visibility.Visible;
                break;
            case "DebugSettings":
                _settingsVM.DebugSettingsVisibility = Visibility.Visible;
                break;
            default:
                throw new NotImplementedException("Unknown tag: " + args.SelectedItemContainer?.Tag);
        }
    }
}
