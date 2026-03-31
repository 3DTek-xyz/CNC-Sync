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
        Assert.True(settings.StartMinimized);
        Assert.False(settings.ScheduledCatchUpEnabled);
        Assert.Equal(10, settings.ScheduledCatchUpIntervalMinutes);
        Assert.Equal(10, profile.StabilityDelaySeconds);
        Assert.Equal(5, profile.StabilityPollingSeconds);
        Assert.Equal(ftp.Id, profile.DestinationId);
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
        var watchFolder = Path.Combine(Path.GetTempPath(), $"cbwss-watch-{Guid.NewGuid():N}");
        var destinationFolder = Path.Combine(Path.GetTempPath(), $"cbwss-destination-{Guid.NewGuid():N}");
        Directory.CreateDirectory(watchFolder);
        Directory.CreateDirectory(destinationFolder);

        try
        {
            var settings = AppSettings.CreateDefault();
            settings.WatchProfiles[0].WatchFolder = watchFolder;
            settings.WatchProfiles[0].StagingFolder = Path.Combine(Path.GetTempPath(), "cbwss-staging");
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
        var watchFolder = Path.Combine(Path.GetTempPath(), $"cbwss-watch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(watchFolder);

        try
        {
            var settings = AppSettings.CreateDefault();
            settings.WatchProfiles[0].WatchFolder = watchFolder;
            settings.WatchProfiles[0].StagingFolder = Path.Combine(Path.GetTempPath(), "cbwss-staging");
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
}
