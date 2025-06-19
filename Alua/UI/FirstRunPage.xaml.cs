using Alua.Services;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Alua.UI;

public partial class FirstRunPage : Page
{
    public FirstRunVM Frvm = Ioc.Default.GetRequiredService<FirstRunVM>();
    public FirstRunPage() => InitializeComponent();
}
