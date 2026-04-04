using Alua;
using Alua.UI.Controls;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace Alua.Droid;

[Activity(
    MainLauncher = true,
    ConfigurationChanges = global::Uno.UI.ActivityHelper.AllConfigChanges,
    WindowSoftInputMode = SoftInput.AdjustNothing | SoftInput.StateHidden
)]
public class MainActivity : Microsoft.UI.Xaml.ApplicationActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        global::AndroidX.Core.SplashScreen.SplashScreen.InstallSplashScreen(this);

        base.OnCreate(savedInstanceState);
    }

    public override void OnTrimMemory(TrimMemory level)
    {
        base.OnTrimMemory(level);

        if (level >= TrimMemory.RunningModerate)
            CachedImage.OnLowMemory();
    }

    public override void OnBackPressed()
    {
        var frame = App.Frame;

        if (frame?.CanGoBack != true)
        {
            base.OnBackPressed();
            return;
        }

        frame.GoBack();
    }
}
