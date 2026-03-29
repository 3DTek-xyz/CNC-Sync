using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CBWSSSync.App.ViewModels;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CBWSSSync.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (Avalonia.Application.Current is CBWSSSync.App.App app && app.ShouldCancelClose())
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

    private async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                Title = "Choose folder"
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

    private static string? ResolveInitialScriptDirectory(MainWindowViewModel viewModel)
    {
        var currentScriptPath = viewModel.SelectedProcessingSetup?.ScriptPath;
        if (!string.IsNullOrWhiteSpace(currentScriptPath) && File.Exists(currentScriptPath))
        {
            return Path.GetDirectoryName(currentScriptPath);
        }

        return viewModel.ScriptsPath;
    }

    private static Uri ToFileUri(string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        return new Uri(normalizedPath, UriKind.Absolute);
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
}
