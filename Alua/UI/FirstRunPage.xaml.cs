using Alua.Services.ViewModels;
namespace Alua.UI;

/// <summary>
/// Initial set up dialog.
/// </summary>
public partial class FirstRunPage : Page
{
    public FirstRunVM Frvm;

    public FirstRunPage(FirstRunVM vm)
    {
        Frvm = vm;
        InitializeComponent();
    } 
}
