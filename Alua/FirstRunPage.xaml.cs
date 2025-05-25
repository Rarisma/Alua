using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Alua;

public partial class FirstRunPage : Page
{
    FirstRunVM _frvm;
    public FirstRunPage()
    {
        _frvm = Ioc.Default.GetRequiredService<FirstRunVM>();
        InitializeComponent();
    }
}
