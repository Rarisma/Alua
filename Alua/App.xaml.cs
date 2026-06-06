using System.ComponentModel;
using System.Runtime.InteropServices;
using Alua.Models;
using Alua.Services.ViewModels;
using Alua.UI;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Serilog;
using Microsoft.UI;
using Uno.Resizetizer;
//I AM COMING DOWN TO THE PAWN SHOP TO SELL MY INFRARED HEATSEEKERS FOR THE SIDEWINDER MISSILES.
namespace Alua;

public partial class App : Application
{
    // P/Invoke declarations for macOS window centering via Objective-C runtime.
    // These bind lazily on first call, so they are harmless to compile on every target;
    // CenterWindowOnMac guards the actual call with a runtime OS check.
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr sel_registerName(string selectorName);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

    private static void CenterWindowOnMac(Window window)
    {
        // Only macOS has the Objective-C runtime at /usr/lib/libobjc.dylib. On Windows/Android/
        // Linux the first P/Invoke would throw DllNotFoundException on every launch.
        if (!OperatingSystem.IsMacOS())
            return;

        try
        {
            // Get the NSApplication shared instance
            var nsApplicationClass = objc_getClass("NSApplication");
            var sharedAppSelector = sel_registerName("sharedApplication");
            var sharedApp = objc_msgSend_IntPtr(nsApplicationClass, sharedAppSelector);

            // Get the main window
            var mainWindowSelector = sel_registerName("mainWindow");
            var mainWindow = objc_msgSend_IntPtr(sharedApp, mainWindowSelector);

            if (mainWindow != IntPtr.Zero)
            {
                // Center the window
                var centerSelector = sel_registerName("center");
                objc_msgSend(mainWindow, centerSelector);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to center window on macOS");
        }
    }


    public static Frame Frame = new();
    /// <summary>
    /// Initializes the singleton application object. This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
    }

    private static Window? MainWindow { get; set; }
    private IHost? Host { get;  set; }

    private static void CleanupStaleLogs()
    {
        try
        {
            var folder = ApplicationData.Current.LocalFolder.Path;
            var cutoff = DateTime.UtcNow.AddDays(-30);
            foreach (var file in Directory.GetFiles(folder, "alua*.log"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                        File.Delete(file);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Failed to clean up stale log {File}", file);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Log cleanup sweep failed");
        }
    }

    private static void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SettingsVM settings) return;
        if (e.PropertyName == nameof(SettingsVM.UserSteamApiKey) ||
            e.PropertyName == nameof(SettingsVM.UserRetroApiKey))
        {
            AppConfig.Refresh(settings);
        }
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Configure Serilog FIRST so the settings load below — including its error path
        // (corrupt/unreadable settings file) — is actually written to the log sink.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File(
                path: Path.Combine(ApplicationData.Current.LocalFolder.Path, "alua.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
            )
            .CreateLogger();

        // Pre-load SettingsVM asynchronously BEFORE building the host so we never block
        // the UI thread on I/O (JSON deserialization + Keychain/DPAPI reads).
        // This must complete before the host registers services so that AppVM can resolve SettingsVM.
        var preloadedSettings = await SettingsVM.LoadAsync();

        var builder = this.CreateBuilder(args)
            .Configure(host => host
#if DEBUG
                // Switch to Development environment when running in DEBUG
                .UseEnvironment(Environments.Development)
#endif
                .UseLogging(configure: (context, logBuilder) =>
                {
                    // Configure log levels for different categories of logging
                    logBuilder
                        .SetMinimumLevel(
                            context.HostingEnvironment.IsDevelopment() ?
                                LogLevel.Information :
                                LogLevel.Warning)
                        .AddConsole()

                        // Default filters for core Uno Platform namespaces
                        .CoreLogLevel(LogLevel.Warning);

                    // Uno Platform namespace filter groups
                    // Uncomment individual methods to see more detailed logging
                    //// Generic Xaml events
                    //logBuilder.XamlLogLevel(LogLevel.Debug);
                    //// Layout specific messages
                    //logBuilder.XamlLayoutLogLevel(LogLevel.Debug);
                    //// Storage messages
                    //logBuilder.StorageLogLevel(LogLevel.Debug);
                    //// Binding related messages
                    //logBuilder.XamlBindingLogLevel(LogLevel.Debug);
                    //// Binder memory references tracking
                    //logBuilder.BinderMemoryReferenceLogLevel(LogLevel.Debug);
                    //// DevServer and HotReload related
                    //logBuilder.HotReloadCoreLogLevel(LogLevel.Information);
                    //// Debug JS interop
                    //logBuilder.WebAssemblyLogLevel(LogLevel.Debug);

                }, enableUnoLogging: true)
                .UseSerilog(consoleLoggingEnabled: true, fileLoggingEnabled: true)
                .UseConfiguration(configure: configBuilder =>
                    configBuilder
                        .EmbeddedSource<App>("appsettings.development.json")
                )
                .ConfigureServices(void (_, services) =>
                {
                    // Register the pre-loaded singleton instance so AppVM can resolve it immediately.
                    services.AddSingleton(preloadedSettings);
                    services.AddSingleton<AppVM>();
                    services.AddSingleton<FirstRunVM>();
                    services.AddSingleton<LibraryVM>();
                    services.AddSingleton<Services.AggregateStatisticsService>();
                    services.AddSingleton<Services.HowLongToBeatService>();
                })
            );

        MainWindow = builder.Window;

#if DEBUG
        MainWindow.UseStudio();
#endif
        MainWindow.SetWindowIcon();
        Host = builder.Build();

        // API keys are now user-provided via Settings — no longer embedded in the binary.
        var settingsVm = Host.Services.GetRequiredService<SettingsVM>();
        AppConfig.Refresh(settingsVm);
        settingsVm.PropertyChanged += OnSettingsPropertyChanged;

        Ioc.Default.ConfigureServices(Host.Services);

        // Expose the SettingsVM singleton to XAML (added before any page loads) so library-card
        // DataTemplates and the Settings preview can bind appearance preferences live via
        // {Binding ..., Source={StaticResource Settings}}. SettingsVM is INotifyPropertyChanged,
        // so toggling a preference updates the cards without a re-scan.
        Resources["Settings"] = settingsVm;

        // Bound the on-disk image cache once at startup in case a previous crash left it
        // over the size limit. Fire-and-forget so we don't hold up window activation.
        _ = Task.Run(() => UI.Controls.CachedImage.InitializeAsync());

        // Sweep stale log files older than 30 days. Serilog's retainedFileCountLimit only
        // covers files it actively rolls; older files from previous configs can stick around.
        _ = Task.Run(CleanupStaleLogs);

        // Do not repeat app initialization when the Window already has content,
        // just ensure that the window is active
        Frame = MainWindow.Content as Frame ?? new Frame();
        if (MainWindow.Content == null)
        {
            // Create a Frame to act as the navigation context and navigate to the first page
            // Place the frame in the current Window
            MainWindow.Content = Frame;
        }

        if (Frame.Content == null)
        {
            // When the navigation stack isn't restored navigate to the first page,
            // configuring the new page by passing required information as a navigation
            // parameter
            Frame.Navigate(typeof(MainPage));
        }

        // Ensure the current window is active
        MainWindow.Activate();

        // Center the window on macOS
        CenterWindowOnMac(MainWindow);
    }
}
