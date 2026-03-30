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

            if (destination.Type == DestinationType.Sftp && string.IsNullOrWhiteSpace(destination.Password))
            {
                result.Errors.Add($"{label} password is required.");
            }

            if (destination.Type == DestinationType.Scp && string.IsNullOrWhiteSpace(destination.Host))
            {
                result.Errors.Add($"{label} host is required.");
            }

            if (destination.Type == DestinationType.Scp && string.IsNullOrWhiteSpace(destination.Username))
            {
                result.Errors.Add($"{label} username is required.");
            }

            if (destination.Type == DestinationType.Scp && string.IsNullOrWhiteSpace(destination.Password))
            {
                result.Errors.Add($"{label} password is required.");
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
        }

        return result;
    }

    private static bool IsValidRemotePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        return !path.Contains('\\');
    }
}
