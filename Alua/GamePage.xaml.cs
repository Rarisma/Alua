using System.Collections.ObjectModel;
using System.Linq;
using Alua.Data;
using Alua.Services;
using CommunityToolkit.Mvvm.DependencyInjection;
//And guess what? It's not the pizza guy! 
namespace Alua;

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
        var list = (AppVM.SelectedGame.Achievements ?? new ObservableCollection<Achievement>())
            .Where(a => !_hideUnlocked || !a.IsUnlocked)
            .Where(a => !_hideHidden || !a.IsHidden);

        _filteredAchievements = new ObservableCollection<Achievement>(list);
        Bindings.Update();
    }
}
