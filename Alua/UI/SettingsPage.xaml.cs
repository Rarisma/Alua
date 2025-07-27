using CommunityToolkit.Mvvm.DependencyInjection;
using SettingsVM = Alua.Services.ViewModels.SettingsVM;

namespace Alua.UI;

public sealed partial class SettingsPage : Page
{
    private SettingsVM _settingsVM = Ioc.Default.GetRequiredService<SettingsVM>();
    public SettingsPage() => InitializeComponent();

    #region Debug helpers
    /// <summary>
    /// Shows set up page again.
    /// </summary>
    private void ShowInitialPage() => App.Frame.Navigate(typeof(FirstRunPage));
    
    /// <summary>
    /// Shows log for session in a dialog
    /// </summary>
    private async Task ShowLogs()
    {
        string log = await File.ReadAllTextAsync(Path.Combine(ApplicationData.Current.LocalFolder.Path, "alua" +
            DateTime.Now.ToString("yyyyMMdd") + ".log"));
        
        ContentDialog dialog = new()
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
                    Margin = new Thickness(10)
                },
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            },
            Resources = { ["ContentDialogMaxWidth"] = 1080 }
        };
        await dialog.ShowAsync();
    }
    #endregion
}
