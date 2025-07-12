using Alua.Services;
using Alua.Services.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;
using SettingsVM = Alua.Services.ViewModels.SettingsVM;

//FHN walked so Alua could run.
namespace Alua.UI;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();
        App.Frame = AppContentFrame;
        App.Frame.Navigated += async (_, _) => { await Ioc.Default.GetRequiredService<SettingsVM>().Save(); };
        
        var settingsVM = Ioc.Default.GetRequiredService<SettingsVM>();
        App.Frame.Navigate(settingsVM.Initialised ? typeof(GameList) : typeof(FirstRunPage));
    }

    private void OpenSettings(object sender, RoutedEventArgs e)
    {
        Ioc.Default.GetRequiredService<AppVM>().InitialLoadCompleted = false;
        App.Frame.Navigate(typeof(SettingsPage));
    }

    private void OpenGamesList(object sender, RoutedEventArgs e) => App.Frame.Navigate(typeof(GameList));
    private void Back(object sender, RoutedEventArgs e) => App.Frame.GoBack();

}
