namespace CNCSync.Core.Configuration;

public sealed class AppSettings
{
    public bool LaunchAtLogin { get; set; }
    public bool StartMinimized { get; set; } = true;
    public AppThemePreference ThemePreference { get; set; } = AppThemePreference.Light;
    public string TelemetryInstallId { get; set; } = string.Empty;
    public DateTime? TelemetryInstallReportedAtUtc { get; set; }
    public string TelemetryLastSeenVersion { get; set; } = string.Empty;
    public DateTime? TelemetryLastSeenAtUtc { get; set; }
    public DateTime? TelemetryLastHeartbeatAtUtc { get; set; }
    public bool ScheduledCatchUpEnabled { get; set; }
    public int ScheduledCatchUpIntervalMinutes { get; set; } = 10;
    public string CustomScriptSourceUrl { get; set; } = string.Empty;
    public ProCutApiSettings ProCutApi { get; set; } = new();
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
            ThemePreference = AppThemePreference.Light,
            TelemetryInstallId = Guid.NewGuid().ToString("N"),
            ScheduledCatchUpEnabled = false,
            ScheduledCatchUpIntervalMinutes = 10,
            CustomScriptSourceUrl = string.Empty,
            ProCutApi = new ProCutApiSettings(),
            Destinations = [ftpDestination],
            ProcessingSetups = [processingSetup],
            WatchProfiles = [watchProfile]
        };
    }

    public static AppSettings CreateProCutApiImportTemplate()
    {
        const string destinationId = "procut-suite-api-output-folder";
        const string processingSetupId = "procut-suite-api-gcode-processing";

        return new AppSettings
        {
            LaunchAtLogin = false,
            StartMinimized = true,
            ThemePreference = AppThemePreference.Light,
            TelemetryInstallId = string.Empty,
            ScheduledCatchUpEnabled = false,
            ScheduledCatchUpIntervalMinutes = 10,
            CustomScriptSourceUrl = string.Empty,
            ProCutApi = new ProCutApiSettings
            {
                BaseUrl = "https://procutsuite.com",
                ApiKey = string.Empty
            },
            Destinations =
            [
                new DestinationSettings
                {
                    Id = destinationId,
                    Name = "ProCut Suite API Output Folder",
                    Type = DestinationType.LocalFolder,
                    Host = string.Empty,
                    Port = 21,
                    LocalRootPath = "/CHANGE-ME/ProCutSuite/Output",
                    AutoUpload = true,
                    ReplaceRemoteFolderOnUpload = false
                }
            ],
            ProcessingSetups =
            [
                new ProcessingSetupSettings
                {
                    Id = processingSetupId,
                    Name = "ProCut Suite API G-code Processing",
                    Mode = ProcessingMode.ProCutApi,
                    ScriptPath = string.Empty,
                    RunnerMode = ScriptRunnerMode.Auto,
                    ArgumentsTemplate = "\"{sourcePath}\" \"{outputPath}\"",
                    ProCutServiceId = "gcode_processing",
                    ProCutApiEndpoint = "/api/external/gcode/process",
                    ProCutArcFittingEnabled = false,
                    ProCutLineJoinerEnabled = true,
                    ProCutArcJoinerEnabled = false,
                    ProCutCornerSmoothEnabled = true
                }
            ],
            WatchProfiles =
            [
                new WatchProfileSettings
                {
                    Id = "procut-suite-api-watch-folder",
                    Name = "ProCut Suite API Input",
                    Enabled = true,
                    WatchFolder = "/CHANGE-ME/ProCutSuite/Input",
                    StagingFolder = "/CHANGE-ME/ProCutSuite/Staging",
                    RemoteSubfolder = string.Empty,
                    WorkItemMode = WatchProfileWorkItemMode.ChangedFilesAndFolders,
                    ProcessingSetupId = processingSetupId,
                    DestinationId = destinationId,
                    StabilityDelaySeconds = 10,
                    StabilityPollingSeconds = 5
                }
            ]
        };
    }

    public static AppSettings PrepareImported(AppSettings? imported, string existingProCutApiKey)
    {
        var normalized = (imported ?? CreateDefault()).Normalize();
        if (string.IsNullOrWhiteSpace(normalized.ProCutApi.ApiKey) &&
            !string.IsNullOrWhiteSpace(existingProCutApiKey))
        {
            normalized.ProCutApi.ApiKey = existingProCutApiKey.Trim();
        }

        return normalized;
    }

    public AppSettings Normalize()
    {
        Destinations ??= [];
        ProcessingSetups ??= [];
        WatchProfiles ??= [];
        ProCutApi ??= new ProCutApiSettings();

        if (!Enum.IsDefined(ThemePreference))
        {
            ThemePreference = AppThemePreference.Light;
        }

        if (string.IsNullOrWhiteSpace(TelemetryInstallId))
        {
            TelemetryInstallId = Guid.NewGuid().ToString("N");
        }

        TelemetryLastSeenVersion = (TelemetryLastSeenVersion ?? string.Empty).Trim();

        if (ScheduledCatchUpIntervalMinutes <= 0)
        {
            ScheduledCatchUpIntervalMinutes = 10;
        }

        CustomScriptSourceUrl = (CustomScriptSourceUrl ?? string.Empty).Trim();
        ProCutApi.BaseUrl = string.IsNullOrWhiteSpace(ProCutApi.BaseUrl)
            ? "https://procutsuite.com"
            : ProCutApi.BaseUrl.Trim().TrimEnd('/');
        ProCutApi.ApiKey = (ProCutApi.ApiKey ?? string.Empty).Trim();

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

            if (!Enum.IsDefined(destination.FtpDataMode))
            {
                destination.FtpDataMode = FtpDataMode.AutoPassive;
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
            processingSetup.ProCutServiceId = string.IsNullOrWhiteSpace(processingSetup.ProCutServiceId)
                ? "gcode_processing"
                : processingSetup.ProCutServiceId.Trim();
            processingSetup.ProCutApiEndpoint = string.IsNullOrWhiteSpace(processingSetup.ProCutApiEndpoint)
                ? "/api/external/gcode/process"
                : processingSetup.ProCutApiEndpoint.Trim();
            processingSetup.ProCutArcFittingToleranceMm = processingSetup.ProCutArcFittingToleranceMm <= 0 ? 0.05 : processingSetup.ProCutArcFittingToleranceMm;
            processingSetup.ProCutArcFittingMinSegments = Math.Max(0, processingSetup.ProCutArcFittingMinSegments);
            processingSetup.ProCutArcFittingMaxSegments = Math.Max(0, processingSetup.ProCutArcFittingMaxSegments);
            processingSetup.ProCutArcJoinerMaxCombinedAngleDeg = processingSetup.ProCutArcJoinerMaxCombinedAngleDeg <= 0 ? 180 : processingSetup.ProCutArcJoinerMaxCombinedAngleDeg;
            processingSetup.ProCutCornerSmoothAngleThresholdDeg = processingSetup.ProCutCornerSmoothAngleThresholdDeg <= 0 ? 45 : processingSetup.ProCutCornerSmoothAngleThresholdDeg;
            processingSetup.ProCutCornerSmoothSlowdownDistanceMm = processingSetup.ProCutCornerSmoothSlowdownDistanceMm <= 0 ? 5 : processingSetup.ProCutCornerSmoothSlowdownDistanceMm;
            processingSetup.ProCutCornerSmoothSlowdownFeedrateMmMin = processingSetup.ProCutCornerSmoothSlowdownFeedrateMmMin <= 0 ? 1250 : processingSetup.ProCutCornerSmoothSlowdownFeedrateMmMin;
            processingSetup.ProCutCornerSmoothSmallArcThresholdMm = processingSetup.ProCutCornerSmoothSmallArcThresholdMm < 0 ? 10 : processingSetup.ProCutCornerSmoothSmallArcThresholdMm;

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

            if (!Enum.IsDefined(profile.WorkItemMode))
            {
                profile.WorkItemMode = WatchProfileWorkItemMode.ChangedFilesAndFolders;
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
        if (!string.Equals(scriptName, "mozaik_job_prep_example.sh", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(scriptName, "mozaik_job_prep_example.ps1", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(scriptName, "legacy_revision.sh", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(scriptName, "legacy_revision.ps1", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        processingSetup.ArgumentsTemplate = processingSetup.ArgumentsTemplate.Replace("-UpdateCycY", "--update-cyc-y");
    }
}
