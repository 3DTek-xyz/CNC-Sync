using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using System.Reflection;
using Avalonia.Threading;
using Avalonia.Markup.Xaml;
using Avalonia.Controls.Notifications;
using CNCSync.App.ViewModels;
using CNCSync.App.Views;
using CNCSync.Infrastructure.Configuration;
using CNCSync.Infrastructure.Logging;
using CNCSync.Infrastructure.Monitoring;
using CNCSync.Infrastructure.Networking;
using CNCSync.Infrastructure.Processing;
using CNCSync.Core.Configuration;
using CNCSync.Core.Processing;
using CNCSync.Core.Services;
using CNCSync.App.Services;

namespace CNCSync.App;

public partial class App : Application
{
    private const int MaxTrayActivityLines = 3;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MainWindowViewModel? _mainWindowViewModel;
    private bool _exitRequested;
    private bool _initialTrayHidePending;
    private readonly Queue<string> _recentTrayMessages = new();
    private TrayIcon? _appTrayIcon;
    private NativeMenuItem? _trayStatusMenuItem;
    private NativeMenuItem? _trayRecentActivityMenuItem1;
    private NativeMenuItem? _trayRecentActivityMenuItem2;
    private NativeMenuItem? _trayRecentActivityMenuItem3;
    private NativeMenuItem? _trayStartMonitoringMenuItem;
    private NativeMenuItem? _trayStopMonitoringMenuItem;
    private CancellationTokenSource? _singleInstanceCts;
    private WindowNotificationManager? _notificationManager;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        InitializeTrayReferences();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            var settingsStore = new JsonAppSettingsStore();
            var validator = new AppSettingsValidator();
            var folderMonitor = new FileSystemFolderMonitor();
            var projectProcessor = new StagingProjectProcessor();
            var ftpService = new FtpService();
            var sftpService = new SftpService();
            var scpService = new ScpService();
            var networkShareService = new NetworkShareService();
            var vpnService = new SystemVpnService();
            var destinationService = new DestinationService(ftpService, sftpService, scpService, networkShareService, vpnService);
            var updateService = new VelopackUpdateService();
            var loginStartupService = new LoginStartupService();
            var scriptBundleImportService = new ScriptBundleImportService();
            var themePreferenceService = new ThemePreferenceService();
            var coordinator = new SyncCoordinator(folderMonitor, projectProcessor, destinationService, validator);
            DiagnosticLog.Initialize(settingsStore.SettingsFilePath);
            RegisterGlobalExceptionLogging();
            DiagnosticLog.WriteInfo($"Startup begin. Settings file: {settingsStore.SettingsFilePath}");
            var initialSettings = settingsStore.Load();
            DiagnosticLog.WriteInfo(
                $"Startup settings loaded. Destinations={initialSettings.Destinations.Count}, " +
                $"ProcessingSetups={initialSettings.ProcessingSetups.Count}, WatchProfiles={initialSettings.WatchProfiles.Count}, " +
                $"LaunchedAtLogin={Program.LaunchedAtLogin}");
            var telemetryService = new PostHogUsageTelemetryService(ResolveAppVersion(), initialSettings);
            _ = Task.Run(async () =>
            {
                try
                {
                    if (telemetryService.CaptureStartupState(initialSettings, Program.LaunchedAtLogin))
                    {
                        await settingsStore.SaveAsync(initialSettings);
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticLog.WriteException("Startup telemetry capture failed.", ex);
                }
            });
            themePreferenceService.Apply(initialSettings.ThemePreference);
            _mainWindowViewModel = new MainWindowViewModel(settingsStore, validator, coordinator, destinationService, updateService, loginStartupService, scriptBundleImportService, vpnService, themePreferenceService, telemetryService, initialSettings);
            DiagnosticLog.WriteInfo("Main window view model created.");
            coordinator.StatusChanged += OnCoordinatorStatusChanged;
            coordinator.ActivityLogged += OnCoordinatorActivityLogged;
            coordinator.ProcessingCompleted += OnCoordinatorProcessingCompleted;
            _initialTrayHidePending = Program.LaunchedAtLogin &&
                                      initialSettings.StartMinimized &&
                                      !OperatingSystem.IsMacOS();

            desktop.MainWindow = new MainWindow
            {
                DataContext = _mainWindowViewModel,
            };
            DiagnosticLog.WriteInfo("Main window created.");
            _notificationManager = new WindowNotificationManager(desktop.MainWindow)
            {
                Position = NotificationPosition.TopRight,
                MaxItems = 3
            };

            _singleInstanceCts = new CancellationTokenSource();
            _ = SingleInstanceSignal.RunServerAsync(
                () =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        ShowMainWindow();
                    });
                    return Task.CompletedTask;
                },
                _singleInstanceCts.Token);

            desktop.Exit += (_, _) =>
            {
                _singleInstanceCts?.Cancel();
                _singleInstanceCts?.Dispose();
                _singleInstanceCts = null;
            };

            desktop.MainWindow.Opened += (_, _) =>
            {
                if (_initialTrayHidePending)
                {
                    _initialTrayHidePending = false;
                    Dispatcher.UIThread.Post(
                        HideMainWindow,
                        DispatcherPriority.Background);
                }

                _ = CheckForUpdatesOnStartupAsync();
            };

            if (initialSettings.WatchProfiles.Any(profile => profile.Enabled) &&
                validator.Validate(initialSettings).IsValid)
            {
                DiagnosticLog.WriteInfo("Queueing automatic monitoring start from startup settings.");
                Dispatcher.UIThread.Post(() =>
                {
                    _mainWindowViewModel?.StartMonitoringCommand.Execute(null);
                }, DispatcherPriority.Background);
            }

            UpdateTrayPresentation(_mainWindowViewModel.MonitoringStatus);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void RegisterGlobalExceptionLogging()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                DiagnosticLog.WriteException("Unhandled AppDomain exception.", exception);
            }
            else
            {
                DiagnosticLog.WriteInfo($"Unhandled AppDomain exception object: {args.ExceptionObject}");
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            DiagnosticLog.WriteException("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };

        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            DiagnosticLog.WriteException("Unhandled UI thread exception.", args.Exception);
        };
    }

    private static string ResolveAppVersion()
    {
        var assembly = typeof(App).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? "dev";
    }

    public bool ShouldCancelClose() => !_exitRequested;

    public void HideMainWindow()
    {
        if (_desktop?.MainWindow is null)
        {
            return;
        }

        _desktop.MainWindow.Hide();
        _desktop.MainWindow.ShowInTaskbar = false;
    }

    private void ShowMainWindow()
    {
        if (_desktop?.MainWindow is null)
        {
            return;
        }

        DiagnosticLog.WriteInfo("ShowMainWindow requested from tray or single-instance activation.");
        _desktop.MainWindow.ShowInTaskbar = true;
        _desktop.MainWindow.Show();
        if (_desktop.MainWindow.WindowState == WindowState.Minimized)
        {
            _desktop.MainWindow.WindowState = WindowState.Maximized;
        }
        _desktop.MainWindow.BringIntoView();
        _desktop.MainWindow.Activate();
    }

    private void OpenMenuItem_OnClick(object? sender, EventArgs e)
    {
        DiagnosticLog.WriteInfo("Tray menu Open clicked.");
        ShowMainWindow();
    }

    private void StartMonitoringMenuItem_OnClick(object? sender, EventArgs e)
    {
        _mainWindowViewModel?.StartMonitoringCommand.Execute(null);
    }

    private void StopMonitoringMenuItem_OnClick(object? sender, EventArgs e)
    {
        _mainWindowViewModel?.StopMonitoringCommand.Execute(null);
    }

    private void QuitMenuItem_OnClick(object? sender, EventArgs e)
    {
        _exitRequested = true;
        _desktop?.Shutdown();
    }

    private void TrayIcon_OnClicked(object? sender, EventArgs e)
    {
        DiagnosticLog.WriteInfo("Tray icon clicked.");
        ShowMainWindow();
    }

    private void OnCoordinatorStatusChanged(string status)
    {
        Dispatcher.UIThread.Post(() => UpdateTrayPresentation(status));
    }

    private void OnCoordinatorActivityLogged(ActivityLogEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var source = string.IsNullOrWhiteSpace(entry.Source) ? string.Empty : $"[{entry.Source}] ";
            var compactLine = $"{entry.TimeDisplay} {source}{entry.Message}";
            _recentTrayMessages.Enqueue(compactLine);
            while (_recentTrayMessages.Count > MaxTrayActivityLines)
            {
                _recentTrayMessages.Dequeue();
            }

            UpdateTrayPresentation(_mainWindowViewModel?.MonitoringStatus ?? "Stopped");
        });
    }

    private void OnCoordinatorProcessingCompleted(ProcessingResult result)
    {
        if (result.Success)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            var sourceName = Path.GetFileName(result.SourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var title = result.Message.Contains("upload", StringComparison.OrdinalIgnoreCase)
                ? "Upload Failed"
                : "Processing Failed";
            var body = string.IsNullOrWhiteSpace(sourceName)
                ? result.Message
                : $"{sourceName}: {result.Message}";

            _notificationManager?.Show(new Notification(
                title,
                body,
                NotificationType.Error,
                TimeSpan.FromSeconds(15)));
        });
    }

    private void UpdateTrayPresentation(string status)
    {
        if (_appTrayIcon is null)
        {
            return;
        }

        if (_trayStatusMenuItem is not null)
        {
            _trayStatusMenuItem.Header = $"Status: {status}";
        }

        if (_trayStartMonitoringMenuItem is not null)
        {
            _trayStartMonitoringMenuItem.IsEnabled = !string.Equals(status, "Running", StringComparison.OrdinalIgnoreCase);
        }

        if (_trayStopMonitoringMenuItem is not null)
        {
            _trayStopMonitoringMenuItem.IsEnabled = string.Equals(status, "Running", StringComparison.OrdinalIgnoreCase);
        }

        var messages = _recentTrayMessages.Reverse().ToArray();
        if (_trayRecentActivityMenuItem1 is not null)
        {
            _trayRecentActivityMenuItem1.Header = messages.ElementAtOrDefault(0) ?? "-";
        }

        if (_trayRecentActivityMenuItem2 is not null)
        {
            _trayRecentActivityMenuItem2.Header = messages.ElementAtOrDefault(1) ?? "-";
        }

        if (_trayRecentActivityMenuItem3 is not null)
        {
            _trayRecentActivityMenuItem3.Header = messages.ElementAtOrDefault(2) ?? "-";
        }

        var tooltipLines = new List<string> { "ProCut Suite Desktop", $"Status: {status}" };
        tooltipLines.AddRange(messages);
        _appTrayIcon.ToolTipText = string.Join(Environment.NewLine, tooltipLines);
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        if (_mainWindowViewModel is null)
        {
            return;
        }

        DiagnosticLog.WriteInfo("Startup update check started.");
        var result = await _mainWindowViewModel.CheckForUpdatesOnStartupAsync();
        DiagnosticLog.WriteInfo($"Startup update check finished. Success={result.Success}, UpdateAvailable={result.UpdateAvailable}");
        if (result.UpdateAvailable)
        {
            _notificationManager?.Show(new Notification(
                "Update Available",
                result.Message,
                NotificationType.Information,
                TimeSpan.FromSeconds(10)));
        }
    }

    private void InitializeTrayReferences()
    {
        var trayIcons = GetValue(TrayIcon.IconsProperty);
        _appTrayIcon = trayIcons?.FirstOrDefault();
        if (_appTrayIcon?.Menu is not NativeMenu menu)
        {
            return;
        }

        var items = menu.Items.OfType<NativeMenuItemBase>().ToList();
        _trayStartMonitoringMenuItem = items.ElementAtOrDefault(2) as NativeMenuItem;
        _trayStopMonitoringMenuItem = items.ElementAtOrDefault(3) as NativeMenuItem;
        _trayStatusMenuItem = items.ElementAtOrDefault(5) as NativeMenuItem;
        _trayRecentActivityMenuItem1 = items.ElementAtOrDefault(6) as NativeMenuItem;
        _trayRecentActivityMenuItem2 = items.ElementAtOrDefault(7) as NativeMenuItem;
        _trayRecentActivityMenuItem3 = items.ElementAtOrDefault(8) as NativeMenuItem;
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
