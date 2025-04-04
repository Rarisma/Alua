using Alua.Services;
using CommunityToolkit.Mvvm.DependencyInjection;
//
namespace Alua;

public sealed partial class SettingsPage : Page
{
    private AppVM AppVM = Ioc.Default.GetRequiredService<AppVM>();
    private SettingsVM SettingsVM = Ioc.Default.GetRequiredService<SettingsVM>();
    public SettingsPage()
    {
        InitializeComponent();
        
        
    }

}
