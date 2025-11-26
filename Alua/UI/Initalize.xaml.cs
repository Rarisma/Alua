using Alua.Services.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Alua.UI;

/// <summary>
/// Initial set up dialog.
/// </summary>
public partial class Initalize : Page
{
    public FirstRunVM Frvm;

    public Initalize()
    {
        Frvm = Ioc.Default.GetRequiredService<FirstRunVM>();
        InitializeComponent();
    } 
}
