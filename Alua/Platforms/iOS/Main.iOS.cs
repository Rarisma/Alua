using UIKit;
using Uno.UI.Hosting;
using Alua;

var host = UnoPlatformHostBuilder.Create()
    .App(() => new App())
    .UseAppleUIKit()
    .Build();

host.Run();