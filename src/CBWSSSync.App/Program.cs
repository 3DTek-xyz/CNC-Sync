using Avalonia;
using System;

namespace CBWSSSync.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new MacOSPlatformOptions
            {
                // Keep dotnet-run and other development launches visible in the Dock so the app
                // is easy to find while we're iterating. Only hide the Dock icon for packaged .app launches.
                ShowInDock = !IsPackagedMacApp(),
            })
            .WithInterFont()
            .LogToTrace();

    private static bool IsPackagedMacApp()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        var processPath = Environment.ProcessPath;
        return !string.IsNullOrWhiteSpace(processPath) &&
               processPath.Contains(".app/Contents/MacOS/", StringComparison.OrdinalIgnoreCase);
    }
}
