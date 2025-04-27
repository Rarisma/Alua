using System.Collections.ObjectModel;
using Alua.Data;
using Alua.Services;
using CommunityToolkit.Mvvm.DependencyInjection;
using Serilog;
using SteamWebAPI2.Models;

//FHN walked so Alua could run.
namespace Alua;

public sealed partial class GameList : Page
{
    private AppVM AppVM = Ioc.Default.GetRequiredService<AppVM>();
    private SettingsVM SettingsVM = Ioc.Default.GetRequiredService<SettingsVM>();
    
    private bool _hideComplete, _hideNoAchievements, _hideUnstarted, _reverse;
    private enum OrderBy { Name, CompletionPct, TotalCount, UnlockedCount }
    private OrderBy _orderBy = OrderBy.Name;
    // what the UI actually binds to

    public GameList()
    {
        InitializeComponent();
        Scan();
    }

    /// <summary>
    /// Does a full scan of users library for each platform
    /// if we don't have any games from that provider.
    /// </summary>
    private async Task Scan()
    {
        if (SettingsVM.Games == null) { SettingsVM.Games = []; }
        //Try to load from settings.
       if (!string.IsNullOrWhiteSpace(SettingsVM.SteamID) && SettingsVM.Games.All(g => g.Platform != Platforms.Steam))
        {
            Log.Information("No games found, scanning.");
            SettingsVM.Games = await new SteamService(SettingsVM.SteamID).GetOwnedGamesAsync();
            Log.Information("Steam scan complete");
        }

        if (!string.IsNullOrWhiteSpace(SettingsVM.RetroAchivementsUsername)
            && SettingsVM.Games.All(g => g.Platform != Platforms.RetroAchievements))
        {
            SettingsVM.Games.AddRange((await new RetroAchievementsService(SettingsVM.RetroAchivementsUsername)
                .GetCompletedGamesAsync()).ToObservableCollection());
        }

        //Save scan results
        await SettingsVM.Save();
        AppVM.FilteredGames.Clear();
        AppVM.FilteredGames.AddRange(SettingsVM.Games);
        Log.Information("loaded {0} games, {1} achievements",
            SettingsVM.Games.Count, SettingsVM.Games.Sum(x => x.Achievements.Count));
    }

    /// <summary>
    /// Updates users games
    /// </summary>
    public async Task Refresh()
    {
        List<Game> games = new();
        if (SettingsVM.SteamID != null)
        {
            games.AddRange(await new SteamService(SettingsVM.SteamID).GetRecentlyPlayedGames());
        }
    
        if (SettingsVM.RetroAchivementsUsername != null)
        {
            games.AddRange((await new RetroAchievementsService(SettingsVM.RetroAchivementsUsername)
                .GetCompletedGamesAsync()).ToObservableCollection());
        }
    
        // Update or add new games.
        foreach (var newGame in games)
        {
            var existing = SettingsVM.Games.FirstOrDefault(g => g.Name == newGame.Name);
            if (existing != null)
            {
                int index = SettingsVM.Games.IndexOf(existing);
                SettingsVM.Games[index] = newGame;
            }
            else
            {
                SettingsVM.Games.Add(newGame);
            }
        }
    
        // Optionally remove games not present in the new list.
        for (int i = SettingsVM.Games.Count - 1; i >= 0; i--)
        {
            var game = SettingsVM.Games[i];
            if (!games.Any(g => g.Name == game.Name))
            {
                SettingsVM.Games.RemoveAt(i);
            }
        }
        
        AppVM.FilteredGames.Clear();
        AppVM.FilteredGames.AddRange(SettingsVM.Games);
    }
    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        // read all four checkboxes
        _hideComplete      = CheckHideComplete.IsChecked == true;
        _hideNoAchievements = CheckNoAchievements.IsChecked == true;
        _hideUnstarted     = CheckUnstarted.IsChecked == true;
        _reverse           = CheckReverse.IsChecked == true;

        // read which radio is checked
        if (RadioName.IsChecked == true)            _orderBy = OrderBy.Name;
        else if (RadioCompletion.IsChecked == true) _orderBy = OrderBy.CompletionPct;
        else if (RadioTotal.IsChecked == true)      _orderBy = OrderBy.TotalCount;
        else if (RadioUnlocked.IsChecked == true)   _orderBy = OrderBy.UnlockedCount;

        RefreshFiltered();
    }
    
    private void RefreshFiltered()
    {
        var list = SettingsVM.Games
            .Where(g => !_hideComplete      || g.UnlockedCount < g.Achievements.Count)
            .Where(g => !_hideNoAchievements || g.HasAchievements)
            .Where(g => !_hideUnstarted     || g.UnlockedCount > 0);

        list = _orderBy switch
        {
            OrderBy.Name          => list.OrderBy(g => g.Name),
            OrderBy.CompletionPct => list.OrderBy(g => (double)g.UnlockedCount / g.Achievements.Count),
            OrderBy.TotalCount    => list.OrderBy(g => g.Achievements.Count),
            OrderBy.UnlockedCount => list.OrderBy(g => g.UnlockedCount),
            _                     => list
        };

        if (_reverse) list = list.Reverse();

        // replace the collection instance so ItemsRepeater sees the change
        AppVM.FilteredGames = new ObservableCollection<Game>(list);
        Bindings.Update();   // refresh x:Bind targets
    }
    /// <summary>
    /// Open Game Page
    /// </summary>
    private void OpenGame(object sender, RoutedEventArgs e)
    {
        Game game = (Game)((Button)sender).DataContext;

        AppVM.SelectedGame = game;
        App.Frame.Navigate(typeof(GamePage));
    }
}
