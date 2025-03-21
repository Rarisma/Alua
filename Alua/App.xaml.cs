using System.Collections.ObjectModel;
using Alua.Data;
using Alua.Services;
using CommunityToolkit.Mvvm.DependencyInjection;
using dotenv.net;
using Uno.Resizetizer;

namespace Alua;
public partial class App : Application
{
    /// <summary>
    /// Instance of app settings
    /// </summary>
    public static Settings Settings;

    public static Frame RootFrame;
    
    public static ObservableCollection<Game> Games = new();
    /// <summary>
    /// Initializes the singleton application object. This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
    }

    public Window? MainWindow { get; private set; }
    protected IHost? Host { get; private set; }

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
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<AppVM>();
                    
                    // TODO: Register your services
                    //services.AddSingleton<IMyService, MyService>();
                })
            );
        MainWindow = builder.Window;
        DotEnv.Load();
        var envVars = DotEnv.Read();
#if DEBUG
        MainWindow.UseStudio();
#endif
        MainWindow.SetWindowIcon();

        Host = builder.Build();

        Ioc.Default.ConfigureServices(Host.Services);
        
        // Do not repeat app initialization when the Window already has content,
        // just ensure that the window is active
        if (MainWindow.Content is not Frame rootFrame)
        {
            // Create a Frame to act as the navigation context and navigate to the first page
            rootFrame = new Frame();

            RootFrame = rootFrame;
            // Place the frame in the current Window
            MainWindow.Content = RootFrame;
        }

        if (RootFrame.Content == null)
        {
            // When the navigation stack isn't restored navigate to the first page,
            // configuring the new page by passing required information as a navigation
            // parameter
            RootFrame.Navigate(typeof(MainPage), args.Arguments);
        }
        
        // Ensure the current window is active
        MainWindow.Activate();
    }
}
