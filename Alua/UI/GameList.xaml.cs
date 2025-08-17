using Alua.Services;
using Alua.Services.ViewModels;
using Alua.UI.Controls;
using CommunityToolkit.Mvvm.DependencyInjection;
using Serilog;
using Microsoft.UI.Xaml.Controls;
using AppVM = Alua.Services.ViewModels.AppVM;
using SettingsVM = Alua.Services.ViewModels.SettingsVM;
using static Alua.Services.ViewModels.OrderBy;

//FHN walked so Alua could run.
namespace Alua.UI;
/// <summary>
/// Main app UI, shows all users games
/// </summary>
public partial class GameList : Page
{
    private AppVM _appVm = Ioc.Default.GetRequiredService<AppVM>();
    private SettingsVM _settingsVM = Ioc.Default.GetRequiredService<SettingsVM>();
    
    // Cache the multi-column layout to prevent recreation issues
    private UniformGridLayout? _multiColumnLayout;

    // Commands for layouts
    public AsyncCommand SingleColumnCommand => new(async () => {
        _appVm.SingleColumnLayout = true;
        _settingsVM.SingleColumnLayout = true;
        LayoutToggle.IsOn = true;
        UpdateItemsLayout();
        await _settingsVM.Save();
    });

    public AsyncCommand MultiColumnCommand => new(async () => {
        _appVm.SingleColumnLayout = false;
        _settingsVM.SingleColumnLayout = false;
        LayoutToggle.IsOn = false;
        UpdateItemsLayout();
        await _settingsVM.Save();
    });

    public GameList() { InitializeComponent(); }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Log.Information("Initialised games list");
            _appVm.CommandBarVisibility = Visibility.Visible;
            // Load persisted filter settings into the app VM
            _appVm.HideComplete = _settingsVM.HideComplete;
            _appVm.HideNoAchievements = _settingsVM.HideNoAchievements;
            _appVm.HideUnstarted = _settingsVM.HideUnstarted;
            _appVm.Reverse = _settingsVM.Reverse;
            _appVm.OrderBy = _settingsVM.OrderBy;
            _appVm.SingleColumnLayout = _settingsVM.SingleColumnLayout;
            
            // Restore UI controls from VM state
            RestoreFilterUIFromVM();
            
            if (!_appVm.InitialLoadCompleted)
            {
                await _appVm.ConfigureProviders();

                if (_settingsVM.Games.Count == 0)
                {
                    Log.Information("No games found, scanning.");
                    _settingsVM.Games = [];
                    ScanCommand.Execute(null);
                }
                else
                {
                    Log.Information(_settingsVM.Games.Count + " games found, refreshing.");
                    RefreshCommand.Execute(null);
                }
                _appVm.InitialLoadCompleted = true;
            }
            else
            {
                // If we've already loaded once, just populate the filtered games
                _appVm.FilteredGames.Clear();
                foreach (var game in _settingsVM.Games.Values)
                {
                    _appVm.FilteredGames.Add(game);
                }
                Filter_Changed(null,null);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Initial load failed");
        }
    }

    private void RestoreFilterUIFromVM()
    {
        // Restore checkbox states from VM
        CheckHideComplete.IsChecked = _appVm.HideComplete;
        CheckNoAchievements.IsChecked = _appVm.HideNoAchievements;
        CheckUnstarted.IsChecked = _appVm.HideUnstarted;
        CheckReverse.IsChecked = _appVm.Reverse;

        // Restore ComboBox selection from VM
        var orderByString = _appVm.OrderBy.ToString();
        foreach (ComboBoxItem item in SortByComboBox.Items)
        {
            if (item.Tag?.ToString() == orderByString)
            {
                SortByComboBox.SelectedItem = item;
                break;
            }
        }

        // Layout toggle and repeater layout
        LayoutToggle.IsOn = _appVm.SingleColumnLayout;
        UpdateItemsLayout();
    }

    /// <summary>
    /// Does a full scan of users library for each platform
    /// if we don't have any games from that provider.
    /// </summary>
    private async Task Scan()
    {
        _settingsVM.Games = [];

        foreach (var provider in _appVm.Providers)
        {
            Log.Information("Scanning for games from {Provider}", provider.GetType().Name);
            var games = await provider.GetLibrary();
            
            foreach (var game in games)
            {
                _settingsVM.AddOrUpdateGame(game);
            }
            
            Log.Information("Found {Count} games from provider", games.Length);
        }
        
        // Fetch HowLongToBeat data for all games in parallel
        _appVm.LoadingGamesSummary = "Fetching HowLongToBeat data...";
        
        var gamesToFetch = _settingsVM.Games.Values
            .Where(g => !g.HowLongToBeatLastFetched.HasValue || 
                       (DateTime.UtcNow - g.HowLongToBeatLastFetched.Value).TotalDays >= 7)
            .ToList();
        
        Log.Information("Fetching HLTB data for {Count} games", gamesToFetch.Count);
        
        if (gamesToFetch.Any())
        {
            // Use a semaphore to limit concurrent requests to 5
            using var semaphore = new SemaphoreSlim(5, 5);
            var completedCount = 0;
            var totalCount = gamesToFetch.Count;
            
            var tasks = gamesToFetch.Select(async game =>
            {
                await semaphore.WaitAsync();
                try
                {
                    using var hltbService = new HowLongToBeatService();
                    await hltbService.FetchAndUpdateGameData(game);
                    
                    var current = Interlocked.Increment(ref completedCount);
                    _appVm.LoadingGamesSummary = $"Fetching HowLongToBeat data ({current}/{totalCount})";
                    
                    _settingsVM.AddOrUpdateGame(game);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to fetch HLTB data for {GameName}", game.Name);
                }
                finally
                {
                    semaphore.Release();
                }
            });
            
            await Task.WhenAll(tasks);
        }
        
        //Save scan results
        await _settingsVM.Save();
        Log.Information("loaded {0} games, {1} achievements",
            _settingsVM.Games.Count, _settingsVM.Games.Sum(x => 
                x.Value.Achievements.Count));

        // Show a message or update a property for the UI
        _appVm.LoadingGamesSummary = "";
        
        // Refresh the filtered games collection once after all games are loaded
        RefreshFiltered();
    }

    /// <summary>
    /// Updates users games
    /// </summary>
    private async Task Refresh()
    {
        _appVm.LoadingGamesSummary = "Preparing to refresh games...";
        
        // If this is the initial load and we have games in memory, load from memory instead of providers
        if (!_appVm.InitialLoadCompleted && _settingsVM.Games.Count > 0)
        {
            _appVm.LoadingGamesSummary = "Loading games from memory...";
            _appVm.FilteredGames.Clear();
            
            // Add a small delay to show the loading state
            await Task.Delay(100);
            
            // Load all games from memory
            foreach (var game in _settingsVM.Games.Values)
            {
                _appVm.FilteredGames.Add(game);
            }
            
            _appVm.LoadingGamesSummary = "";
            Filter_Changed(null, null);
            return;
        }
        
        // Regular refresh logic - get recent games from providers
        List<Game> games = new();

        foreach (var provider in _appVm.Providers)
        {
            Log.Information("Getting recent games from {Provider}", provider.GetType().Name);
            games.AddRange(await provider.RefreshLibrary());
            Log.Information("Found {Count} games from provider", games.Count);
        }
        
        // Update or add new games.
        foreach (var newGame in games)
        {
            _settingsVM.AddOrUpdateGame(newGame);
        }
        
        // Fetch HowLongToBeat data for new/updated games in parallel
        var gamesToFetch = games.Where(g => !g.HowLongToBeatLastFetched.HasValue || 
                                           (DateTime.UtcNow - g.HowLongToBeatLastFetched.Value).TotalDays >= 7)
                                .ToList();
        
        if (gamesToFetch.Any())
        {
            _appVm.LoadingGamesSummary = "Fetching HowLongToBeat data...";
            Log.Information("Fetching HLTB data for {Count} refreshed games", gamesToFetch.Count);
            
            // Use a semaphore to limit concurrent requests to 5
            using var semaphore = new SemaphoreSlim(5, 5);
            var completedCount = 0;
            var totalCount = gamesToFetch.Count;
            
            var tasks = gamesToFetch.Select(async game =>
            {
                await semaphore.WaitAsync();
                try
                {
                    using var hltbService = new HowLongToBeatService();
                    await hltbService.FetchAndUpdateGameData(game);
                    
                    var current = Interlocked.Increment(ref completedCount);
                    _appVm.LoadingGamesSummary = $"Fetching HowLongToBeat data ({current}/{totalCount})";
                    
                    _settingsVM.AddOrUpdateGame(game);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to fetch HLTB data for {GameName}", game.Name);
                }
                finally
                {
                    semaphore.Release();
                }
            });
            
            await Task.WhenAll(tasks);
        }

        // Clear and repopulate FilteredGames with ALL games from memory
        _appVm.FilteredGames.Clear();
        await _settingsVM.Save();
        
        // Ensure we load ALL games from memory, not just the recent ones
        foreach (var game in _settingsVM.Games.Values)
        {
            _appVm.FilteredGames.Add(game);
        }
        
        _appVm.LoadingGamesSummary = "";
        Filter_Changed(null,null);
    }
    private async void Filter_Changed(object? sender, RoutedEventArgs? e)
    {
        // read all four checkboxes
        _appVm.HideComplete = CheckHideComplete.IsChecked == true;
        _appVm.HideNoAchievements = CheckNoAchievements.IsChecked == true;
        _appVm.HideUnstarted = CheckUnstarted.IsChecked == true;
        _appVm.Reverse = CheckReverse.IsChecked == true;

        // read which item is selected in the ComboBox
        if (SortByComboBox.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is string tag)
        {
            _appVm.OrderBy = Enum.Parse<OrderBy>(tag);
        }

        // Persist settings
        _settingsVM.HideComplete = _appVm.HideComplete;
        _settingsVM.HideNoAchievements = _appVm.HideNoAchievements;
        _settingsVM.HideUnstarted = _appVm.HideUnstarted;
        _settingsVM.Reverse = _appVm.Reverse;
        _settingsVM.OrderBy = _appVm.OrderBy;

        await _settingsVM.Save();

        RefreshFiltered();
    }

    private void RefreshFiltered()
    {
        var list = (_settingsVM.Games ?? new ())
            .Where(g => !_appVm.HideComplete || g.Value.UnlockedCount < g.Value.Achievements.Count)
            .Where(g => !_appVm.HideNoAchievements || g.Value.HasAchievements)
            .Where(g => !_appVm.HideUnstarted || g.Value.UnlockedCount > 0);

        // Filter out games without HLTB data when sorting by HLTB
        if (_appVm.OrderBy == HowLongToBeatMain)
        {
            list = list.Where(g => g.Value.HowLongToBeatMain.HasValue);
        }
        else if (_appVm.OrderBy == HowLongToBeatCompletionist)
        {
            list = list.Where(g => g.Value.HowLongToBeatCompletionist.HasValue);
        }

        list = _appVm.OrderBy switch
        {
            OrderBy.Name => list.OrderBy(g => g.Value.Name),
            CompletionPct => list.OrderBy(g => (double)g.Value.UnlockedCount / g.Value.Achievements.Count),
            TotalCount => list.OrderBy(g => g.Value.Achievements.Count),
            UnlockedCount => list.OrderBy(g => g.Value.UnlockedCount),
            Playtime => list.OrderBy(g => g.Value.PlaytimeMinutes),
            LastUpdated => list.OrderByDescending(g => g.Value.LastUpdated),
            HowLongToBeatMain => list.OrderBy(g => g.Value.HowLongToBeatMain.Value),
            HowLongToBeatCompletionist => list.OrderBy(g => g.Value.HowLongToBeatCompletionist.Value),
            _ => list
        };

        if (_appVm.Reverse) { list = list.Reverse(); }
        
        // Clear and repopulate the existing collection instead of replacing it
        _appVm.FilteredGames.Clear();
        foreach (var game in list.Select(g => g.Value))
        {
            _appVm.FilteredGames.Add(game);
        }

        // Ensure layout settings are maintained after filtering
        UpdateItemsLayout();
    }
    /// <summary>
    /// Open Game Page
    /// </summary>
    private void OpenGame(object sender, RoutedEventArgs e)
    {
        Game game = (Game)((Button)sender).DataContext;
        _appVm.SelectedGame = game;
        App.Frame?.Navigate(typeof(GamePage));
    }

    // Method to update layout based on toggle state
    private void UpdateItemsLayout()
    {
        if (_appVm.SingleColumnLayout)
        {
            // Show ListView and hide repeater
            gameRepeater.Visibility = Visibility.Collapsed;
            gameListView.Visibility = Visibility.Visible;
        }
        else
        {
            // Show repeater with multi-column layout
            gameListView.Visibility = Visibility.Collapsed;
            gameRepeater.Visibility = Visibility.Visible;
            
            // Create the layout only once and reuse it
            if (_multiColumnLayout == null)
            {
                _multiColumnLayout = new UniformGridLayout
                {
                    MinRowSpacing = 10,
                    MinColumnSpacing = 10,
                    ItemsStretch = UniformGridLayoutItemsStretch.Fill,
                    MaximumRowsOrColumns = 4
                };
            }
            
            gameRepeater.Layout = _multiColumnLayout;
        }
    }

    // Method to handle toggle button click
    private async void ToggleLayout_Click(object sender, RoutedEventArgs e)
    {
        var toggle = (ToggleSwitch)sender;
        _appVm.SingleColumnLayout = toggle.IsOn;
        _settingsVM.SingleColumnLayout = toggle.IsOn;
        UpdateItemsLayout();
        await _settingsVM.Save();
    }

    #region Async Commands
    private AsyncCommand? _refreshCommand;
    public AsyncCommand RefreshCommand => _refreshCommand ??= new AsyncCommand(Refresh);
    private AsyncCommand? _scanCommand;
    public AsyncCommand ScanCommand => _scanCommand ??= new AsyncCommand(Scan);
    #endregion
}
