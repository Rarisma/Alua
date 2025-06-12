using Alua.Services;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Alua.UI;

public partial class FirstRunPage
{
    public FirstRunVM FRVM = Ioc.Default.GetRequiredService<FirstRunVM>();
    public FirstRunPage() => InitializeComponent();
}
