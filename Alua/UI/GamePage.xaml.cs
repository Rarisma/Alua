using System.Collections.ObjectModel;
using Alua.Services.Providers;
using Alua.Services.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;
using Serilog;
using AppVM = Alua.Services.ViewModels.AppVM;

//And guess what? It's not the pizza guy! 
namespace Alua.UI;

public sealed partial class GamePage : Page
{
    AppVM AppVM = Ioc.Default.GetRequiredService<AppVM>();
    SettingsVM SettingsVM = Ioc.Default.GetRequiredService<SettingsVM>();

    private ObservableCollection<Achievement> _filteredAchievements = new();
    public ObservableCollection<Achievement> FilteredAchievements => _filteredAchievements;

    private bool _showUnlocked = true, _showLocked = true, _hideHidden = true;

    public GamePage()
    {
        InitializeComponent();
        RefreshFiltered();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        _showUnlocked = CheckShowUnlocked.IsChecked == true;
        _showLocked = CheckShowLocked.IsChecked == true;
        _hideHidden = CheckHideHidden.IsChecked == true;
        RefreshFiltered();
    }

    private void Close(object sender, RoutedEventArgs e) => App.Frame.GoBack();
    private void RefreshFiltered()
    {
        var list = (AppVM.SelectedGame?.Achievements ?? Enumerable.Empty<Achievement>())
            .Where(a => (_showUnlocked || !a.IsUnlocked)) // remove unlocked if showUnlocked is false
            .Where(a => (_showLocked || a.IsUnlocked))    // remove locked if showLocked is false
            .Where(a => !_hideHidden || !a.IsHidden || a.IsUnlocked) // hide hidden items unless unlocked
            .ToList();
        _filteredAchievements = new ObservableCollection<Achievement>(list);
        Bindings.Update();
    }
    
    /// <summary>
    /// Refreshes game data for this game
    /// </summary>
    /// <exception cref="NotImplementedException">Requested game provider is not available or doesn't exist.</exception>
    private async Task Refresh()
    {
        try
        {
            //Resolve game provider
            IAchievementProvider? provider = AppVM.SelectedGame.Platform switch
            {
                Platforms.Steam => AppVM.GetProvider<SteamService>(),
                Platforms.RetroAchievements => AppVM.GetProvider<RetroAchievementsService>(),
                Platforms.PlayStation => AppVM.GetProvider<PSNService>(),
                Platforms.Xbox => AppVM.GetProvider<XboxService>(),
                _ => null
            };

            if (provider == null)
            {
                Log.Warning("No provider available for platform {Platform}", AppVM.SelectedGame.Platform);
                return;
            }

            //Update settings collection and this page's binding source.
            Game game = await provider.RefreshTitle(AppVM.SelectedGame.Identifier);
            SettingsVM.AddOrUpdateGame(game);
            AppVM.SelectedGame = game;

            await SettingsVM.Save();
            RefreshFiltered();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Cannot refresh game ID {GameId}", AppVM.SelectedGame.Identifier);
        }
    }
}
