using Alua.Services;
using CommunityToolkit.Mvvm.DependencyInjection;
using SettingsVM = Alua.Services.ViewModels.SettingsVM;

namespace Alua.UI;

public sealed partial class SettingsPage : Page
{
    private SettingsVM _settingsVM = Ioc.Default.GetRequiredService<SettingsVM>();
    public SettingsPage() => InitializeComponent();
}
