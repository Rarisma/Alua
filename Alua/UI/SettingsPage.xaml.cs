using Alua.Services;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Alua.UI;

public sealed partial class SettingsPage : Page
{
    private SettingsVM _settingsVM = Ioc.Default.GetRequiredService<SettingsVM>();
    public SettingsPage() => InitializeComponent();
}
