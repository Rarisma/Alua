using Alua.Services.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;
//FHN walked so Alua could run.
namespace Alua.UI;

public sealed partial class MainPage : Page
{
    AppVM _appVM = Ioc.Default.GetRequiredService<AppVM>();
    SettingsVM _settingsVM = Ioc.Default.GetRequiredService<SettingsVM>();
    
    public MainPage()
    {
        InitializeComponent();
        App.Frame = AppContentFrame;
        App.Frame.Navigated += async (_, _) =>
        {
            GC.Collect();
            await _settingsVM.Save();
        };
        
        if (_settingsVM.Initialised)
        {
            App.Frame.Navigate(typeof(GameList));
        }
        else
        {
            //Hide nav bar.
            App.Frame.Navigate(typeof(FirstRunPage));
            _appVM.CommandBarVisibility = Visibility.Collapsed;
        }
    }

    private void OpenSettings(object sender, RoutedEventArgs e)
    {
        _appVM.InitialLoadCompleted = false; //Will force reload al providers.
        App.Frame.Navigate(typeof(SettingsPage));
    }

    private void OpenGamesList(object sender, RoutedEventArgs e) => App.Frame.Navigate(typeof(GameList));
    private void Back(object sender, RoutedEventArgs e) => App.Frame.GoBack();

}
