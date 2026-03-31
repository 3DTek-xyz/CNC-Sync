namespace CNCSync.Core.Services;

public static class FileSystemItemFilter
{
    public static bool ShouldIgnoreFileSystemItem(string? itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return true;
        }

        return itemName.StartsWith(".", StringComparison.Ordinal) ||
               itemName.StartsWith("._", StringComparison.Ordinal) ||
               string.Equals(itemName, "Thumbs.db", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(itemName, "desktop.ini", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(itemName, "ehthumbs.db", StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<string> EnumerateIncludedFiles(string rootPath) =>
        File.Exists(rootPath)
            ? EnumerateSingleFile(rootPath)
            : Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
                .Where(path => !ShouldIgnoreAnyPathSegment(path, rootPath));

    public static IEnumerable<string> EnumerateIncludedDirectories(string rootPath) =>
        Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories)
            .Where(path => !ShouldIgnoreAnyPathSegment(path, rootPath));

    private static bool ShouldIgnoreAnyPathSegment(string fullPath, string rootPath)
    {
        var relativePath = Path.GetRelativePath(rootPath, fullPath);
        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(ShouldIgnoreFileSystemItem);
    }

    private static IEnumerable<string> EnumerateSingleFile(string filePath)
    {
        if (!ShouldIgnoreFileSystemItem(Path.GetFileName(filePath)))
        {
            yield return filePath;
        }
    }
}
