using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CNCSync.App.ViewModels;

namespace CNCSync.App.Views;

public sealed partial class DesktopPrintStationView : UserControl
{
    public DesktopPrintStationView()
    {
        InitializeComponent();
    }

    private async void ExportTrustCertificate_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DesktopPrintStationViewModel viewModel)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanSave: true } storage)
        {
            viewModel.EditorMessage = "The save dialog is unavailable.";
            return;
        }

        try
        {
            if (viewModel.ExportTrustCertificate() is not { Length: > 0 } certificate)
            {
                viewModel.EditorMessage = "Enable print sharing and select Save and Apply before exporting the certificate.";
                return;
            }

            viewModel.TrustFingerprint = $"SHA-256 fingerprint {Convert.ToHexString(SHA256.HashData(certificate))}";
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export print station trust certificate",
                SuggestedFileName = "ProCut-Suite-Print-Station.cer",
                DefaultExtension = "cer",
                ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType("Public certificate") { Patterns = ["*.cer"] }]
            });
            if (file is null)
            {
                return;
            }

            await using (var stream = await file.OpenWriteAsync())
            {
                if (stream.CanSeek)
                {
                    stream.SetLength(0);
                }

                await stream.WriteAsync(certificate);
            }

            viewModel.EditorMessage = "Trust certificate exported.";
        }
        catch (Exception)
        {
            viewModel.EditorMessage = "Could not export the trust certificate. Verify print sharing is enabled and try again.";
        }
    }

    private async void BrowseCertificate_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopPrintStationViewModel viewModel &&
            await PickFileAsync(viewModel, "Choose station certificate", "PEM certificate", ["*.pem", "*.crt", "*.cer"]) is { } path)
        {
            viewModel.CertificatePath = path;
        }
    }

    private async void BrowsePrivateKey_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopPrintStationViewModel viewModel &&
            await PickFileAsync(viewModel, "Choose station private key", "PEM private key", ["*.pem", "*.key"]) is { } path)
        {
            viewModel.PrivateKeyPath = path;
        }
    }

    private async void BrowsePdfRenderer_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopPrintStationViewModel viewModel &&
            await PickFileAsync(viewModel, "Choose the SumatraPDF executable", "Windows executable", ["*.exe"]) is { } path)
        {
            viewModel.WindowsPdfRendererPath = path;
        }
    }

    private async Task<string?> PickFileAsync(DesktopPrintStationViewModel viewModel, string title,
        string fileTypeName, IReadOnlyList<string> patterns)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage)
        {
            viewModel.EditorMessage = "The file picker is unavailable. Enter the file path directly.";
            return null;
        }

        try
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType(fileTypeName) { Patterns = patterns }]
            });
            return files.FirstOrDefault()?.TryGetLocalPath();
        }
        catch (Exception)
        {
            viewModel.EditorMessage = "Could not open the file picker. Enter the file path directly.";
            return null;
        }
    }
}
