using CNCSync.Core.Configuration;

namespace CNCSync.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_AreSuitableForFirstRun()
    {
        var settings = AppSettings.CreateDefault();
        var ftp = Assert.Single(settings.FtpDestinations);
        var profile = Assert.Single(settings.WatchProfiles);

        Assert.Equal(21, ftp.Port);
        Assert.True(ftp.UseAnonymousFtp);
        Assert.True(ftp.AutoUpload);
        Assert.True(settings.StartMinimized);
        Assert.Equal(30, profile.StabilityDelaySeconds);
        Assert.Equal(5, profile.StabilityPollingSeconds);
        Assert.Equal(ftp.Id, profile.FtpDestinationId);
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
        Directory.CreateDirectory(watchFolder);

        try
        {
            var settings = AppSettings.CreateDefault();
            settings.WatchProfiles[0].WatchFolder = watchFolder;
            settings.WatchProfiles[0].StagingFolder = Path.Combine(Path.GetTempPath(), "cbwss-staging");

            var result = validator.Validate(settings);

            Assert.True(result.IsValid);
        }
        finally
        {
            Directory.Delete(watchFolder, recursive: true);
        }
    }

    [Fact]
    public void Normalize_FillsInMissingCollectionsForOlderSettings()
    {
        var settings = new AppSettings
        {
            FtpDestinations = [],
            WatchProfiles = []
        };

        settings.Normalize();

        Assert.NotEmpty(settings.FtpDestinations);
        Assert.NotEmpty(settings.WatchProfiles);
        Assert.Equal(settings.FtpDestinations[0].Id, settings.WatchProfiles[0].FtpDestinationId);
    }
}
