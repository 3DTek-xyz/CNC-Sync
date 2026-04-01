using CNCSync.Core.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CNCSync.App.ViewModels;

public partial class WatchProfileItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private bool enabled = true;

    [ObservableProperty]
    private string watchFolder = string.Empty;

    [ObservableProperty]
    private string stagingFolder = string.Empty;

    [ObservableProperty]
    private string remoteSubfolder = string.Empty;

    [ObservableProperty]
    private string processingSetupId = string.Empty;

    [ObservableProperty]
    private string destinationId = string.Empty;

    [ObservableProperty]
    private int stabilityDelaySeconds = 10;

    [ObservableProperty]
    private int stabilityPollingSeconds = 5;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Unnamed watch profile" : Name;

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    public static WatchProfileItemViewModel FromSettings(WatchProfileSettings settings) =>
        new()
        {
            Id = settings.Id,
            Name = settings.Name,
            Enabled = settings.Enabled,
            WatchFolder = settings.WatchFolder,
            StagingFolder = settings.StagingFolder,
            RemoteSubfolder = settings.RemoteSubfolder,
            ProcessingSetupId = settings.ProcessingSetupId,
            DestinationId = settings.DestinationId,
            StabilityDelaySeconds = settings.StabilityDelaySeconds,
            StabilityPollingSeconds = settings.StabilityPollingSeconds
        };

    public WatchProfileSettings ToSettings() =>
        new()
        {
            Id = Id,
            Name = Name,
            Enabled = Enabled,
            WatchFolder = WatchFolder,
            StagingFolder = StagingFolder,
            RemoteSubfolder = RemoteSubfolder,
            ProcessingSetupId = ProcessingSetupId,
            DestinationId = DestinationId,
            StabilityDelaySeconds = StabilityDelaySeconds,
            StabilityPollingSeconds = StabilityPollingSeconds
        };
}
