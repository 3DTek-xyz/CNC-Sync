namespace CNCSync.Core.Configuration;

public sealed class WatchProfileSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string WatchFolder { get; set; } = string.Empty;
    public string StagingFolder { get; set; } = string.Empty;
    public string RemoteSubfolder { get; set; } = string.Empty;
    public string ProcessingSetupId { get; set; } = string.Empty;
    public string DestinationId { get; set; } = string.Empty;
    public int StabilityDelaySeconds { get; set; } = 10;
    public int StabilityPollingSeconds { get; set; } = 5;

    public static WatchProfileSettings CreateDefault(string name, string destinationId, string processingSetupId = "") =>
        new()
        {
            Name = name,
            Enabled = true,
            WatchFolder = string.Empty,
            StagingFolder = string.Empty,
            RemoteSubfolder = string.Empty,
            ProcessingSetupId = processingSetupId,
            DestinationId = destinationId,
            StabilityDelaySeconds = 10,
            StabilityPollingSeconds = 5
        };
}
