using Alua.Data;
using Alua.Services;
using CommunityToolkit.Mvvm.DependencyInjection;
using Serilog;
//FHN walked so Alua could run.
namespace Alua;

public sealed partial class GameList
{
    private AppVM AppVM = Ioc.Default.GetRequiredService<AppVM>();
    private SettingsVM SettingsVM = Ioc.Default.GetRequiredService<SettingsVM>();
    public GameList()
    {
        InitializeComponent();
        test();
    }

    public async void test()
    {
        if (string.IsNullOrWhiteSpace(SettingsVM.SteamID))
        {
            ContentDialog dialog = new()
            {
                Title = "Steam ID not set",
                Content = "Please set your Steam ID in the settings.",
                PrimaryButtonText = "OK",
                XamlRoot = App.XamlRoot
            };
            dialog.ShowAsync();
        }

        if (SettingsVM.Games == null)
        {
            SettingsVM.Games = new();
        }
        
        //Try to load from settings.
       if (!string.IsNullOrWhiteSpace(SettingsVM.SteamID) &&
           SettingsVM.Games.All(g => g.Platform != Platforms.Steam))
        {
            Log.Information("No games found, scanning.");
            SettingsVM.Games = await new SteamService(SettingsVM.SteamID).GetOwnedGamesAsync();
            Log.Information("Steam scan complete");
        }

        if (!string.IsNullOrWhiteSpace(SettingsVM.RetroAchivementsUsername))
            //&& SettingsVM.Games.All(g => g.Platform != Platforms.RetroAchievements))
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
    /// Open Game Page
    /// </summary>
    private void OpenGame(object sender, RoutedEventArgs e)
    {
        Game game = (Game)((Button)sender).DataContext;

        AppVM.SelectedGame = game;
        App.Frame.Navigate(typeof(GamePage));
    }
}
