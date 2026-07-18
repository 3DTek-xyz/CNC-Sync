namespace CNCSync.Core.Configuration;

public sealed class AppSettingsValidator
{
    public AppSettingsValidationResult Validate(AppSettings settings)
    {
        var result = new AppSettingsValidationResult();
        var destinationsById = settings.Destinations
            .Where(destination => !string.IsNullOrWhiteSpace(destination.Id))
            .GroupBy(destination => destination.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var processingSetupsById = settings.ProcessingSetups
            .Where(setup => !string.IsNullOrWhiteSpace(setup.Id))
            .GroupBy(setup => setup.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        if (settings.WatchProfiles.Count == 0)
        {
            result.Errors.Add("At least one watch profile is required.");
        }

        if (settings.ProcessingSetups.Count == 0)
        {
            result.Errors.Add("At least one processing setup is required.");
        }

        for (var index = 0; index < settings.Destinations.Count; index++)
        {
            var destination = settings.Destinations[index];
            var destinationKind = destination.Type switch
            {
                DestinationType.LocalFolder => "local destination",
                DestinationType.NetworkShare => "network destination",
                DestinationType.Sftp => "SFTP destination",
                DestinationType.Scp => "SCP destination",
                _ => "FTP destination"
            };
            var label = string.IsNullOrWhiteSpace(destination.Name)
                ? $"{destinationKind} #{index + 1}"
                : $"{destinationKind} '{destination.Name}'";

            if (string.IsNullOrWhiteSpace(destination.Name))
            {
                result.Errors.Add($"{label} needs a name.");
            }

            if ((destination.Type == DestinationType.Ftp || destination.Type == DestinationType.Sftp || destination.Type == DestinationType.Scp) &&
                destination.Port is < 1 or > 65535)
            {
                result.Errors.Add($"{label} port must be between 1 and 65535.");
            }

            if (!IsValidRemotePath(destination.RemoteBasePath))
            {
                result.Errors.Add($"{label} remote base path must use a slash-style server path.");
            }

            if (destination.Type == DestinationType.Ftp &&
                !destination.UseAnonymousFtp &&
                string.IsNullOrWhiteSpace(destination.Username))
            {
                result.Errors.Add($"{label} username is required when anonymous FTP is disabled.");
            }

            if (destination.Type == DestinationType.Ftp && string.IsNullOrWhiteSpace(destination.Host))
            {
                result.Errors.Add($"{label} host is required.");
            }

            if (destination.Type == DestinationType.Sftp && string.IsNullOrWhiteSpace(destination.Host))
            {
                result.Errors.Add($"{label} host is required.");
            }

            if (destination.Type == DestinationType.Sftp && string.IsNullOrWhiteSpace(destination.Username))
            {
                result.Errors.Add($"{label} username is required.");
            }

            if (destination.Type == DestinationType.Sftp &&
                destination.SshAuthenticationMode == SshAuthenticationMode.Password &&
                string.IsNullOrWhiteSpace(destination.Password))
            {
                result.Errors.Add($"{label} password is required.");
            }

            if (destination.Type == DestinationType.Sftp &&
                destination.SshAuthenticationMode == SshAuthenticationMode.PrivateKey &&
                string.IsNullOrWhiteSpace(destination.PrivateKeyPath))
            {
                result.Errors.Add($"{label} private key path is required.");
            }

            if (destination.Type == DestinationType.Sftp &&
                destination.SshAuthenticationMode == SshAuthenticationMode.PrivateKey &&
                !string.IsNullOrWhiteSpace(destination.PrivateKeyPath) &&
                !File.Exists(ExpandHomeDirectory(destination.PrivateKeyPath)))
            {
                result.Errors.Add($"{label} private key path does not exist.");
            }

            if (destination.Type == DestinationType.Scp && string.IsNullOrWhiteSpace(destination.Host))
            {
                result.Errors.Add($"{label} host is required.");
            }

            if (destination.Type == DestinationType.Scp && string.IsNullOrWhiteSpace(destination.Username))
            {
                result.Errors.Add($"{label} username is required.");
            }

            if (destination.Type == DestinationType.Scp &&
                destination.SshAuthenticationMode == SshAuthenticationMode.Password &&
                string.IsNullOrWhiteSpace(destination.Password))
            {
                result.Errors.Add($"{label} password is required.");
            }

            if (destination.Type == DestinationType.Scp &&
                destination.SshAuthenticationMode == SshAuthenticationMode.PrivateKey &&
                string.IsNullOrWhiteSpace(destination.PrivateKeyPath))
            {
                result.Errors.Add($"{label} private key path is required.");
            }

            if (destination.Type == DestinationType.Scp &&
                destination.SshAuthenticationMode == SshAuthenticationMode.PrivateKey &&
                !string.IsNullOrWhiteSpace(destination.PrivateKeyPath) &&
                !File.Exists(ExpandHomeDirectory(destination.PrivateKeyPath)))
            {
                result.Errors.Add($"{label} private key path does not exist.");
            }

            if (destination.Type == DestinationType.LocalFolder && string.IsNullOrWhiteSpace(destination.LocalRootPath))
            {
                result.Errors.Add($"{label} local root path is required.");
            }

            if (destination.Type == DestinationType.NetworkShare && string.IsNullOrWhiteSpace(destination.NetworkHost))
            {
                result.Errors.Add($"{label} server host is required.");
            }

            if (destination.Type == DestinationType.NetworkShare && string.IsNullOrWhiteSpace(destination.NetworkShareName))
            {
                result.Errors.Add($"{label} share name is required.");
            }

            if (destination.Type == DestinationType.NetworkShare &&
                !destination.UseCurrentUserCredentials &&
                string.IsNullOrWhiteSpace(destination.Username))
            {
                result.Errors.Add($"{label} username is required when not using current user credentials.");
            }

            if (destination.Type == DestinationType.NetworkShare &&
                !destination.UseCurrentUserCredentials &&
                string.IsNullOrWhiteSpace(destination.Password))
            {
                result.Errors.Add($"{label} password is required when not using current user credentials.");
            }
        }

        for (var index = 0; index < settings.ProcessingSetups.Count; index++)
        {
            var setup = settings.ProcessingSetups[index];
            var label = string.IsNullOrWhiteSpace(setup.Name)
                ? $"Processing setup #{index + 1}"
                : $"Processing setup '{setup.Name}'";

            if (string.IsNullOrWhiteSpace(setup.Name))
            {
                result.Errors.Add($"{label} needs a name.");
            }

            if (setup.Mode == ProcessingMode.ExternalScript)
            {
                if (string.IsNullOrWhiteSpace(setup.ScriptPath))
                {
                    result.Errors.Add($"{label} script path is required for external script mode.");
                }
                else if (!File.Exists(setup.ScriptPath))
                {
                    result.Errors.Add($"{label} script path does not exist.");
                }
            }

            if (setup.Mode == ProcessingMode.ProCutApi)
            {
                if (string.IsNullOrWhiteSpace(settings.ProCutApi.BaseUrl) ||
                    !Uri.TryCreate(settings.ProCutApi.BaseUrl, UriKind.Absolute, out _))
                {
                    result.Errors.Add($"{label} requires a valid ProCut Suite API base URL.");
                }

                if (string.IsNullOrWhiteSpace(settings.ProCutApi.ApiKey))
                {
                    result.Errors.Add($"{label} requires a saved ProCut Suite API key.");
                }

                if (string.IsNullOrWhiteSpace(setup.ProCutApiEndpoint))
                {
                    result.Errors.Add($"{label} requires a ProCut Suite API endpoint.");
                }

                if (!setup.ProCutArcFittingEnabled &&
                    !setup.ProCutLineJoinerEnabled &&
                    !setup.ProCutArcJoinerEnabled &&
                    !setup.ProCutCornerSmoothEnabled)
                {
                    result.Errors.Add($"{label} requires at least one G-code tool to be enabled.");
                }
            }
        }

        for (var index = 0; index < settings.WatchProfiles.Count; index++)
        {
            var profile = settings.WatchProfiles[index];
            var label = string.IsNullOrWhiteSpace(profile.Name)
                ? $"Watch profile #{index + 1}"
                : $"Watch profile '{profile.Name}'";

            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                result.Errors.Add($"{label} needs a name.");
            }

            if (string.IsNullOrWhiteSpace(profile.WatchFolder))
            {
                result.Errors.Add($"{label} watch folder is required.");
            }
            else if (!Directory.Exists(profile.WatchFolder))
            {
                result.Errors.Add($"{label} watch folder does not exist.");
            }

            if (string.IsNullOrWhiteSpace(profile.StagingFolder))
            {
                result.Errors.Add($"{label} staging folder is required.");
            }

            if (!IsValidRemotePath(profile.RemoteSubfolder))
            {
                result.Errors.Add($"{label} additional remote path must use a slash-style server path.");
            }

            if (profile.StabilityDelaySeconds < 1)
            {
                result.Errors.Add($"{label} stability delay must be at least 1 second.");
            }

            if (profile.StabilityPollingSeconds < 1)
            {
                result.Errors.Add($"{label} polling interval must be at least 1 second.");
            }

            if (!string.IsNullOrWhiteSpace(profile.DestinationId) &&
                !destinationsById.ContainsKey(profile.DestinationId))
            {
                result.Errors.Add($"{label} references a destination that does not exist.");
            }

            if (!string.IsNullOrWhiteSpace(profile.ProcessingSetupId) &&
                !processingSetupsById.ContainsKey(profile.ProcessingSetupId))
            {
                result.Errors.Add($"{label} references a processing setup that does not exist.");
            }

            if (!string.IsNullOrWhiteSpace(profile.DestinationId) &&
                destinationsById.TryGetValue(profile.DestinationId, out var destination) &&
                profile.WorkItemMode == WatchProfileWorkItemMode.ChangedFilesAndFolders &&
                destination.ReplaceRemoteFolderOnUpload)
            {
                var destinationName = string.IsNullOrWhiteSpace(destination.Name)
                    ? "the selected destination"
                    : $"destination '{destination.Name}'";
                result.Warnings.Add(
                    $"{label} uses Work Item Mode 'Individual files and folders' together with Replace Remote Folder On Upload on {destinationName}. " +
                    "This can be dangerous for job-folder workflows because a nested child folder may be treated as its own upload target and replace the matching top-level remote folder. " +
                    "Use 'Grouped project folders' when each first-level folder under the watch root should be treated as one job.");
            }
        }

        AddUniqueFolderValidationErrors(
            settings.WatchProfiles,
            profile => profile.WatchFolder,
            "watch folder",
            result);

        AddUniqueFolderValidationErrors(
            settings.WatchProfiles,
            profile => profile.StagingFolder,
            "staging folder",
            result);

        AddFolderLoopValidationErrors(settings, destinationsById, result);

        return result;
    }

    private static void AddFolderLoopValidationErrors(
        AppSettings settings,
        IReadOnlyDictionary<string, DestinationSettings> destinationsById,
        AppSettingsValidationResult result)
    {
        var enabledProfiles = settings.WatchProfiles
            .Where(profile => profile.Enabled)
            .Select(profile => new
            {
                Profile = profile,
                Label = string.IsNullOrWhiteSpace(profile.Name)
                    ? $"Watch profile '{profile.Id}'"
                    : $"Watch profile '{profile.Name}'",
                WatchPath = NormalizeDirectoryPath(profile.WatchFolder),
                StagingPath = NormalizeDirectoryPath(profile.StagingFolder),
                Destination = !string.IsNullOrWhiteSpace(profile.DestinationId) &&
                              destinationsById.TryGetValue(profile.DestinationId, out var destination)
                    ? destination
                    : null
            })
            .ToList();

        foreach (var profile in enabledProfiles)
        {
            if (DirectoryPathsOverlap(profile.WatchPath, profile.StagingPath))
            {
                result.Errors.Add($"{profile.Label} watch folder and staging folder must not overlap. Put staging outside the watched tree to prevent processing loops.");
            }

            if (profile.Destination?.Type == DestinationType.LocalFolder)
            {
                var localOutputPath = NormalizeDirectoryPath(profile.Destination.LocalRootPath);
                if (DirectoryPathsOverlap(profile.WatchPath, localOutputPath))
                {
                    result.Errors.Add($"{profile.Label} watch folder and local destination folder must not overlap. Output inside the watched tree can be reprocessed indefinitely.");
                }

                if (DirectoryPathsOverlap(profile.StagingPath, localOutputPath))
                {
                    result.Errors.Add($"{profile.Label} staging folder and local destination folder must not overlap. Staging and output must be separate folders.");
                }
            }
        }

        foreach (var sourceProfile in enabledProfiles)
        {
            if (sourceProfile.Destination?.Type != DestinationType.LocalFolder)
            {
                continue;
            }

            var localOutputPath = NormalizeDirectoryPath(sourceProfile.Destination.LocalRootPath);
            foreach (var targetProfile in enabledProfiles)
            {
                if (DirectoryPathsOverlap(localOutputPath, targetProfile.WatchPath))
                {
                    result.Errors.Add(
                        $"{sourceProfile.Label} local destination folder must not overlap enabled watch folder '{targetProfile.Profile.Name}'. " +
                        "Local output can feed another watch profile and create a processing loop.");
                }
            }
        }
    }

    private static void AddUniqueFolderValidationErrors(
        IReadOnlyList<WatchProfileSettings> profiles,
        Func<WatchProfileSettings, string?> pathSelector,
        string fieldLabel,
        AppSettingsValidationResult result)
    {
        var duplicates = profiles
            .Where(profile => profile.Enabled)
            .Select(profile => new
            {
                ProfileName = string.IsNullOrWhiteSpace(profile.Name) ? profile.Id : profile.Name,
                Path = NormalizeDirectoryPath(pathSelector(profile))
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Path))
            .GroupBy(item => item.Path!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1);

        foreach (var duplicate in duplicates)
        {
            var profileNames = string.Join(", ", duplicate.Select(item => item.ProfileName));
            result.Errors.Add($"Enabled watch profiles must use a unique {fieldLabel}. Shared {fieldLabel} found for: {profileNames}.");
        }
    }

    private static bool IsValidRemotePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        return !path.Contains('\\');
    }

    private static string ExpandHomeDirectory(string path)
    {
        if (!path.StartsWith("~/", StringComparison.Ordinal) &&
            !path.StartsWith("~\\", StringComparison.Ordinal))
        {
            return path;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, path[2..]);
    }

    private static string? NormalizeDirectoryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim();
        }
    }

    private static bool DirectoryPathsOverlap(string? firstPath, string? secondPath)
    {
        if (string.IsNullOrWhiteSpace(firstPath) || string.IsNullOrWhiteSpace(secondPath))
        {
            return false;
        }

        var first = TrimDirectoryPath(firstPath);
        var second = TrimDirectoryPath(secondPath);
        return string.Equals(first, second, StringComparison.OrdinalIgnoreCase) ||
               IsSubdirectoryOf(first, second) ||
               IsSubdirectoryOf(second, first);
    }

    private static string TrimDirectoryPath(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsSubdirectoryOf(string candidatePath, string parentPath)
    {
        var parentPrefix = TrimDirectoryPath(parentPath) + Path.DirectorySeparatorChar;
        return candidatePath.StartsWith(parentPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
