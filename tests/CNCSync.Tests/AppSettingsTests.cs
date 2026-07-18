using CNCSync.Core.Configuration;

namespace CNCSync.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_AreSuitableForFirstRun()
    {
        var settings = AppSettings.CreateDefault();
        var ftp = Assert.Single(settings.Destinations);
        var profile = Assert.Single(settings.WatchProfiles);

        Assert.Equal(21, ftp.Port);
        Assert.True(ftp.UseAnonymousFtp);
        Assert.Equal(FtpDataMode.AutoPassive, ftp.FtpDataMode);
        Assert.True(settings.StartMinimized);
        Assert.Equal(AppThemePreference.Light, settings.ThemePreference);
        Assert.False(settings.ScheduledCatchUpEnabled);
        Assert.Equal(10, settings.ScheduledCatchUpIntervalMinutes);
        Assert.Equal("https://procutsuite.com", settings.ProCutApi.BaseUrl);
        Assert.Equal(string.Empty, settings.ProCutApi.ApiKey);
        Assert.Equal("gcode_processing", settings.ProcessingSetups[0].ProCutServiceId);
        Assert.Equal("/api/external/gcode/process", settings.ProcessingSetups[0].ProCutApiEndpoint);
        Assert.True(settings.ProcessingSetups[0].ProCutCornerSmoothEnabled);
        Assert.Equal(10, profile.StabilityDelaySeconds);
        Assert.Equal(5, profile.StabilityPollingSeconds);
        Assert.Equal(WatchProfileWorkItemMode.ChangedFilesAndFolders, profile.WorkItemMode);
        Assert.Equal(ftp.Id, profile.DestinationId);
    }

    [Fact]
    public void Normalize_FillsInMissingProCutApiSettingsForOlderSettings()
    {
        var settings = new AppSettings
        {
            ProCutApi = null!,
            Destinations = [],
            WatchProfiles = []
        };

        settings.Normalize();

        Assert.NotNull(settings.ProCutApi);
        Assert.Equal("https://procutsuite.com", settings.ProCutApi.BaseUrl);
        Assert.Equal(string.Empty, settings.ProCutApi.ApiKey);
    }

    [Fact]
    public void ProcessingModes_IncludeProCutApi()
    {
        Assert.Contains(ProcessingMode.ProCutApi, Enum.GetValues<ProcessingMode>());
    }

    [Fact]
    public void DesktopAppLinks_SeparateDesktopProjectDocsFromProCutSuiteDashboard()
    {
        Assert.Equal("https://3dtek-xyz.github.io/CNC-Sync/", DesktopAppLinks.ProjectSiteUrl);
        Assert.Equal("https://procutsuite.com", DesktopAppLinks.ProCutSuiteDashboardUrl);
        Assert.NotEqual(DesktopAppLinks.ProjectSiteUrl, DesktopAppLinks.ProCutSuiteDashboardUrl);
    }

    [Fact]
    public void ProCutApiImportTemplate_ConfiguresLinkedWatchProcessingAndDestination()
    {
        var settings = AppSettings.CreateProCutApiImportTemplate();
        var destination = Assert.Single(settings.Destinations);
        var processingSetup = Assert.Single(settings.ProcessingSetups);
        var profile = Assert.Single(settings.WatchProfiles);

        Assert.Equal("https://procutsuite.com", settings.ProCutApi.BaseUrl);
        Assert.Equal(string.Empty, settings.ProCutApi.ApiKey);
        Assert.Equal(string.Empty, settings.TelemetryInstallId);
        Assert.Equal(DestinationType.LocalFolder, destination.Type);
        Assert.Equal("ProCut Suite API Output Folder", destination.Name);
        Assert.Equal(ProcessingMode.ProCutApi, processingSetup.Mode);
        Assert.Equal("gcode_processing", processingSetup.ProCutServiceId);
        Assert.Equal("/api/external/gcode/process", processingSetup.ProCutApiEndpoint);
        Assert.True(processingSetup.ProCutCornerSmoothEnabled);
        Assert.True(processingSetup.ProCutLineJoinerEnabled);
        Assert.False(processingSetup.ProCutArcFittingEnabled);
        Assert.False(processingSetup.ProCutArcJoinerEnabled);
        Assert.Equal(destination.Id, profile.DestinationId);
        Assert.Equal(processingSetup.Id, profile.ProcessingSetupId);
        Assert.Contains("CHANGE-ME", profile.WatchFolder, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CHANGE-ME", profile.StagingFolder, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CHANGE-ME", destination.LocalRootPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrepareImportedSettings_PreservesExistingProCutApiKeyWhenImportDoesNotContainOne()
    {
        var imported = AppSettings.CreateProCutApiImportTemplate();

        var prepared = AppSettings.PrepareImported(imported, TestSecrets.ProCutApiSecret);

        Assert.Equal(TestSecrets.ProCutApiSecret, prepared.ProCutApi.ApiKey);
    }

    [Fact]
    public void Validator_RejectsProCutApiProcessingWithoutApiKey()
    {
        var validator = new AppSettingsValidator();
        var settings = AppSettings.CreateDefault();
        settings.ProcessingSetups[0].Mode = ProcessingMode.ProCutApi;

        var result = validator.Validate(settings);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("ProCut Suite API key", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validator_RejectsProCutApiProcessingWithoutEnabledTools()
    {
        var validator = new AppSettingsValidator();
        var settings = AppSettings.CreateDefault();
        settings.ProCutApi.ApiKey = TestSecrets.ProCutApiKey;
        settings.ProcessingSetups[0].Mode = ProcessingMode.ProCutApi;
        settings.ProcessingSetups[0].ProCutArcFittingEnabled = false;
        settings.ProcessingSetups[0].ProCutLineJoinerEnabled = false;
        settings.ProcessingSetups[0].ProCutArcJoinerEnabled = false;
        settings.ProcessingSetups[0].ProCutCornerSmoothEnabled = false;

        var result = validator.Validate(settings);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("at least one G-code tool", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validator_RejectsMissingWatchFolder()
    {
        var validator = new AppSettingsValidator();
        var settings = AppSettings.CreateDefault();
        settings.WatchProfiles[0].StagingFolder = "/tmp/staging";

        var result = validator.Validate(settings);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("watch folder", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validator_AcceptsBasicValidSettings()
    {
        var validator = new AppSettingsValidator();
        var watchFolder = Path.Combine(Path.GetTempPath(), $"cncsync-watch-{Guid.NewGuid():N}");
        var destinationFolder = Path.Combine(Path.GetTempPath(), $"cncsync-destination-{Guid.NewGuid():N}");
        Directory.CreateDirectory(watchFolder);
        Directory.CreateDirectory(destinationFolder);

        try
        {
            var settings = AppSettings.CreateDefault();
            settings.WatchProfiles[0].WatchFolder = watchFolder;
            settings.WatchProfiles[0].StagingFolder = Path.Combine(Path.GetTempPath(), "cncsync-staging");
            settings.Destinations[0].Type = DestinationType.LocalFolder;
            settings.Destinations[0].LocalRootPath = destinationFolder;
            settings.Destinations[0].Host = string.Empty;

            var result = validator.Validate(settings);

            Assert.True(result.IsValid);
        }
        finally
        {
            Directory.Delete(watchFolder, recursive: true);
            Directory.Delete(destinationFolder, recursive: true);
        }
    }

    [Fact]
    public void Normalize_FillsInMissingCollectionsForOlderSettings()
    {
        var settings = new AppSettings
        {
            Destinations = [],
            WatchProfiles = []
        };

        settings.Normalize();

        Assert.NotEmpty(settings.Destinations);
        Assert.NotEmpty(settings.WatchProfiles);
        Assert.Equal(settings.Destinations[0].Id, settings.WatchProfiles[0].DestinationId);
    }

    [Fact]
    public void Validator_RejectsIncompleteNetworkShareDestination()
    {
        var validator = new AppSettingsValidator();
        var watchFolder = Path.Combine(Path.GetTempPath(), $"cncsync-watch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(watchFolder);

        try
        {
            var settings = AppSettings.CreateDefault();
            settings.WatchProfiles[0].WatchFolder = watchFolder;
            settings.WatchProfiles[0].StagingFolder = Path.Combine(Path.GetTempPath(), "cncsync-staging");
            settings.Destinations[0].Type = DestinationType.NetworkShare;
            settings.Destinations[0].NetworkHost = "fileserver.local";
            settings.Destinations[0].UseCurrentUserCredentials = false;

            var result = validator.Validate(settings);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.Contains("share name", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Errors, error => error.Contains("username", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Errors, error => error.Contains("password", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(watchFolder, recursive: true);
        }
    }

    [Fact]
    public void Validator_RejectsSharedWatchOrStagingFolders()
    {
        var validator = new AppSettingsValidator();
        var watchFolder = Path.Combine(Path.GetTempPath(), $"cncsync-watch-{Guid.NewGuid():N}");
        var stagingFolder = Path.Combine(Path.GetTempPath(), $"cncsync-stage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(watchFolder);
        Directory.CreateDirectory(stagingFolder);

        try
        {
            var settings = AppSettings.CreateDefault();
            settings.WatchProfiles[0].WatchFolder = watchFolder;
            settings.WatchProfiles[0].StagingFolder = stagingFolder;
            settings.WatchProfiles.Add(new WatchProfileSettings
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Watch 2",
                Enabled = true,
                WatchFolder = watchFolder,
                StagingFolder = stagingFolder,
                DestinationId = settings.Destinations[0].Id,
                ProcessingSetupId = settings.ProcessingSetups[0].Id,
                StabilityDelaySeconds = 10,
                StabilityPollingSeconds = 5
            });

            var result = validator.Validate(settings);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.Contains("unique watch folder", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Errors, error => error.Contains("unique staging folder", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(watchFolder, recursive: true);
            Directory.Delete(stagingFolder, recursive: true);
        }
    }

    [Fact]
    public void Validator_AllowsDisabledProfileToShareWatchOrStagingFolders()
    {
        var validator = new AppSettingsValidator();
        var watchFolder = Path.Combine(Path.GetTempPath(), $"cncsync-watch-{Guid.NewGuid():N}");
        var stagingFolder = Path.Combine(Path.GetTempPath(), $"cncsync-stage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(watchFolder);
        Directory.CreateDirectory(stagingFolder);

        try
        {
            var settings = AppSettings.CreateDefault();
            settings.WatchProfiles[0].WatchFolder = watchFolder;
            settings.WatchProfiles[0].StagingFolder = stagingFolder;
            settings.WatchProfiles[0].Enabled = true;
            settings.WatchProfiles.Add(new WatchProfileSettings
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Watch 2",
                Enabled = false,
                WatchFolder = watchFolder,
                StagingFolder = stagingFolder,
                DestinationId = settings.Destinations[0].Id,
                ProcessingSetupId = settings.ProcessingSetups[0].Id,
                StabilityDelaySeconds = 10,
                StabilityPollingSeconds = 5
            });

            var result = validator.Validate(settings);

            Assert.DoesNotContain(result.Errors, error => error.Contains("unique watch folder", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Errors, error => error.Contains("unique staging folder", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(watchFolder, recursive: true);
            Directory.Delete(stagingFolder, recursive: true);
        }
    }

    [Fact]
    public void Validator_RejectsOverlappingWatchStagingAndLocalOutputFolders()
    {
        var validator = new AppSettingsValidator();
        var root = Path.Combine(Path.GetTempPath(), $"cncsync-loop-guard-{Guid.NewGuid():N}");
        var watchFolder = Path.Combine(root, "Input");
        Directory.CreateDirectory(watchFolder);

        try
        {
            var settings = AppSettings.CreateDefault();
            settings.WatchProfiles[0].WatchFolder = watchFolder;
            settings.WatchProfiles[0].StagingFolder = Path.Combine(watchFolder, "Staging");
            settings.Destinations[0].Type = DestinationType.LocalFolder;
            settings.Destinations[0].LocalRootPath = Path.Combine(watchFolder, "Output");
            settings.Destinations[0].Host = string.Empty;

            var result = validator.Validate(settings);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.Contains("watch folder and staging folder must not overlap", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Errors, error => error.Contains("watch folder and local destination folder must not overlap", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Validator_AllowsSiblingInputStagingAndOutputFolders()
    {
        var validator = new AppSettingsValidator();
        var root = Path.Combine(Path.GetTempPath(), $"cncsync-loop-guard-{Guid.NewGuid():N}");
        var watchFolder = Path.Combine(root, "Input");
        var stagingFolder = Path.Combine(root, "InputStaging");
        var outputFolder = Path.Combine(root, "Output");
        Directory.CreateDirectory(watchFolder);

        try
        {
            var settings = AppSettings.CreateDefault();
            settings.WatchProfiles[0].WatchFolder = watchFolder;
            settings.WatchProfiles[0].StagingFolder = stagingFolder;
            settings.Destinations[0].Type = DestinationType.LocalFolder;
            settings.Destinations[0].LocalRootPath = outputFolder;
            settings.Destinations[0].Host = string.Empty;

            var result = validator.Validate(settings);

            Assert.True(result.IsValid);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Validator_RejectsLocalOutputThatFeedsAnotherEnabledWatchFolder()
    {
        var validator = new AppSettingsValidator();
        var root = Path.Combine(Path.GetTempPath(), $"cncsync-loop-guard-{Guid.NewGuid():N}");
        var firstWatchFolder = Path.Combine(root, "InputA");
        var secondWatchFolder = Path.Combine(root, "InputB");
        Directory.CreateDirectory(firstWatchFolder);
        Directory.CreateDirectory(secondWatchFolder);

        try
        {
            var settings = AppSettings.CreateDefault();
            settings.WatchProfiles[0].WatchFolder = firstWatchFolder;
            settings.WatchProfiles[0].StagingFolder = Path.Combine(root, "StageA");
            settings.Destinations[0].Type = DestinationType.LocalFolder;
            settings.Destinations[0].LocalRootPath = secondWatchFolder;
            settings.Destinations[0].Host = string.Empty;
            settings.WatchProfiles.Add(new WatchProfileSettings
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Watch 2",
                Enabled = true,
                WatchFolder = secondWatchFolder,
                StagingFolder = Path.Combine(root, "StageB"),
                DestinationId = settings.Destinations[0].Id,
                ProcessingSetupId = settings.ProcessingSetups[0].Id,
                StabilityDelaySeconds = 10,
                StabilityPollingSeconds = 5
            });

            var result = validator.Validate(settings);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.Contains("local destination folder must not overlap enabled watch folder", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Validator_WarnsWhenChangedFilesAndFoldersIsCombinedWithReplaceRemoteFolderOnUpload()
    {
        var validator = new AppSettingsValidator();
        var watchFolder = Path.Combine(Path.GetTempPath(), $"cncsync-watch-{Guid.NewGuid():N}");
        var stagingFolder = Path.Combine(Path.GetTempPath(), $"cncsync-stage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(watchFolder);
        Directory.CreateDirectory(stagingFolder);

        try
        {
            var settings = AppSettings.CreateDefault();
            settings.WatchProfiles[0].WatchFolder = watchFolder;
            settings.WatchProfiles[0].StagingFolder = stagingFolder;
            settings.WatchProfiles[0].WorkItemMode = WatchProfileWorkItemMode.ChangedFilesAndFolders;
            settings.Destinations[0].Host = "example.local";
            settings.Destinations[0].ReplaceRemoteFolderOnUpload = true;

            var result = validator.Validate(settings);

            Assert.True(result.IsValid);
            Assert.True(result.HasWarnings);
            Assert.Contains(result.Warnings, warning => warning.Contains("Individual files and folders", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning => warning.Contains("Grouped project folders", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(watchFolder, recursive: true);
            Directory.Delete(stagingFolder, recursive: true);
        }
    }
}
