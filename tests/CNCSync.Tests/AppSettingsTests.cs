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
}
