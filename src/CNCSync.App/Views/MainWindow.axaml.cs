using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CNCSync.App.ViewModels;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CNCSync.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (Avalonia.Application.Current is CNCSync.App.App app && app.ShouldCancelClose())
        {
            e.Cancel = true;
            app.HideMainWindow();
            return;
        }

        base.OnClosing(e);
    }

    private async void BrowseWatchFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        var selectedPath = await PickFolderAsync();
        if (selectedPath is not null &&
            DataContext is MainWindowViewModel viewModel &&
            viewModel.SelectedWatchProfile is not null)
        {
            viewModel.SelectedWatchProfile.WatchFolder = selectedPath;
        }
    }

    private async void BrowseStagingFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        var selectedPath = await PickFolderAsync();
        if (selectedPath is not null &&
            DataContext is MainWindowViewModel viewModel &&
            viewModel.SelectedWatchProfile is not null)
        {
            viewModel.SelectedWatchProfile.StagingFolder = selectedPath;
        }
    }

    private async void BrowseDestinationFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            viewModel.SelectedDestination is null)
        {
            return;
        }

        var initialDirectory = ResolveInitialDestinationDirectory(viewModel);
        var selectedPath = await PickFolderAsync(initialDirectory);
        if (selectedPath is not null)
        {
            viewModel.SelectedDestination.LocalRootPath = selectedPath;
        }
    }

    private async void BrowsePrivateKey_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            viewModel.SelectedDestination is null)
        {
            return;
        }

        var initialDirectory = ResolveInitialPrivateKeyDirectory(viewModel);
        var selectedPath = await PickFileAsync(initialDirectory);
        if (selectedPath is not null)
        {
            viewModel.SelectedDestination.PrivateKeyPath = selectedPath;
        }
    }

    private async void BrowseProcessingScript_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            viewModel.SelectedProcessingSetup is null)
        {
            return;
        }

        var initialDirectory = ResolveInitialScriptDirectory(viewModel);
        var selectedPath = await PickFileAsync(initialDirectory);
        if (selectedPath is not null)
        {
            viewModel.SelectedProcessingSetup.ScriptPath = selectedPath;
        }
    }

    private async void ImportSettings_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var initialDirectory = Path.GetDirectoryName(viewModel.SettingsPath);
        var selectedPath = await PickSettingsFileAsync(initialDirectory);
        if (selectedPath is null)
        {
            return;
        }

        try
        {
            await viewModel.ImportSettingsFromFileAsync(selectedPath);
        }
        catch (Exception ex)
        {
            viewModel.CurrentTask = $"Settings import failed: {ex.Message}";
        }
    }

    private void OpenExternalLink_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url } && !string.IsNullOrWhiteSpace(url))
        {
            OpenUrlInShell(url);
        }
    }

    private void OpenScriptsFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            string.IsNullOrWhiteSpace(viewModel.ScriptsPath) ||
            !Directory.Exists(viewModel.ScriptsPath))
        {
            return;
        }

        OpenFolderInShell(viewModel.ScriptsPath);
    }

    private void RemoteBrowserList_OnDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            viewModel.SelectedRemoteBrowserItem is null ||
            !viewModel.SelectedRemoteBrowserItem.IsDirectory)
        {
            return;
        }

        if (viewModel.OpenSelectedRemoteFolderCommand.CanExecute(null))
        {
            viewModel.OpenSelectedRemoteFolderCommand.Execute(null);
        }
    }

    private async Task<string?> PickFolderAsync(string? initialDirectory = null)
    {
        IStorageFolder? suggestedStartLocation = null;
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            suggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(ToFileUri(initialDirectory));
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                Title = "Choose folder",
                SuggestedStartLocation = suggestedStartLocation
            });

        return folders.FirstOrDefault()?.Path.LocalPath;
    }

    private async Task<string?> PickFileAsync(string? initialDirectory = null)
    {
        IStorageFolder? suggestedStartLocation = null;
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            suggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(ToFileUri(initialDirectory));
        }

        var files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                AllowMultiple = false,
                Title = "Choose script",
                SuggestedStartLocation = suggestedStartLocation
            });

        return files.FirstOrDefault()?.Path.LocalPath;
    }

    private async Task<string?> PickSettingsFileAsync(string? initialDirectory = null)
    {
        IStorageFolder? suggestedStartLocation = null;
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            suggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(ToFileUri(initialDirectory));
        }

        var files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                AllowMultiple = false,
                Title = "Import settings file",
                SuggestedStartLocation = suggestedStartLocation,
                FileTypeFilter =
                [
                    new FilePickerFileType("JSON files")
                    {
                        Patterns = ["*.json"]
                    }
                ]
            });

        return files.FirstOrDefault()?.Path.LocalPath;
    }

    private static string? ResolveInitialScriptDirectory(MainWindowViewModel viewModel)
    {
        var currentScriptPath = viewModel.SelectedProcessingSetup?.ScriptPath;
        if (!string.IsNullOrWhiteSpace(currentScriptPath) && File.Exists(currentScriptPath))
        {
            return Path.GetDirectoryName(currentScriptPath);
        }

        return viewModel.ScriptsPath;
    }

    private static string? ResolveInitialDestinationDirectory(MainWindowViewModel viewModel)
    {
        var currentDestinationPath = viewModel.SelectedDestination?.LocalRootPath;
        if (!string.IsNullOrWhiteSpace(currentDestinationPath) && Directory.Exists(currentDestinationPath))
        {
            return currentDestinationPath;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static string? ResolveInitialPrivateKeyDirectory(MainWindowViewModel viewModel)
    {
        var currentKeyPath = viewModel.SelectedDestination?.PrivateKeyPath;
        if (!string.IsNullOrWhiteSpace(currentKeyPath))
        {
            var expandedPath = ExpandHomeDirectory(currentKeyPath);
            if (File.Exists(expandedPath))
            {
                return Path.GetDirectoryName(expandedPath);
            }
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
    }

    private static Uri ToFileUri(string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        return new Uri(normalizedPath, UriKind.Absolute);
    }

    private static string ExpandHomeDirectory(string path)
    {
        if (!path.StartsWith("~/", StringComparison.Ordinal) &&
            !path.StartsWith("~\\", StringComparison.Ordinal))
        {
            return path;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, path[2..]);
    }

    private static void OpenFolderInShell(string path)
    {
        if (OperatingSystem.IsMacOS())
        {
            var startInfo = new ProcessStartInfo("open")
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(path);
            Process.Start(startInfo);
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            var startInfo = new ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(path);
            Process.Start(startInfo);
            return;
        }

        var linuxStartInfo = new ProcessStartInfo("xdg-open")
        {
            UseShellExecute = false
        };
        linuxStartInfo.ArgumentList.Add(path);
        Process.Start(linuxStartInfo);
    }

    private static void OpenUrlInShell(string url)
    {
        if (OperatingSystem.IsMacOS())
        {
            var startInfo = new ProcessStartInfo("open")
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(url);
            Process.Start(startInfo);
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            var startInfo = new ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(url);
            Process.Start(startInfo);
            return;
        }

        var linuxStartInfo = new ProcessStartInfo("xdg-open")
        {
            UseShellExecute = false
        };
        linuxStartInfo.ArgumentList.Add(url);
        Process.Start(linuxStartInfo);
    }
}
