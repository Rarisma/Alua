namespace Alua.Helpers;
//Oi! you got a bindable for that mate?
public class Platform
{
    // Set up bools to monitor per plat stuff
    public static bool IsWindows { get; } = OperatingSystem.IsWindows();
    public static bool IsLinux   { get; } = OperatingSystem.IsLinux();
    public static bool IsMacOS   { get; } = OperatingSystem.IsMacOS();
    public static bool IsAndroid { get; } = OperatingSystem.IsAndroid();
    public static bool IsIOS     { get; } = OperatingSystem.IsIOS();
    public static bool IsBrowser { get; } = OperatingSystem.IsBrowser();

    // XAML bindable stuff
    public static Visibility HideOnWindows { get; } = IsWindows ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility ShowOnWindows { get; } = IsWindows ? Visibility.Visible   : Visibility.Collapsed;

    public static Visibility HideOnLinux   { get; } = IsLinux ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility ShowOnLinux   { get; } = IsLinux ? Visibility.Visible   : Visibility.Collapsed;

    public static Visibility HideOnMacOS   { get; } = IsMacOS ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility ShowOnMacOS   { get; } = IsMacOS ? Visibility.Visible   : Visibility.Collapsed;

    public static Visibility HideOnAndroid { get; } = IsAndroid ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility ShowOnAndroid { get; } = IsAndroid ? Visibility.Visible   : Visibility.Collapsed;

    public static Visibility HideOnIOS     { get; } = IsIOS ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility ShowOnIOS     { get; } = IsIOS ? Visibility.Visible   : Visibility.Collapsed;

    public static Visibility HideOnBrowser { get; } = IsBrowser ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility ShowOnBrowser { get; } = IsBrowser ? Visibility.Visible   : Visibility.Collapsed;
}
