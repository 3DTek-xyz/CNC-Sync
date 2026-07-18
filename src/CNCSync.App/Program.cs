using Avalonia;
using CNCSync.App.Services;
using System;
using System.Threading;
using Velopack;

namespace CNCSync.App;

sealed class Program
{
    private static Mutex? _singleInstanceMutex;
    private static readonly TimeSpan RestartWaitTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RestartPollDelay = TimeSpan.FromMilliseconds(150);

    public static bool LaunchedAtLogin { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        LaunchedAtLogin = args.Any(arg => string.Equals(arg, AppLaunchArguments.LaunchAtLoginArgument, StringComparison.OrdinalIgnoreCase));

        if (!AcquireSingleInstanceMutex())
        {
            return;
        }

        VelopackApp.Build().Run();
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

    private static bool AcquireSingleInstanceMutex()
    {
        if (TryAcquireMutex())
        {
            return true;
        }

        _ = SingleInstanceSignal.TrySignalExistingInstanceAsync();
        var deadline = DateTime.UtcNow + RestartWaitTimeout;

        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(RestartPollDelay);
            if (TryAcquireMutex())
            {
                return true;
            }
        }

        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        return false;
    }

    private static bool TryAcquireMutex()
    {
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = new Mutex(true, @"Global\3DTek.ProCutSuiteDesktop", out var createdNew);
        if (createdNew)
        {
            return true;
        }

        _singleInstanceMutex.Dispose();
        _singleInstanceMutex = null;
        return false;
    }
}
