namespace CBWSSSync.Core.Configuration;

public sealed class AppSettings
{
    public bool LaunchAtLogin { get; set; }
    public bool StartMinimized { get; set; } = true;
    public List<FtpDestinationSettings> FtpDestinations { get; set; } = [];
    public List<WatchProfileSettings> WatchProfiles { get; set; } = [];

    public static AppSettings CreateDefault()
    {
        var ftpDestination = FtpDestinationSettings.CreateDefault("Primary FTP");
        var watchProfile = WatchProfileSettings.CreateDefault("Primary Watch Folder", ftpDestination.Id);

        return new AppSettings
        {
            LaunchAtLogin = false,
            StartMinimized = true,
            FtpDestinations = [ftpDestination],
            WatchProfiles = [watchProfile]
        };
    }

    public AppSettings Normalize()
    {
        FtpDestinations ??= [];
        WatchProfiles ??= [];

        if (FtpDestinations.Count == 0)
        {
            FtpDestinations.Add(FtpDestinationSettings.CreateDefault("Primary FTP"));
        }

        foreach (var destination in FtpDestinations)
        {
            if (string.IsNullOrWhiteSpace(destination.Id))
            {
                destination.Id = Guid.NewGuid().ToString("N");
            }

            if (destination.Port <= 0)
            {
                destination.Port = 21;
            }

            destination.RemoteBasePath = NormalizeRemotePath(destination.RemoteBasePath);
        }

        if (WatchProfiles.Count == 0)
        {
            WatchProfiles.Add(WatchProfileSettings.CreateDefault("Primary Watch Folder", FtpDestinations[0].Id));
        }

        foreach (var profile in WatchProfiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Id))
            {
                profile.Id = Guid.NewGuid().ToString("N");
            }

            if (profile.StabilityDelaySeconds <= 0)
            {
                profile.StabilityDelaySeconds = 30;
            }

            if (profile.StabilityPollingSeconds <= 0)
            {
                profile.StabilityPollingSeconds = 5;
            }

            if (string.IsNullOrWhiteSpace(profile.FtpDestinationId))
            {
                profile.FtpDestinationId = FtpDestinations[0].Id;
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
}
