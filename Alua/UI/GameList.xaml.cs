using System.Collections.ObjectModel;
using Alua.Services;
using Alua.UI.Controls;
using CommunityToolkit.Mvvm.DependencyInjection;
using Serilog;
using AppVM = Alua.Services.ViewModels.AppVM;
using SettingsVM = Alua.Services.ViewModels.SettingsVM;

//FHN walked so Alua could run.
namespace Alua.UI;
/// <summary>
/// Main app UI, shows all users games
/// </summary>
public partial class GameList : Page
{
    private AppVM _appVm = Ioc.Default.GetRequiredService<AppVM>();
    private SettingsVM _settingsVM = Ioc.Default.GetRequiredService<SettingsVM>();

    private bool _hideComplete, _hideNoAchievements, _hideUnstarted, _reverse;
    private bool _singleColumnLayout;
    private enum OrderBy { Name, CompletionPct, TotalCount, UnlockedCount, Playtime }
    private OrderBy _orderBy = OrderBy.Name;

    // Static flag to track if initial load has occurred
    private static bool _initialLoadCompleted = false;

    // Commands for layouts
    public AsyncCommand SingleColumnCommand => new(async () => {
        _singleColumnLayout = true;
        UpdateItemsLayout();
    });

    public AsyncCommand MultiColumnCommand => new(async () => {
        _singleColumnLayout = false;
        UpdateItemsLayout();
    });

    public GameList() { InitializeComponent(); }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Log.Information("Initialised games list");
            if (_appVm.Providers.Count == 0)
            {
                await _appVm.ConfigureProviders();
                Log.Information("No providers found, loading default providers.");
            }
            if (!_initialLoadCompleted)
            {
                if (_settingsVM.Games.Count == 0)
                {
                    Log.Information("No games found, scanning.");
                    _settingsVM.Games = [];
                    ScanCommand.Execute(null);
                }
                else
                {
                    Log.Information(_settingsVM.Games.Count + " games found, scanning.");
                    RefreshCommand.Execute(null);
                }
                _initialLoadCompleted = true;
            }
            else
            {
                // If we've already loaded once, just populate the filtered games
                _appVm.FilteredGames.Clear();
                if (_settingsVM.Games != null) { _appVm.FilteredGames.AddRange(_settingsVM.Games);}
            }
            Filter_Changed(null,null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Initial load failed");
        }
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
            
            // Handle potential duplicate identifiers by checking if key already exists
            foreach (var game in games)
            {
                if (string.IsNullOrEmpty(game.Identifier))
                {
                    Log.Warning("Game {GameName} from {Provider} has empty identifier, skipping", 
                        game.Name, provider.GetType().Name);
                    continue;
                }
                
                if (_settingsVM.Games.ContainsKey(game.Identifier))
                {
                    Log.Warning("Game with identifier {Identifier} already exists, skipping duplicate from {Provider}", 
                        game.Identifier, provider.GetType().Name);
                    continue;
                }
                
                _settingsVM.Games.Add(game.Identifier, game);
            }
            
            Log.Information("Found {Count} games from provider", games.Length);
        }
        //Save scan results
        await _settingsVM.Save();
        _appVm.FilteredGames = _settingsVM.Games.Values.ToObservableCollection();
        Log.Information("loaded {0} games, {1} achievements",
            _settingsVM.Games.Count, _settingsVM.Games.Sum(x => 
                x.Value.Achievements.Count));

        // Show a message or update a property for the UI
        _appVm.LoadingGamesSummary = "";
        
        _appVm.FilteredGames.Clear();
        _appVm.FilteredGames.AddRange(_settingsVM.Games);
        Filter_Changed(null,null);
    }

    /// <summary>
    /// Updates users games
    /// </summary>
    private async Task Refresh()
    {
        _appVm.LoadingGamesSummary = "Preparing to refresh games...";
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
            _settingsVM.Games[newGame.Identifier] = newGame;
        }

        _appVm.FilteredGames.Clear();
        await _settingsVM.Save();
        if (_settingsVM.Games != null) { _appVm.FilteredGames.AddRange(_settingsVM.Games);}
        
        _appVm.LoadingGamesSummary = "";
    }
    private void Filter_Changed(object? sender, RoutedEventArgs? e)
    {
        // read all four checkboxes
        _hideComplete = CheckHideComplete.IsChecked == true;
        _hideNoAchievements = CheckNoAchievements.IsChecked == true;
        _hideUnstarted = CheckUnstarted.IsChecked == true;
        _reverse = CheckReverse.IsChecked == true;

        // read which radio is checked
        if (RadioName.IsChecked == true) _orderBy = OrderBy.Name;
        else if (RadioCompletion.IsChecked == true) _orderBy = OrderBy.CompletionPct;
        else if (RadioTotal.IsChecked == true) _orderBy = OrderBy.TotalCount;
        else if (RadioUnlocked.IsChecked == true) _orderBy = OrderBy.UnlockedCount;
        else if (RadioPlaytime.IsChecked == true) _orderBy = OrderBy.Playtime;

        RefreshFiltered();
    }

    private void RefreshFiltered()
    {
        var list = (_settingsVM.Games ?? new ())
            .Where(g => !_hideComplete || g.Value.UnlockedCount < g.Value.Achievements.Count)
            .Where(g => !_hideNoAchievements || g.Value.HasAchievements)
            .Where(g => !_hideUnstarted || g.Value.UnlockedCount > 0);

        list = _orderBy switch
        {
            OrderBy.Name => list.OrderBy(g => g.Value.Name),
            OrderBy.CompletionPct => list.OrderBy(g => (double)g.Value.UnlockedCount / g.Value.Achievements.Count),
            OrderBy.TotalCount => list.OrderBy(g => g.Value.Achievements.Count),
            OrderBy.UnlockedCount => list.OrderBy(g => g.Value.UnlockedCount),
            OrderBy.Playtime => list.OrderBy(g => g.Value.PlaytimeMinutes),
            _ => list
        };

        if (_reverse) { list = list.Reverse(); }
        
        // replace the collection instance so ItemsRepeater sees the change
        _appVm.FilteredGames = new ObservableCollection<Game>(list.Select(g => g.Value));
        Bindings.Update();   // refresh x:Bind targets

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
        if (_singleColumnLayout)
        {
            // Single column layout
            gameRepeater.Layout = new StackLayout
            {
                Spacing = 10,
                Orientation = Orientation.Vertical
            };
        }
        else
        {
            // Multi-column layout with maximum of 4 columns
            gameRepeater.Layout = new UniformGridLayout
            {
                MinRowSpacing = 10,
                MinColumnSpacing = 10,
                ItemsStretch = UniformGridLayoutItemsStretch.Fill,
                MaximumRowsOrColumns = 4  
            };
        }
    }

    // Method to handle toggle button click
    private void ToggleLayout_Click(object sender, RoutedEventArgs e)
    {
        _singleColumnLayout = !_singleColumnLayout;
        UpdateItemsLayout();
    }

    #region Async Commands
    private AsyncCommand? _refreshCommand;
    public AsyncCommand RefreshCommand => _refreshCommand ??= new AsyncCommand(Refresh);
    private AsyncCommand? _scanCommand;
    public AsyncCommand ScanCommand => _scanCommand ??= new AsyncCommand(Scan);
    #endregion
}
