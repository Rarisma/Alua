using System.Diagnostics;
using Serilog;

namespace Alua.Services;

/// <summary>
/// Opens URLs in the user's default browser. Used on Linux where the embedded WebView2
/// (Uno's X11 native webview) does not render reliably under XWayland, so the Steam /
/// RetroAchievements / PSN sign-in flows hand off to the system browser instead.
/// </summary>
public static class SystemBrowser
{
    public static void Open(string url)
    {
        try
        {
            if (OperatingSystem.IsLinux())
                Process.Start(new ProcessStartInfo("xdg-open", url) { UseShellExecute = false });
            else
                // Windows/macOS: shell-execute the URL so the OS picks the default handler.
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open URL {Url} in system browser", url);
        }
    }
}
