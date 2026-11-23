using CNCSync.Core.Configuration;

namespace CNCSync.Tests;

public sealed class FolderOverlapValidationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("cncsync-overlaps-").FullName;

    [Theory]
    [InlineData("watch", "watch")]
    [InlineData("watch", "staging")]
    [InlineData("watch", "local destination")]
    [InlineData("staging", "watch")]
    [InlineData("staging", "staging")]
    [InlineData("staging", "local destination")]
    [InlineData("local destination", "watch")]
    [InlineData("local destination", "staging")]
    public void Validator_RejectsNestedFoldersAcrossProfiles(string parentKind, string childKind)
    {
        var settings = CreateTwoProfiles();
        var parent = Path.Combine(_root, "parent");
        SetFolder(settings, 0, parentKind, parent);
        SetFolder(settings, 1, childKind, Path.Combine(parent, "child"));

        var result = new AppSettingsValidator().Validate(settings);

        Assert.Contains(result.Errors, error => error.Contains("overlap", StringComparison.OrdinalIgnoreCase) &&
            error.Contains("Profile 1", StringComparison.Ordinal) && error.Contains("Profile 2", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_ResolvesAncestorSymlinkForNotYetCreatedStagingFolder()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Windows symlink creation requires an optional privilege on CI.
        }

        var settings = CreateTwoProfiles();
        var alias = Path.Combine(_root, "alias");
        Directory.CreateSymbolicLink(alias, Path.GetFileName(settings.WatchProfiles[0].WatchFolder));
        settings.WatchProfiles[1].StagingFolder = Path.Combine(alias, "missing-stage");

        var result = new AppSettingsValidator().Validate(settings);

        Assert.Contains(result.Errors, error => error.Contains("overlap", StringComparison.OrdinalIgnoreCase) &&
            error.Contains("Profile 1", StringComparison.Ordinal) && error.Contains("Profile 2", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_AllowsDisjointOutputSubfoldersOnSharedDestination()
    {
        var settings = CreateTwoProfiles();
        settings.WatchProfiles[1].DestinationId = settings.WatchProfiles[0].DestinationId;
        settings.WatchProfiles[0].RemoteSubfolder = "machine-1";
        settings.WatchProfiles[1].RemoteSubfolder = "machine-2";

        Assert.Empty(new AppSettingsValidator().Validate(settings).Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Validator_AllowsSharedOutputUnlessOneDestinationReplacesFolders(bool replace)
    {
        var settings = CreateTwoProfiles();
        settings.Destinations[1].LocalRootPath = settings.Destinations[0].LocalRootPath;
        settings.Destinations[1].ReplaceRemoteFolderOnUpload = replace;

        var result = new AppSettingsValidator().Validate(settings);

        Assert.Equal(!replace, result.IsValid);
    }

    [Fact]
    public void Validator_RejectsOutputSubfolderThatResolvesIntoAnotherWatchFolder()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var settings = CreateTwoProfiles();
        var output = Directory.CreateDirectory(settings.Destinations[0].LocalRootPath).FullName;
        Directory.CreateSymbolicLink(Path.Combine(output, "machine-1"), settings.WatchProfiles[1].WatchFolder);
        settings.WatchProfiles[0].RemoteSubfolder = "machine-1";

        var result = new AppSettingsValidator().Validate(settings);

        Assert.Contains(result.Errors, error => error.Contains("local destination folder must not overlap enabled watch folder", StringComparison.Ordinal));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private AppSettings CreateTwoProfiles()
    {
        var settings = AppSettings.CreateDefault();
        settings.Destinations.Add(DestinationSettings.CreateDefault("Destination 2"));
        settings.WatchProfiles.Add(new WatchProfileSettings());
        for (var index = 0; index < 2; index++)
        {
            settings.WatchProfiles[index].Name = $"Profile {index + 1}";
            settings.WatchProfiles[index].Enabled = true;
            settings.WatchProfiles[index].DestinationId = settings.Destinations[index].Id;
            settings.Destinations[index].Type = DestinationType.LocalFolder;
            SetFolder(settings, index, "watch", Path.Combine(_root, $"watch-{index}"));
            SetFolder(settings, index, "staging", Path.Combine(_root, $"staging-{index}"));
            SetFolder(settings, index, "local destination", Path.Combine(_root, $"output-{index}"));
        }

        return settings;
    }

    private static void SetFolder(AppSettings settings, int index, string kind, string path)
    {
        switch (kind)
        {
            case "watch":
                settings.WatchProfiles[index].WatchFolder = Directory.CreateDirectory(path).FullName;
                break;
            case "staging":
                settings.WatchProfiles[index].StagingFolder = path;
                break;
            case "local destination":
                settings.Destinations[index].LocalRootPath = path;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }
}
