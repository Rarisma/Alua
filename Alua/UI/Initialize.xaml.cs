using Alua.Services.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;
//Yo, you are now tuned into Jeleel Live!
namespace Alua.UI;

/// <summary>
/// Initial set up dialog.
/// </summary>
public partial class Initialize : Page
{
    public FirstRunVM FRVM;

    public Initialize()
    {
        FRVM = Ioc.Default.GetRequiredService<FirstRunVM>();
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // Returning from the API-key setup page may have configured Steam/RA; refresh the indicators.
        FRVM.RefreshApiKeyState();
    }
}
