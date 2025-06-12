using Uno.UI.Hosting;

// ReSharper disable once CheckNamespace
namespace Alua;
public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        UnoPlatformHost host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseX11()
            .UseMacOS()
            .UseWin32()
            .Build();

        host.Run();
    }
}
