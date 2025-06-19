using System.Collections.ObjectModel;
using Alua.Controls;
using Alua.Services;
using CommunityToolkit.Mvvm.DependencyInjection;
using Serilog;
//FHN walked so Alua could run.
namespace Alua.UI;
/// <summary>
/// Main app UI, shows all users games
/// </summary>
public partial class GameList
{
    private AppVM _appVm = Ioc.Default.GetRequiredService<AppVM>();
    private SettingsVM SettingsVM = Ioc.Default.GetRequiredService<SettingsVM>();

    private bool _hideComplete, _hideNoAchievements, _hideUnstarted, _reverse;
    private bool _singleColumnLayout = false; // Default to multi-column layout
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

    public GameList()
    {
        InitializeComponent();
        Log.Information("Initialised games list");
        if (_appVm.Providers.Count == 0)
        {
            Log.Information("No providers found, loading default providers.");
            _appVm.ConfigureProviders();
        }
        if (!_initialLoadCompleted)
        {
            if (SettingsVM.Games == null || SettingsVM.Games.Count == 0)
            {
                Log.Information("No games found, scanning.");
                SettingsVM.Games = [];
                ScanCommand.Execute(null);
            }
            else
            {
                Log.Information(SettingsVM.Games.Count + " games found, scanning.");
                RefreshCommand.Execute(null);
            }
            _initialLoadCompleted = true;
        }
        else
        {
            // If we've already loaded once, just populate the filtered games
            _appVm.FilteredGames.Clear();
            if (SettingsVM.Games != null) { _appVm.FilteredGames.AddRange(SettingsVM.Games);}
        }
        Filter_Changed(null,null);

    }

    /// <summary>
    /// Does a full scan of users library for each platform
    /// if we don't have any games from that provider.
    /// </summary>
    private async Task Scan()
    {
        SettingsVM.Games = [];

        foreach (var provider in _appVm.Providers)
        {
            Log.Information("Scanning for games from {Provider}", provider.GetType().Name);
            var games = await provider.GetLibrary();
            SettingsVM.Games.AddRange(games);
            Log.Information("Found {Count} games from provider", games.Count());
        }
        //Save scan results
        await SettingsVM.Save();
        _appVm.FilteredGames = SettingsVM.Games.ToObservableCollection();
        Log.Information("loaded {0} games, {1} achievements",
            SettingsVM.Games.Count, SettingsVM.Games.Sum(x => x.Achievements.Count));

        // Show a message or update a property for the UI
        _appVm.LoadingGamesSummary = "";
    }

    /// <summary>
    /// Updates users games
    /// </summary>
    private async Task Refresh()
    {
        _appVm.LoadingGamesSummary = "Preparing to refresh games...";
        SettingsVM.Games ??= [];

        List<Game> games = new();

        foreach (var provider in _appVm.Providers)
        {
            Log.Information("Getting recent games from {Provider}", provider.GetType().Name);
            games.AddRange(await provider.GetLibrary());
            Log.Information("Found {Count} games from provider", games.Count());
        }

        // Update or add new games.
        foreach (var newGame in games)
        {
            var existing = SettingsVM.Games?.FirstOrDefault(g => g.Name == newGame.Name);
            if (existing != null && SettingsVM.Games != null)
            {
                int index = SettingsVM.Games.IndexOf(existing);
                SettingsVM.Games[index] = newGame;
            }
            else
            {
                SettingsVM.Games?.Add(newGame);
            }
        }

        _appVm.FilteredGames.Clear();
        if (SettingsVM.Games != null) { _appVm.FilteredGames.AddRange(SettingsVM.Games);}


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
        var list = (SettingsVM.Games ?? new List<Game>())
            .Where(g => !_hideComplete || g.UnlockedCount < g.Achievements.Count)
            .Where(g => !_hideNoAchievements || g.HasAchievements)
            .Where(g => !_hideUnstarted || g.UnlockedCount > 0);

        list = _orderBy switch
        {
            OrderBy.Name => list.OrderBy(g => g.Name),
            OrderBy.CompletionPct => list.OrderBy(g => (double)g.UnlockedCount / g.Achievements.Count),
            OrderBy.TotalCount => list.OrderBy(g => g.Achievements.Count),
            OrderBy.UnlockedCount => list.OrderBy(g => g.UnlockedCount),
            OrderBy.Playtime => list.OrderBy(g => g.PlaytimeMinutes),
            _ => list
        };

        if (_reverse) list = list.Reverse();

        // replace the collection instance so ItemsRepeater sees the change
        _appVm.FilteredGames = new ObservableCollection<Game>(list);
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
