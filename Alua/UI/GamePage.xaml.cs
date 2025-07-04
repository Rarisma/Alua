using System.Collections.ObjectModel;
using Alua.Services;
using Alua.Services.Providers;
using CommunityToolkit.Mvvm.DependencyInjection;
using AppVM = Alua.Services.ViewModels.AppVM;

//And guess what? It's not the pizza guy! 
namespace Alua.UI;

public sealed partial class GamePage : Page
{
    AppVM AppVM => Ioc.Default.GetRequiredService<AppVM>();

    private ObservableCollection<Achievement> _filteredAchievements = new();
    public ObservableCollection<Achievement> FilteredAchievements => _filteredAchievements;

    private bool _hideUnlocked, _hideHidden;

    public GamePage()
    {
        InitializeComponent();
        RefreshFiltered();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        _hideUnlocked = CheckHideUnlocked.IsChecked == true;
        _hideHidden = CheckHideHidden.IsChecked == true;
        RefreshFiltered();
    }

    private void Close(object sender, RoutedEventArgs e) => App.Frame.GoBack();
    private void RefreshFiltered()
    {
        var list = (AppVM.SelectedGame?.Achievements ?? Enumerable.Empty<Achievement>())
            .Where(a => (!_hideUnlocked || !a.IsUnlocked)      // keep locked items unless _hideUnlocked is true
                        && (!_hideHidden  || !a.IsHidden || a.IsUnlocked)) // show hidden items only if they’re unlocked
            .ToList(); 
        _filteredAchievements = new ObservableCollection<Achievement>(list);
        Bindings.Update();
    }

    private void RefreshGameClick(object sender, RoutedEventArgs e) => _refresh();

    private void RefreshGamePull(RefreshContainer sender, RefreshRequestedEventArgs args) => _refresh();
    private async Task _refresh()
    {
        switch (AppVM.SelectedGame.Platform)
        {
            case Platforms.Steam:
               await AppVM.Providers.OfType<SteamService>().FirstOrDefault().RefreshTitle(AppVM.SelectedGame.Identifier);
               break;
            default:
                throw new NotImplementedException("Unimplemented platform" + AppVM.SelectedGame.Platform);
        }
    }
}
