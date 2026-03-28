using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CBWSSSync.App.ViewModels;
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
}
