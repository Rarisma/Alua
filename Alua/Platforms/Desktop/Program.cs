using Uno.UI.Hosting;
// If you catch a glimpse, you might blind yourself.
// ReSharper disable once CheckNamespace
namespace Alua;
public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (OperatingSystem.IsLinux())
        {
            //Force XWayland to allow browser to render right
            if (Environment.GetEnvironmentVariable("GDK_BACKEND") is null)
                Environment.SetEnvironmentVariable("GDK_BACKEND", "x11");

            if (Environment.GetEnvironmentVariable("UNO_DISPLAY_SCALE_OVERRIDE") is null)
                Environment.SetEnvironmentVariable("UNO_DISPLAY_SCALE_OVERRIDE", "1.0");
        }
        
        UnoPlatformHost host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseX11()
            .UseMacOS()
            .UseWin32()
            .Build();

        host.Run();
    }
}
