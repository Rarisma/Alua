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
        Log.Information("loaded {0} games, {1} achievements",
            SettingsVM.Games.Count, SettingsVM.Games.Sum(x => x.Achievements.Count));
    }

    /// <summary>
    /// Updates users games
    /// </summary>
    public async Task Refresh()
    {
        if (SettingsVM.SteamID != null)
        {
            SettingsVM.Games = await new SteamService(SettingsVM.SteamID).GetRecentlyPlayedGames();
        }
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
