namespace CBWSSSync.Core.Configuration;

public sealed class AppSettingsValidator
{
    public AppSettingsValidationResult Validate(AppSettings settings)
    {
        var result = new AppSettingsValidationResult();
        var destinationsById = settings.FtpDestinations
            .Where(destination => !string.IsNullOrWhiteSpace(destination.Id))
            .GroupBy(destination => destination.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        if (settings.WatchProfiles.Count == 0)
        {
            result.Errors.Add("At least one watch profile is required.");
        }

        for (var index = 0; index < settings.FtpDestinations.Count; index++)
        {
            var destination = settings.FtpDestinations[index];
            var label = string.IsNullOrWhiteSpace(destination.Name)
                ? $"FTP destination #{index + 1}"
                : $"FTP destination '{destination.Name}'";

            if (string.IsNullOrWhiteSpace(destination.Name))
            {
                result.Errors.Add($"{label} needs a name.");
            }

            if (destination.Port is < 1 or > 65535)
            {
                result.Errors.Add($"{label} port must be between 1 and 65535.");
            }

            if (!IsValidRemotePath(destination.RemoteBasePath))
            {
                result.Errors.Add($"{label} remote base path must use a slash-style server path.");
            }

            if (!destination.UseAnonymousFtp && string.IsNullOrWhiteSpace(destination.Username))
            {
                result.Errors.Add($"{label} username is required when anonymous FTP is disabled.");
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
                result.Errors.Add($"{label} remote subfolder must use a slash-style server path.");
            }

            if (profile.StabilityDelaySeconds < 1)
            {
                result.Errors.Add($"{label} stability delay must be at least 1 second.");
            }

            if (profile.StabilityPollingSeconds < 1)
            {
                result.Errors.Add($"{label} polling interval must be at least 1 second.");
            }

            if (!string.IsNullOrWhiteSpace(profile.FtpDestinationId) &&
                !destinationsById.ContainsKey(profile.FtpDestinationId))
            {
                result.Errors.Add($"{label} references an FTP destination that does not exist.");
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
