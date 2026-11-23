namespace CNCSync.Core.Configuration;

internal static class ProfileFolderValidator
{
    private const string Watch = "watch folder";
    private const string Staging = "staging folder";
    private const string Output = "local destination folder";
    private const StringComparison PathComparison = StringComparison.OrdinalIgnoreCase;

    public static void AddErrors(
        IReadOnlyList<WatchProfileSettings> profiles,
        IReadOnlyDictionary<string, DestinationSettings> destinations,
        AppSettingsValidationResult result)
    {
        var folders = new List<ProfileFolder>();
        for (var index = 0; index < profiles.Count; index++)
        {
            var profile = profiles[index];
            if (!profile.Enabled)
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(profile.Name) ? $"#{index + 1}" : profile.Name;
            AddFolder(folders, index, name, Watch, profile.WatchFolder, result);
            AddFolder(folders, index, name, Staging, profile.StagingFolder, result);
            if (!string.IsNullOrWhiteSpace(profile.DestinationId) &&
                destinations.TryGetValue(profile.DestinationId, out var destination))
            {
                AddOutputFolder(folders, index, name, profile, destination, result);
            }
        }

        for (var i = 0; i < folders.Count; i++)
        {
            for (var j = i + 1; j < folders.Count; j++)
            {
                if (CanConflict(folders[i], folders[j]) &&
                    folders[i].Paths.Any(first => folders[j].Paths.Any(second => Overlaps(first, second))))
                {
                    result.Errors.Add(DescribeOverlap(folders[i], folders[j]));
                }
            }
        }
    }

    private static void AddOutputFolder(
        List<ProfileFolder> folders, int index, string name, WatchProfileSettings profile,
        DestinationSettings destination, AppSettingsValidationResult result)
    {
        var root = destination.Type switch
        {
            DestinationType.LocalFolder => destination.LocalRootPath,
            DestinationType.NetworkShare when OperatingSystem.IsWindows() &&
                !string.IsNullOrWhiteSpace(destination.NetworkHost) &&
                !string.IsNullOrWhiteSpace(destination.NetworkShareName) =>
                    $@"\\{destination.NetworkHost}\{destination.NetworkShareName}",
            _ => null
        };
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        // These are the same path segments used to route the profile's uploads.
        var segments = new[] { destination.RemoteBasePath, profile.RemoteSubfolder }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .SelectMany(path => path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var output = segments.Aggregate(root, Path.Combine);
        AddFolder(folders, index, name, Output, output, result, destination.ReplaceRemoteFolderOnUpload);
    }

    private static void AddFolder(
        List<ProfileFolder> folders, int index, string name, string kind, string? path,
        AppSettingsValidationResult result, bool replacesOutput = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(RemoveWindowsExtendedPrefix(path));
            var lexical = ComparablePath(fullPath);
            // Do not probe UNC shares during overlap validation; compare their paths lexically.
            var remainingLinks = 40;
            var resolved = OperatingSystem.IsWindows() && lexical.StartsWith(@"\\", StringComparison.Ordinal)
                ? lexical
                : ComparablePath(ResolveAliases(fullPath, ref remainingLinks));
            folders.Add(new ProfileFolder(index, name, kind, [lexical, resolved], replacesOutput));
        }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            result.Errors.Add($"Watch profile '{name}' {kind} could not be resolved for overlap validation: {error.Message}");
        }
    }

    private static string ResolveAliases(string fullPath, ref int remainingLinks)
    {
        var root = Path.GetPathRoot(fullPath)!;
        var current = root;
        foreach (var part in fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(current, part);
            var target = new DirectoryInfo(candidate).LinkTarget;
            if (target is null)
            {
                current = candidate;
                continue;
            }

            if (remainingLinks-- == 0)
            {
                throw new IOException("Too many symbolic links in the directory path.");
            }

            current = ResolveAliases(Path.GetFullPath(RemoveWindowsExtendedPrefix(target), current), ref remainingLinks);
        }

        return current;
    }

    private static string ComparablePath(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return OperatingSystem.IsMacOS() ? trimmed.Normalize() : trimmed;
    }

    private static string RemoveWindowsExtendedPrefix(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            {
                path = @"\\" + path[8..];
            }
            else if (path.StartsWith(@"\\?\", StringComparison.Ordinal) && path.Length >= 7 && path[5] == ':')
            {
                path = path[4..];
            }
        }

        return path;
    }

    private static bool Overlaps(string first, string second) =>
        string.Equals(first, second, PathComparison) ||
        first.StartsWith(second + Path.DirectorySeparatorChar, PathComparison) ||
        second.StartsWith(first + Path.DirectorySeparatorChar, PathComparison);

    private static bool CanConflict(ProfileFolder first, ProfileFolder second) =>
        first.Kind != Output || second.Kind != Output || first.ReplacesOutput || second.ReplacesOutput;

    private static string DescribeOverlap(ProfileFolder first, ProfileFolder second)
    {
        if (first.Kind == Output && second.Kind == Output)
        {
            return $"Output folders for '{first.Name}' and '{second.Name}' must not overlap when either destination replaces folders on upload.";
        }

        if (first.Index == second.Index)
        {
            return $"Watch profile '{first.Name}' {first.Kind} and {second.Kind} must not overlap.";
        }

        if (first.Kind == second.Kind && first.Kind is Watch or Staging)
        {
            return $"Enabled watch profiles must use a unique {first.Kind}. Folders for '{first.Name}' and '{second.Name}' overlap.";
        }

        if (second.Kind == Output && first.Kind != Output)
        {
            return DescribeOverlap(second, first);
        }

        return $"Watch profile '{first.Name}' {first.Kind} must not overlap enabled {second.Kind} '{second.Name}'.";
    }

    private sealed record ProfileFolder(int Index, string Name, string Kind, string[] Paths, bool ReplacesOutput);
}
