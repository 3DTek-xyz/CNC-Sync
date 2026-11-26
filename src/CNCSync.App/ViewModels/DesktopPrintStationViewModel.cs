using Avalonia.Threading;
using CNCSync.Core.Configuration;
using CNCSync.Infrastructure.DesktopPrinting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CNCSync.App.ViewModels;

public sealed partial class DesktopPrintStationViewModel : ViewModelBase
{
    private readonly DesktopPrintStation _station;
    private int _developmentKeyPresenceVersion;

    [ObservableProperty]
    private bool enabled;

    [ObservableProperty]
    private string stationName = string.Empty;

    [ObservableProperty]
    private string origin = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsePublicCertificate))]
    [NotifyPropertyChangedFor(nameof(UseLocalCertificate))]
    [NotifyPropertyChangedFor(nameof(UseManualCertificate))]
    private PrintCertificateChoice? selectedCertificate;

    [ObservableProperty]
    private IReadOnlyList<DesktopPrintNetworkChoice> networkInterfaces = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ManagedOrigin))]
    private DesktopPrintNetworkChoice? selectedNetworkInterface;

    [ObservableProperty]
    private string certificatePath = string.Empty;

    [ObservableProperty]
    private string privateKeyPath = string.Empty;

    [ObservableProperty]
    private string browserOrigins = string.Empty;

    [ObservableProperty]
    private bool allowLocalDevelopmentOrigin;

    [ObservableProperty]
    private bool useLocalDevelopmentApi;

    [ObservableProperty]
    private string developmentApiKey = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DevelopmentApiKeyWatermark))]
    private bool hasSavedDevelopmentApiKey;

    [ObservableProperty]
    private string windowsPdfRendererPath = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<DesktopPrintWorkshop> workshops = [];

    [ObservableProperty]
    private DesktopPrintWorkshop? selectedWorkshop;

    [ObservableProperty]
    private IReadOnlyList<DesktopPrintQueueViewModel> printers = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPrinterDiscoveryMessage))]
    private string printerDiscoveryMessage = "Looking for installed printers…";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshPrintersCommand))]
    private bool isRefreshingPrinters;

    [ObservableProperty]
    private string status = string.Empty;

    [ObservableProperty]
    private string publicOrigin = string.Empty;

    [ObservableProperty]
    private string editorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaveError))]
    private string saveError = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrustFingerprint))]
    private string trustFingerprint = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshWorkshopsCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool isBusy;

    public DesktopPrintStationViewModel(DesktopPrintStation station)
    {
        _station = station;
        var settings = station.Settings;
        Enabled = settings.Enabled;
        StationName = string.IsNullOrWhiteSpace(settings.Name) ? DesktopPrintStation.DefaultName : settings.Name;
        SelectedCertificate = settings.UsePublicCertificate ? PublicCertificate :
            settings.UseManagedCertificate ? LocalCertificate : ManualCertificate;
        Origin = settings.Origin;
        LoadNetworkInterfaces(settings.NetworkInterfaceId);
        CertificatePath = settings.CertificatePath;
        PrivateKeyPath = settings.PrivateKeyPath;
        BrowserOrigins = string.Join(Environment.NewLine, settings.BrowserOrigins
            .Where(origin => origin != "http://localhost:5173"));
        AllowLocalDevelopmentOrigin = settings.AllowLocalDevelopmentOrigin;
        UseLocalDevelopmentApi = settings.UseLocalDevelopmentApi;
        WindowsPdfRendererPath = settings.WindowsPdfRendererPath;
        if (!string.IsNullOrWhiteSpace(settings.WorkshopId))
        {
            var savedWorkshop = new DesktopPrintWorkshop(settings.WorkshopId, $"Saved workshop ({settings.WorkshopId})");
            Workshops = [savedWorkshop];
            SelectedWorkshop = savedWorkshop;
        }

        Printers = CreatePrinterRows([], settings.Printers);
        Dispatcher.UIThread.Post(() => _ = RefreshPrintersAsync(CancellationToken.None));

        station.StatusChanged += _ => Dispatcher.UIThread.Post(() =>
        {
            RefreshStationStatus();
            RefreshNetworkInterfaces();
        });
        RefreshStationStatus();
    }

    private void RefreshStationStatus()
    {
        Status = _station.Status;
        PublicOrigin = _station.Settings.UsePublicCertificate ? _station.ActiveOrigin ?? string.Empty : string.Empty;
    }

    public bool IsWindows { get; } = OperatingSystem.IsWindows();
    private static readonly PrintCertificateChoice PublicCertificate = new("Automatic public certificate");
    private static readonly PrintCertificateChoice LocalCertificate = new("Local certificate with manual trust");
    private static readonly PrintCertificateChoice ManualCertificate = new("Use a custom HTTPS certificate");
    public IReadOnlyList<PrintCertificateChoice> CertificateChoices { get; } =
        [PublicCertificate, LocalCertificate, ManualCertificate];
    public bool UsePublicCertificate => SelectedCertificate == PublicCertificate;
    public bool UseLocalCertificate => SelectedCertificate == LocalCertificate;
    public bool UseManualCertificate => SelectedCertificate == ManualCertificate;
    public bool HasPrinterDiscoveryMessage => PrinterDiscoveryMessage.Length > 0;
    public bool HasSaveError => SaveError.Length > 0;
    public bool HasTrustFingerprint => TrustFingerprint.Length > 0;
    public string DevelopmentApiKeyWatermark => HasSavedDevelopmentApiKey ? "********" : "Enter a development API key";
    public string ManagedOrigin => SelectedNetworkInterface?.Address is { Length: > 0 } address
        ? $"https://{address}:{DesktopPrintStation.ManagedHttpsPort}"
        : string.Empty;

    public byte[]? ExportTrustCertificate() => _station.ExportTrustCertificate();

    partial void OnUseLocalDevelopmentApiChanged(bool value)
    {
        Workshops = [];
        SelectedWorkshop = null;
        DevelopmentApiKey = string.Empty;
        _ = RefreshDevelopmentKeyPresenceAsync();
    }

    private async Task RefreshDevelopmentKeyPresenceAsync()
    {
        var version = ++_developmentKeyPresenceVersion;
        if (!UseLocalDevelopmentApi)
        {
            HasSavedDevelopmentApiKey = false;
            return;
        }

        bool hasSavedKey;
        try
        {
            hasSavedKey = await _station.HasSavedDevelopmentKeyAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException)
        {
            hasSavedKey = false;
        }

        if (version == _developmentKeyPresenceVersion)
        {
            HasSavedDevelopmentApiKey = hasSavedKey;
        }
    }

    private bool CanRunServiceCommand() => !IsBusy;

    private bool CanRefreshPrinters() => !IsRefreshingPrinters;

    [RelayCommand(CanExecute = nameof(CanRefreshPrinters))]
    private async Task RefreshPrintersAsync(CancellationToken cancellationToken)
    {
        if (!CanRefreshPrinters())
        {
            return;
        }

        IsRefreshingPrinters = true;
        PrinterDiscoveryMessage = "Looking for installed printers…";
        try
        {
            var installed = await InstalledPrintQueues.GetInstalledAsync(cancellationToken);
            var draft = Printers.Select(printer => printer.ToSelection()).ToArray();
            Printers = CreatePrinterRows(installed, _station.Settings.Printers, draft);
            PrinterDiscoveryMessage = installed.Count == 0
                ? "No installed printers found. Add a printer in your system settings, then refresh."
                : string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            PrinterDiscoveryMessage = string.Empty;
        }
        catch (Exception)
        {
            PrinterDiscoveryMessage = "Could not list installed printers. Check your system printer settings, then refresh.";
        }
        finally
        {
            IsRefreshingPrinters = false;
        }
    }

    private DesktopPrintQueueViewModel[] CreatePrinterRows(IReadOnlyList<InstalledPrintQueue> installed,
        IReadOnlyList<DesktopPrintQueueMapping> saved, IReadOnlyList<PrintQueueSelection>? draft = null) =>
        PrintQueueSelections.Merge(_station.StationId, installed, saved, draft, ignoreQueueCase: IsWindows)
            .OrderBy(queue => queue.InstalledName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(queue => queue.Queue, StringComparer.Ordinal)
            .GroupBy(queue => queue.InstalledName, StringComparer.CurrentCultureIgnoreCase)
            .SelectMany(group =>
            {
                var hasDuplicateName = group.Skip(1).Any();
                return group.Select((queue, index) => new DesktopPrintQueueViewModel(queue,
                    hasDuplicateName ? $"{queue.InstalledName} ({index + 1})" : queue.InstalledName));
            })
            .ToArray();

    [RelayCommand]
    private void RefreshNetworkInterfaces() => LoadNetworkInterfaces(SelectedNetworkInterface?.Id);

    private void LoadNetworkInterfaces(string? selectedId)
    {
        var choices = DesktopPrintStation.GetNetworkInterfaces()
            .Select(adapter => new DesktopPrintNetworkChoice(adapter.Id, $"{adapter.Name} ({adapter.Address})", adapter.Address))
            .ToList();
        var selected = choices.FirstOrDefault(adapter => string.Equals(adapter.Id, selectedId, StringComparison.Ordinal));
        if (selected is null && !string.IsNullOrWhiteSpace(selectedId))
        {
            selected = new DesktopPrintNetworkChoice(selectedId, "Saved adapter (disconnected)", string.Empty);
            choices.Add(selected);
        }

        if (!NetworkInterfaces.SequenceEqual(choices))
        {
            NetworkInterfaces = choices;
        }

        SelectedNetworkInterface = selected ?? (choices.Count == 1 ? choices[0] : null);
    }

    [RelayCommand(CanExecute = nameof(CanRunServiceCommand))]
    private async Task RefreshWorkshopsAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        EditorMessage = "Loading workshops…";
        try
        {
            var selectedId = SelectedWorkshop?.Id;
            var available = await _station.GetWorkshopsAsync(cancellationToken,
                UseLocalDevelopmentApi ? DevelopmentApiKey : null);
            DevelopmentApiKey = string.Empty;
            _ = RefreshDevelopmentKeyPresenceAsync();
            Workshops = available;
            SelectedWorkshop = available.FirstOrDefault(workshop =>
                string.Equals(workshop.Id, selectedId, StringComparison.Ordinal));
            EditorMessage = available.Count == 0 ? "No workshops are available for this API key." : string.Empty;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            EditorMessage = ex.Message;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            EditorMessage = string.Empty;
        }
        catch (Exception)
        {
            EditorMessage = "Could not load workshops. Check the ProCut Suite connection in App Settings.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunServiceCommand))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        SaveError = string.Empty;
        EditorMessage = "Saving print sharing settings…";
        try
        {
            var settings = _station.Settings with
            {
                Enabled = Enabled,
                WorkshopId = SelectedWorkshop?.Id ?? string.Empty,
                Name = StationName.Trim(),
                UsePublicCertificate = !UseLocalCertificate && !UseManualCertificate,
                UseManagedCertificate = UseLocalCertificate,
                Origin = Origin.Trim(),
                NetworkInterfaceId = SelectedNetworkInterface?.Id ?? string.Empty,
                CertificatePath = CertificatePath.Trim(),
                PrivateKeyPath = PrivateKeyPath.Trim(),
                BrowserOrigins = BrowserOrigins.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                AllowLocalDevelopmentOrigin = AllowLocalDevelopmentOrigin,
                UseLocalDevelopmentApi = UseLocalDevelopmentApi,
                WindowsPdfRendererPath = WindowsPdfRendererPath.Trim(),
                Printers = PrintQueueSelections.ToMappings(Printers.Select(printer => printer.ToSelection()))
            };

            var keyWarning = await _station.SaveAsync(settings, cancellationToken, UseLocalDevelopmentApi ? DevelopmentApiKey : null);
            StationName = _station.Settings.Name;
            if (keyWarning is null)
            {
                DevelopmentApiKey = string.Empty;
            }
            _ = RefreshDevelopmentKeyPresenceAsync();

            RefreshStationStatus();
            EditorMessage = "Settings saved on this computer.";
            SaveError = keyWarning ?? string.Empty;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            EditorMessage = string.Empty;
            SaveError = $"Print sharing settings weren’t saved. {ex.Message}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            EditorMessage = string.Empty;
        }
        catch (Exception)
        {
            EditorMessage = string.Empty;
            SaveError = "Print sharing settings weren’t saved. Check the station settings and try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public sealed record DesktopPrintNetworkChoice(string Id, string Label, string Address);
public sealed record PrintCertificateChoice(string Label);

public sealed partial class DesktopPrintQueueViewModel(PrintQueueSelection queue, string label) : ViewModelBase
{
    public string Label { get; } = label;
    public bool IsAvailable => queue.IsAvailable;
    public bool CanChangeSharing => IsAvailable || IsShared;
    public int MaxDisplayNameLength => PrintQueueSelections.MaxDisplayNameLength;
    public string ConnectionLabel
    {
        get
        {
            if (!IsAvailable)
            {
                return "Unavailable on this computer";
            }

            var connection = queue.Connection switch
            {
                PrintQueueConnection.Usb => "USB printer",
                PrintQueueConnection.Network => "Network printer",
                PrintQueueConnection.Bluetooth => "Bluetooth printer",
                PrintQueueConnection.Virtual => "Virtual printer",
                _ => "Connection unknown"
            };
            var location = queue.Location?.Trim();
            return string.IsNullOrEmpty(location) ? connection : $"{connection} · {location}";
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeSharing))]
    private bool isShared = queue.IsShared;

    [ObservableProperty]
    private string displayName = queue.DisplayName;

    public PrintQueueSelection ToSelection() => queue with { IsShared = IsShared, DisplayName = DisplayName };
}
