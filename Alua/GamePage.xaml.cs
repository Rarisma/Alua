using Alua.Services;
using CommunityToolkit.Mvvm.DependencyInjection;
//And guess what? It's not the pizza guy! 
namespace Alua;

public sealed partial class GamePage : Page
{
    AppVM AppVM => Ioc.Default.GetRequiredService<AppVM>();
    
    public GamePage()
    {
        InitializeComponent();
    }

    private void Close(object sender, RoutedEventArgs e) => App.Frame.GoBack();
}
