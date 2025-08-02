using Alua.Services.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Alua.UI;

/// <summary>
/// Initial set up dialog.
/// </summary>
public partial class FirstRunPage : Page
{
    public FirstRunVM Frvm;

    public FirstRunPage()
    {
        Frvm = Ioc.Default.GetRequiredService<FirstRunVM>();
        InitializeComponent();
    } 
}
