namespace CNCSync.Core.Configuration;

public sealed class AppSettings
{
    public bool LaunchAtLogin { get; set; }
    public bool StartMinimized { get; set; } = true;
    public bool ScheduledCatchUpEnabled { get; set; }
    public int ScheduledCatchUpIntervalMinutes { get; set; } = 10;
    public List<DestinationSettings> Destinations { get; set; } = [];
    public List<ProcessingSetupSettings> ProcessingSetups { get; set; } = [];
    public List<WatchProfileSettings> WatchProfiles { get; set; } = [];

    public static AppSettings CreateDefault()
    {
        var ftpDestination = DestinationSettings.CreateDefault("Primary FTP");
        var processingSetup = ProcessingSetupSettings.CreateDefault("Default Processing");
        var watchProfile = WatchProfileSettings.CreateDefault("Primary Watch Folder", ftpDestination.Id, processingSetup.Id);

        return new AppSettings
        {
            LaunchAtLogin = false,
            StartMinimized = true,
            ScheduledCatchUpEnabled = false,
            ScheduledCatchUpIntervalMinutes = 10,
            Destinations = [ftpDestination],
            ProcessingSetups = [processingSetup],
            WatchProfiles = [watchProfile]
        };
    }

    public AppSettings Normalize()
    {
        Destinations ??= [];
        ProcessingSetups ??= [];
        WatchProfiles ??= [];

        if (ScheduledCatchUpIntervalMinutes <= 0)
        {
            ScheduledCatchUpIntervalMinutes = 10;
        }

        if (Destinations.Count == 0)
        {
            Destinations.Add(DestinationSettings.CreateDefault("Primary FTP"));
        }

        foreach (var destination in Destinations)
        {
            if (string.IsNullOrWhiteSpace(destination.Id))
            {
                destination.Id = Guid.NewGuid().ToString("N");
            }

            if ((destination.Type == DestinationType.Ftp || destination.Type == DestinationType.Sftp || destination.Type == DestinationType.Scp) && destination.Port <= 0)
            {
                destination.Port = destination.Type is DestinationType.Sftp or DestinationType.Scp ? 22 : 21;
            }

            destination.Host ??= string.Empty;
            destination.Username ??= string.Empty;
            destination.Password ??= string.Empty;
            destination.PrivateKeyPath ??= string.Empty;
            destination.PrivateKeyPassphrase ??= string.Empty;
            destination.LocalRootPath ??= string.Empty;
            destination.RemoteBasePath = NormalizeRemotePath(destination.RemoteBasePath);
            destination.NetworkHost = (destination.NetworkHost ?? string.Empty).Trim();
            destination.NetworkShareName = (destination.NetworkShareName ?? string.Empty).Trim().Trim('/').Trim('\\');
            destination.NetworkDomain = (destination.NetworkDomain ?? string.Empty).Trim();
            destination.RequiredVpnConnectionName = (destination.RequiredVpnConnectionName ?? string.Empty).Trim();
        }

        if (ProcessingSetups.Count == 0)
        {
            ProcessingSetups.Add(ProcessingSetupSettings.CreateDefault("Default Processing"));
        }

        foreach (var processingSetup in ProcessingSetups)
        {
            if (string.IsNullOrWhiteSpace(processingSetup.Id))
            {
                processingSetup.Id = Guid.NewGuid().ToString("N");
            }

            processingSetup.ArgumentsTemplate = string.IsNullOrWhiteSpace(processingSetup.ArgumentsTemplate)
                ? "\"{sourcePath}\" \"{outputPath}\""
                : processingSetup.ArgumentsTemplate;

            NormalizeBundledScriptArguments(processingSetup);
        }

        if (WatchProfiles.Count == 0)
        {
            WatchProfiles.Add(WatchProfileSettings.CreateDefault("Primary Watch Folder", Destinations[0].Id, ProcessingSetups[0].Id));
        }

        foreach (var profile in WatchProfiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Id))
            {
                profile.Id = Guid.NewGuid().ToString("N");
            }

            if (profile.StabilityDelaySeconds <= 0)
            {
                profile.StabilityDelaySeconds = 10;
            }

            if (profile.StabilityPollingSeconds <= 0)
            {
                profile.StabilityPollingSeconds = 5;
            }

            if (string.IsNullOrWhiteSpace(profile.DestinationId))
            {
                profile.DestinationId = Destinations[0].Id;
            }

            if (string.IsNullOrWhiteSpace(profile.ProcessingSetupId))
            {
                profile.ProcessingSetupId = ProcessingSetups[0].Id;
            }

            profile.RemoteSubfolder = NormalizeRemotePath(profile.RemoteSubfolder);
        }

        return this;
    }

    private static string NormalizeRemotePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.Trim().Replace('\\', '/').Trim('/');
        return string.IsNullOrWhiteSpace(trimmed) ? string.Empty : $"/{trimmed}";
    }

    private static void NormalizeBundledScriptArguments(ProcessingSetupSettings processingSetup)
    {
        if (string.IsNullOrWhiteSpace(processingSetup.ScriptPath) ||
            string.IsNullOrWhiteSpace(processingSetup.ArgumentsTemplate))
        {
            return;
        }

        var scriptName = Path.GetFileName(processingSetup.ScriptPath);
        if (!string.Equals(scriptName, "cbwss_mozaik_example.sh", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(scriptName, "cbwss_mozaik_example.ps1", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        processingSetup.ArgumentsTemplate = processingSetup.ArgumentsTemplate.Replace("-UpdateCycY", "--update-cyc-y");
    }
}
