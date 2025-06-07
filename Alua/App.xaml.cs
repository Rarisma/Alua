using System.Collections.ObjectModel;
using Alua.Services;
using Alua.UI;
using CommunityToolkit.Mvvm.DependencyInjection;
using dotenv.net;
using Uno.Resizetizer;
//I AM COMING DOWN TO THE PAWN SHOP TO SELL MY INFRARED HEATSEEKERS FOR THE SIDEWINDER MISSLES.
namespace Alua;
public partial class App
{
    public static Frame? Frame;
    public static XamlRoot XamlRoot => MainWindow.Content.XamlRoot;
    
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

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
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
                        .EmbeddedSource<App>()
                )
                .ConfigureServices(void (_, services) =>
                {
                    services.AddSingleton<AppVM>();
                    var settings = SettingsVM.Load();
                    services.AddSingleton<SettingsVM>(settings);
                    services.AddSingleton<Services.FirstRunVM>();
                })
            );
        MainWindow = builder.Window;
        DotEnv.Load();
        var envVars = DotEnv.Read();
        AppConfig.SteamAPIKey = envVars["SteamAPI"];
        AppConfig.RAAPIKey = envVars["RetroAPI"];
#if DEBUG
        MainWindow.UseStudio();
#endif
        MainWindow.SetWindowIcon();
        Host = builder.Build();
        Ioc.Default.ConfigureServices(Host.Services);
        
        // Do not repeat app initialization when the Window already has content,
        // just ensure that the window is active
        Frame = MainWindow.Content as Frame;
        if (Frame == null)
        {
            // Create a Frame to act as the navigation context and navigate to the first page
            Frame = new();
            // Place the frame in the current Window
            MainWindow.Content = Frame;
        }

        if (Frame.Content == null)
        {
            // When the navigation stack isn't restored navigate to the first page,
            // configuring the new page by passing required information as a navigation
            // parameter

            //check if app has run before
            var settingsVM = Ioc.Default.GetRequiredService<SettingsVM>();
            // If the app has not run before, navigate to the FirstRunPage, otherwise show main UI.
            Frame.Navigate(settingsVM.Initialised ? typeof(MainPage) : typeof(FirstRunPage));
        }
        
        // Ensure the current window is active
        MainWindow.Activate();
    }
}
