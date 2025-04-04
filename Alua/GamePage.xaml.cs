using System.Collections.ObjectModel;
using Alua.Services;
using CommunityToolkit.Mvvm.DependencyInjection;
using Serilog;
//And guess what? It's not the pizza guy! 
namespace Alua;

public sealed partial class GamePage
{
    AppVM AppVM => Ioc.Default.GetRequiredService<AppVM>();
    
    public GamePage()
    {
        InitializeComponent();
    }

}
