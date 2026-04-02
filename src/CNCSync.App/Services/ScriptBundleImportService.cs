using System.IO.Compression;

namespace CNCSync.App.Services;

public sealed class ScriptBundleImportService : IScriptBundleImportService
{
    private static readonly HashSet<string> SupportedScriptExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ps1",
        ".sh",
        ".bat",
        ".cmd",
        ".py"
    };

    private readonly HttpClient _httpClient;

    public ScriptBundleImportService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<ScriptBundleImportResult> ImportAsync(
        string sourceUrl,
        string scriptsDirectoryPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            throw new InvalidOperationException("A script source URL is required.");
        }

        if (!Uri.TryCreate(NormalizeSourceUrl(sourceUrl), UriKind.Absolute, out var sourceUri))
        {
            throw new InvalidOperationException("The script source URL is not a valid absolute URL.");
        }

        var importedRootPath = Path.Combine(scriptsDirectoryPath, "Imported");
        var targetDirectoryPath = Path.Combine(importedRootPath, "CustomSource");
        var workingRootPath = Path.Combine(importedRootPath, ".import-working", Guid.NewGuid().ToString("N"));
        var downloadPath = Path.Combine(workingRootPath, "download.bin");
        var preparedDirectoryPath = Path.Combine(workingRootPath, "prepared");

        Directory.CreateDirectory(workingRootPath);

        try
        {
            using (var response = await _httpClient.GetAsync(sourceUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var inputStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var outputStream = File.Create(downloadPath);
                await inputStream.CopyToAsync(outputStream, cancellationToken);
            }

            Directory.CreateDirectory(preparedDirectoryPath);

            string preparedScriptRelativePath;
            if (await LooksLikeZipAsync(downloadPath, cancellationToken))
            {
                ExtractZipSafely(downloadPath, preparedDirectoryPath);
                preparedScriptRelativePath = ResolveScriptRelativePath(
                    preparedDirectoryPath,
                    null,
                    GetCurrentPlatformDirectoryName());
            }
            else
            {
                var fileName = ResolveImportedFileName(sourceUri);
                File.Copy(downloadPath, Path.Combine(preparedDirectoryPath, fileName), overwrite: true);
                preparedScriptRelativePath = fileName;
            }
            var mergeResult = MergePreparedFilesIntoTarget(preparedDirectoryPath, targetDirectoryPath);

            return new ScriptBundleImportResult(
                targetDirectoryPath,
                BuildImportMessage(targetDirectoryPath, mergeResult));
        }
        finally
        {
            if (Directory.Exists(workingRootPath))
            {
                Directory.Delete(workingRootPath, recursive: true);
            }
        }
    }

    private static string NormalizeSourceUrl(string sourceUrl)
    {
        if (!Uri.TryCreate(sourceUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return sourceUrl.Trim();
        }

        if (!uri.Host.Contains("dropbox.com", StringComparison.OrdinalIgnoreCase))
        {
            return uri.ToString();
        }

        var builder = new UriBuilder(uri);
        var query = ParseQueryString(builder.Query);
        query["dl"] = "1";
        builder.Query = string.Join("&", query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return builder.Uri.ToString();
    }

    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split('=', 2);
            var key = Uri.UnescapeDataString(pieces[0]);
            var value = pieces.Length > 1 ? Uri.UnescapeDataString(pieces[1]) : string.Empty;
            result[key] = value;
        }

        return result;
    }

    private static async Task<bool> LooksLikeZipAsync(string filePath, CancellationToken cancellationToken)
    {
        var buffer = new byte[4];
        await using var stream = File.OpenRead(filePath);
        var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
        return bytesRead == 4 &&
               buffer[0] == 0x50 &&
               buffer[1] == 0x4B &&
               buffer[2] == 0x03 &&
               buffer[3] == 0x04;
    }

    private static string ResolveScriptRelativePath(
        string preparedDirectoryPath,
        string? preferredExistingRelativePath,
        string? preferredPlatformDirectoryName)
    {
        var candidates = Directory
            .EnumerateFiles(preparedDirectoryPath, "*", SearchOption.AllDirectories)
            .Where(path => IsSupportedScriptFile(path))
            .Select(path => Path.GetRelativePath(preparedDirectoryPath, path))
            .ToList();

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("The downloaded bundle did not contain a supported script file.");
        }

        if (!string.IsNullOrWhiteSpace(preferredExistingRelativePath))
        {
            var matchingExistingPath = candidates.FirstOrDefault(path =>
                string.Equals(
                    NormalizeRelativePath(path),
                    NormalizeRelativePath(preferredExistingRelativePath),
                    StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(matchingExistingPath))
            {
                return matchingExistingPath;
            }
        }

        if (!string.IsNullOrWhiteSpace(preferredPlatformDirectoryName))
        {
            var platformCandidate = candidates
                .Where(path => IsPreferredPlatformScriptPath(path, preferredPlatformDirectoryName))
                .OrderBy(path => GetPathDepth(path))
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(platformCandidate))
            {
                return platformCandidate;
            }
        }

        return candidates
            .OrderBy(path => GetPathDepth(path))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static string ResolveImportedFileName(Uri sourceUri)
    {
        var name = Path.GetFileName(Uri.UnescapeDataString(sourceUri.AbsolutePath));
        return string.IsNullOrWhiteSpace(name) ? "imported-script" : name;
    }

    private static string? GetCurrentPlatformDirectoryName()
    {
        if (OperatingSystem.IsMacOS())
        {
            return "macos";
        }

        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }

        if (OperatingSystem.IsLinux())
        {
            return "linux";
        }

        return null;
    }

    private static bool IsPreferredPlatformScriptPath(string relativePath, string preferredPlatformDirectoryName)
    {
        var normalized = relativePath.Replace('\\', '/');
        return normalized.Contains($"/scripts/{preferredPlatformDirectoryName}/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith($"scripts/{preferredPlatformDirectoryName}/", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetPathDepth(string relativePath)
    {
        return relativePath.Count(ch => ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar);
    }

    private static ScriptMergeResult MergePreparedFilesIntoTarget(string preparedDirectoryPath, string targetDirectoryPath)
    {
        Directory.CreateDirectory(targetDirectoryPath);
        var archiveStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var archivedCount = 0;
        var importedCount = 0;
        var skippedCount = 0;

        foreach (var sourceFilePath in Directory.EnumerateFiles(preparedDirectoryPath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(preparedDirectoryPath, sourceFilePath);
            var destinationFilePath = Path.Combine(targetDirectoryPath, relativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            if (File.Exists(destinationFilePath))
            {
                if (FilesMatch(sourceFilePath, destinationFilePath))
                {
                    skippedCount++;
                    continue;
                }

                var archivedPath = BuildArchivedFilePath(destinationFilePath, archiveStamp);
                File.Move(destinationFilePath, archivedPath);
                archivedCount++;
            }

            File.Copy(sourceFilePath, destinationFilePath, overwrite: false);
            importedCount++;
        }

        return new ScriptMergeResult(importedCount, archivedCount, skippedCount);
    }

    private static void ExtractZipSafely(string zipPath, string destinationDirectoryPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);

        foreach (var entry in archive.Entries)
        {
            if (ShouldSkipZipEntry(entry))
            {
                continue;
            }

            var relativePath = entry.FullName.Replace('\\', '/').TrimStart('/');
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            var destinationPath = Path.GetFullPath(Path.Combine(destinationDirectoryPath, relativePath));
            var normalizedDestinationRoot = Path.GetFullPath(destinationDirectoryPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (!destinationPath.StartsWith(normalizedDestinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                entry.FullName.EndsWith("\\", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static string BuildArchivedFilePath(string originalFilePath, string archiveStamp)
    {
        var directory = Path.GetDirectoryName(originalFilePath) ?? string.Empty;
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(originalFilePath);
        var extension = Path.GetExtension(originalFilePath);
        var candidatePath = Path.Combine(directory, $"{fileNameWithoutExtension}.{archiveStamp}{extension}");
        var counter = 1;

        while (File.Exists(candidatePath))
        {
            candidatePath = Path.Combine(directory, $"{fileNameWithoutExtension}.{archiveStamp}.{counter}{extension}");
            counter++;
        }

        return candidatePath;
    }

    private static bool FilesMatch(string sourceFilePath, string destinationFilePath)
    {
        var sourceInfo = new FileInfo(sourceFilePath);
        var destinationInfo = new FileInfo(destinationFilePath);
        if (sourceInfo.Length != destinationInfo.Length)
        {
            return false;
        }

        using var sourceStream = File.OpenRead(sourceFilePath);
        using var destinationStream = File.OpenRead(destinationFilePath);

        const int bufferSize = 81920;
        var sourceBuffer = new byte[bufferSize];
        var destinationBuffer = new byte[bufferSize];

        while (true)
        {
            var sourceRead = sourceStream.Read(sourceBuffer, 0, sourceBuffer.Length);
            var destinationRead = destinationStream.Read(destinationBuffer, 0, destinationBuffer.Length);

            if (sourceRead != destinationRead)
            {
                return false;
            }

            if (sourceRead == 0)
            {
                return true;
            }

            for (var index = 0; index < sourceRead; index++)
            {
                if (sourceBuffer[index] != destinationBuffer[index])
                {
                    return false;
                }
            }
        }
    }

    private static void RemoveIgnoredFiles(string rootPath)
    {
        foreach (var filePath in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            if (IsIgnoredFile(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    private static bool IsSupportedScriptFile(string filePath)
    {
        return !IsIgnoredFile(filePath) && SupportedScriptExtensions.Contains(Path.GetExtension(filePath));
    }

    private static bool IsIgnoredFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return string.Equals(fileName, ".DS_Store", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "Thumbs.db", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "desktop.ini", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "ehthumbs.db", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("._", StringComparison.Ordinal);
    }

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/');

    private static string BuildImportMessage(string targetDirectoryPath, ScriptMergeResult mergeResult)
    {
        if (mergeResult.ImportedCount == 0 && mergeResult.ArchivedCount == 0)
        {
            return $"Script bundle is already current in {targetDirectoryPath}.";
        }

        var parts = new List<string> { $"Imported script bundle to {targetDirectoryPath}" };
        if (mergeResult.ImportedCount > 0)
        {
            parts.Add($"updated {mergeResult.ImportedCount} file(s)");
        }

        if (mergeResult.ArchivedCount > 0)
        {
            parts.Add($"archived {mergeResult.ArchivedCount} existing file(s)");
        }

        if (mergeResult.SkippedCount > 0)
        {
            parts.Add($"left {mergeResult.SkippedCount} unchanged file(s) alone");
        }

        return string.Join(" and ", parts);
    }

    private static bool ShouldSkipZipEntry(ZipArchiveEntry entry)
    {
        var normalizedPath = NormalizeRelativePath(entry.FullName).TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return true;
        }

        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => string.Equals(segment, "__MACOSX", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var fileName = segments.LastOrDefault() ?? string.Empty;
        return string.Equals(fileName, ".DS_Store", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("._", StringComparison.Ordinal);
    }

    private sealed record ScriptMergeResult(int ImportedCount, int ArchivedCount, int SkippedCount);
}
