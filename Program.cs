using Avalonia;
using Velopack;

namespace HoI4ModBuilder;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack handles install, update and uninstall hooks here and may exit right away.
        VelopackApp.Build().Run();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Used by the visual designer as well, so keep it public and side-effect free.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
