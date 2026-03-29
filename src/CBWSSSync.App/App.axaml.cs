using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Threading;
using Avalonia.Markup.Xaml;
using CBWSSSync.App.ViewModels;
using CBWSSSync.App.Views;
using CBWSSSync.Infrastructure.Configuration;
using CBWSSSync.Infrastructure.Monitoring;
using CBWSSSync.Infrastructure.Networking;
using CBWSSSync.Infrastructure.Processing;
using CBWSSSync.Core.Configuration;
using CBWSSSync.Core.Services;
using CBWSSSync.App.Services;

namespace CBWSSSync.App;

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
            var updateService = new VelopackUpdateService();
            var coordinator = new SyncCoordinator(folderMonitor, projectProcessor, ftpService, validator);
            var initialSettings = settingsStore.Load();
            _mainWindowViewModel = new MainWindowViewModel(settingsStore, validator, coordinator, ftpService, updateService, initialSettings);
            coordinator.StatusChanged += OnCoordinatorStatusChanged;
            coordinator.ActivityLogged += OnCoordinatorActivityLogged;
            // On macOS we currently prefer a visible first launch over risking an inaccessible
            // menu-bar-only startup if the tray icon fails to appear.
            _initialTrayHidePending = initialSettings.StartMinimized && !OperatingSystem.IsMacOS();

            desktop.MainWindow = new MainWindow
            {
                DataContext = _mainWindowViewModel,
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
            };

            if (initialSettings.WatchProfiles.Any(profile => profile.Enabled) &&
                validator.Validate(initialSettings).IsValid)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _mainWindowViewModel?.StartMonitoringCommand.Execute(null);
                }, DispatcherPriority.Background);
            }

            UpdateTrayPresentation(_mainWindowViewModel.MonitoringStatus);
        }

        base.OnFrameworkInitializationCompleted();
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

        _desktop.MainWindow.ShowInTaskbar = true;
        _desktop.MainWindow.Show();
        _desktop.MainWindow.WindowState = WindowState.Normal;
        _desktop.MainWindow.BringIntoView();
        _desktop.MainWindow.Activate();
    }

    private void OpenMenuItem_OnClick(object? sender, EventArgs e)
    {
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

        var tooltipLines = new List<string> { "CNC Sync", $"Status: {status}" };
        tooltipLines.AddRange(messages);
        _appTrayIcon.ToolTipText = string.Join(Environment.NewLine, tooltipLines);
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
