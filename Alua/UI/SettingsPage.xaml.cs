using Alua.Services;
using CommunityToolkit.Mvvm.DependencyInjection;
using Serilog;
using SettingsVM = Alua.Services.ViewModels.SettingsVM;

namespace Alua.UI;

public sealed partial class SettingsPage : Page
{
    private SettingsVM _settingsVM = Ioc.Default.GetRequiredService<SettingsVM>();
    public SettingsPage() => InitializeComponent();

    #region Debug helpers
    private void ShowInitialPage(object sender, RoutedEventArgs e) => App.Frame.Navigate(typeof(FirstRunPage));

    #endregion

    private void ShowLogs(object sender, RoutedEventArgs e)
    {
        string log = File.ReadAllText(Path.Combine(ApplicationData.Current.LocalFolder.Path, "alua" +
            DateTime.Now.ToString("yyyyMMdd") + ".log"));
        
        ContentDialog logwindow = new()
        {
            XamlRoot = App.Frame.XamlRoot,
            Title = "Logs",
            Height = 600,
            Width = 600,
            PrimaryButtonText = "Close",
            Content = new ScrollViewer
            {
                Content = new TextBlock
                {
                    Text = log,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new(10)
                },
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            }
        };
        logwindow.Resources["ContentDialogMaxWidth"] = 1080;
        logwindow.ShowAsync();
    }
}
