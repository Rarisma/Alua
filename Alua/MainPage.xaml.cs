using System.Collections.ObjectModel;
using Alua.Services;
using CommunityToolkit.Mvvm.DependencyInjection;
using Serilog;
//FHN walked so Alua could run.
namespace Alua;

public sealed partial class MainPage : Page
{
    public static ulong SteamID = 76561198411207982;
    private AppVM AppVM = Ioc.Default.GetRequiredService<AppVM>();
    public MainPage()
    {
        InitializeComponent();
        test();
    }

    public async void test()
    {
        App.Settings = await Data.Settings.Load();

        //Try to load from settings.
        if (!App.Settings.AllGames.Any())
        {
            Log.Information("No games found, scanning.");
            
            AppVM.Games = (await new SteamService(SteamID).GetOwnedGamesAsync()).ToObservableCollection();
            App.Settings.AllGames = AppVM.Games.ToList();
            Log.Information("Scan complete");
        }
        else
        {
            AppVM.Games = App.Settings.AllGames.ToObservableCollection();
        }
        
        //Save scan results
        await App.Settings.Save(App.Settings);
        Log.Information("loaded {0} games, {1} achievements",
            AppVM.Games.Count, AppVM.Games.Sum(x => x.Achievements.Count));
        
    }
    
}
