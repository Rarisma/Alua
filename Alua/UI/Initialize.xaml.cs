using Alua.Services.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;
//Yo, you are now tuned into Jeleel Live!
namespace Alua.UI;

/// <summary>
/// Initial set up dialog.
/// </summary>
public partial class Initialize : Page
{
    public FirstRunVM Frvm;

    public Initialize()
    {
        Frvm = Ioc.Default.GetRequiredService<FirstRunVM>();
        InitializeComponent();
    } 
}
