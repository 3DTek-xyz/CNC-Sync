namespace CNCSync.Core.Configuration;

public sealed record DesktopPrintQueueMapping(string Id, string Name, string Queue)
{
    // A missing value preserves a legacy mapping's custom name. Empty selects the installed name.
    public string? DisplayName { get; init; }
}

public sealed record DesktopPrintStationSettings
{
    public bool Enabled { get; init; }
    public bool UsePublicCertificate { get; init; }
    public bool UseManagedCertificate { get; init; } = true;
    public string WorkshopId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Origin { get; init; } = string.Empty;
    public string NetworkInterfaceId { get; init; } = string.Empty;
    public string WindowsPdfRendererPath { get; init; } = string.Empty;
    public bool UseLocalDevelopmentApi { get; init; }
    public bool AllowLocalDevelopmentOrigin { get; init; }
    public string CertificatePath { get; init; } = string.Empty;
    public string PrivateKeyPath { get; init; } = string.Empty;
    public string[] BrowserOrigins { get; init; } = ["https://procutsuite.com"];
    public DesktopPrintQueueMapping[] Printers { get; init; } = [];
}
