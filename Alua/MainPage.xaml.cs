using Alua.Services;
using CommunityToolkit.Mvvm.DependencyInjection;

//FHN walked so Alua could run.
namespace Alua;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();
        App.Frame = AppContentFrame;
        App.Frame.Navigate(typeof(GameList));
        App.Frame.Navigated += async (s, e) => { await Ioc.Default.GetService<SettingsVM>().Save(); };
    }

    private void OpenSettings(object sender, RoutedEventArgs e) => App.Frame.Navigate(typeof(SettingsPage));
    private void OpenGamesList(object sender, RoutedEventArgs e) => App.Frame.Navigate(typeof(GameList));
    private void Back(object sender, RoutedEventArgs e) => App.Frame.GoBack();

}
