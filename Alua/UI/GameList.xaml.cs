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
public sealed partial class GameList : Page
{
    private AppVM _appVm = Ioc.Default.GetRequiredService<AppVM>();
    private SettingsVM SettingsVM = Ioc.Default.GetRequiredService<SettingsVM>();

    private bool _hideComplete, _hideNoAchievements, _hideUnstarted, _reverse;
    private enum OrderBy { Name, CompletionPct, TotalCount, UnlockedCount }
    private OrderBy _orderBy = OrderBy.Name;
    
    // Static flag to track if initial load has occurred
    private static bool _initialLoadCompleted = false;

    public GameList()
    {
        InitializeComponent();
        Log.Information("Initialised games list");
        
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
            if (SettingsVM.Games != null)
                _appVm.FilteredGames.AddRange(SettingsVM.Games);
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

        if (!string.IsNullOrWhiteSpace(SettingsVM.SteamID))
        {
            Log.Information("No games found, scanning.");
            SettingsVM.Games = await (await SteamService.CreateAsync(SettingsVM.SteamID)).GetOwnedGamesAsync();
            Log.Information("Steam scan complete");
        }

        if (!string.IsNullOrWhiteSpace(SettingsVM.RetroAchivementsUsername))
        {
            SettingsVM.Games.AddRange((await new RetroAchievementsService(SettingsVM.RetroAchivementsUsername)
                .GetCompletedGamesAsync()).ToObservableCollection());
        }

        //Save scan results
        await SettingsVM.Save();
        _appVm.FilteredGames.Clear();
        _appVm.FilteredGames.AddRange(SettingsVM.Games);
        Log.Information("loaded {0} games, {1} achievements",
            SettingsVM.Games.Count, SettingsVM.Games.Sum(x => x.Achievements.Count));

        // Show a message or update a property for the UI
        _appVm.GamesFoundMessage = $"Found {SettingsVM.Games.Count} games.";
        _appVm.LoadingGamesSummary = "";
    }

    /// <summary>
    /// Updates users games
    /// </summary>
    private async Task Refresh()
    {
        _appVm.LoadingGamesSummary = "Prepraring to refresh games...";
        SettingsVM.Games ??= [];
        
        List<Game> games = new();
        if (SettingsVM.SteamID != null)
        {
            games.AddRange(await (await SteamService.CreateAsync(SettingsVM.SteamID)).GetRecentlyPlayedGames());
        }

        if (SettingsVM.RetroAchivementsUsername != null)
        {
            games.AddRange((await new RetroAchievementsService(SettingsVM.RetroAchivementsUsername)
                .GetCompletedGamesAsync()).ToObservableCollection());
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
        if (SettingsVM.Games != null)
            _appVm.FilteredGames.AddRange(SettingsVM.Games);
        
        
        _appVm.LoadingGamesSummary = "";
    }
    private void Filter_Changed(object sender, RoutedEventArgs e)
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
            _ => list
        };

        if (_reverse) list = list.Reverse();

        // replace the collection instance so ItemsRepeater sees the change
        _appVm.FilteredGames = new ObservableCollection<Game>(list);
        Bindings.Update();   // refresh x:Bind targets
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

    #region Async Commands
    private AsyncCommand? _refreshCommand;
    public AsyncCommand RefreshCommand => _refreshCommand ??= new AsyncCommand(Refresh);
    private AsyncCommand? _scanCommand;
    public AsyncCommand ScanCommand => _scanCommand ??= new AsyncCommand(Scan);
    #endregion
}
