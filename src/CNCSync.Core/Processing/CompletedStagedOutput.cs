using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using CNCSync.Core.Services;

namespace CNCSync.Core.Processing;

public sealed class CompletedStagedOutput
{
    private const string JobPrefix = "job-";
    private const string MarkerName = "complete.json";
    private const int FormatVersion = 1;

    private CompletedStagedOutput(string jobDirectory, string outputPath, Manifest manifest)
    {
        JobDirectory = jobDirectory;
        OutputPath = outputPath;
        ProfileId = manifest.ProfileId;
        SourcePath = manifest.SourcePath;
        SourceFingerprint = manifest.SourceFingerprint;
        RemoteFolderName = manifest.RemoteFolderName;
        RemoteRelativeParent = manifest.RemoteRelativeParent;
        CompletedAtUtc = manifest.CompletedAtUtc;
        ProcessedFiles = Array.AsReadOnly(manifest.Files.Select(file => file.Path).ToArray());
    }

    public string JobDirectory { get; }
    public string ProfileId { get; }
    public string SourcePath { get; }
    public string? SourceFingerprint { get; }
    public string OutputPath { get; }
    public string? RemoteFolderName { get; }
    public string? RemoteRelativeParent { get; }
    public IReadOnlyList<string> ProcessedFiles { get; }
    public DateTime CompletedAtUtc { get; }

    public static string CreateJobDirectory(string stagingRoot) =>
        Directory.CreateDirectory(Path.Combine(Path.GetFullPath(stagingRoot), $"{JobPrefix}{Guid.NewGuid():N}")).FullName;

    public static CompletedStagedOutput Publish(
        string jobDirectory,
        string profileId,
        string sourcePath,
        string outputPath,
        string? remoteFolderName,
        string? sourceFingerprint = null,
        CancellationToken cancellationToken = default,
        string? remoteRelativeParent = null)
    {
        jobDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(jobDirectory));
        var relativePath = Path.GetRelativePath(jobDirectory, outputPath);
        outputPath = ResolveOutputPath(jobDirectory, relativePath);
        if (remoteFolderName is not null && !IsPathSegment(remoteFolderName))
        {
            throw new InvalidDataException("Invalid staged output remote folder name.");
        }

        if (!IsRemoteRelativeParent(remoteRelativeParent))
        {
            throw new InvalidDataException("Invalid staged output remote parent path.");
        }

        var files = ReadInventory(outputPath, cancellationToken);
        var manifest = new Manifest(FormatVersion, profileId, Path.GetFullPath(sourcePath), sourceFingerprint,
            relativePath, remoteFolderName, DateTime.UtcNow, files, remoteRelativeParent);
        var temporaryMarker = Path.Combine(jobDirectory, $".{Guid.NewGuid():N}.tmp");
        var markerCreated = false;
        try
        {
            using (var stream = new FileStream(temporaryMarker, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                markerCreated = true;
                JsonSerializer.Serialize(stream, manifest);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryMarker, Path.Combine(jobDirectory, MarkerName));
            markerCreated = false;
        }
        finally
        {
            if (markerCreated)
            {
                try
                {
                    File.Delete(temporaryMarker);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Trace.TraceWarning($"Could not remove temporary staging marker {temporaryMarker}: {ex.Message}");
                }
            }
        }

        return new CompletedStagedOutput(jobDirectory, outputPath, manifest);
    }

    public static IEnumerable<CompletedStagedOutput> Enumerate(
        string stagingRoot, string profileId, CancellationToken cancellationToken = default,
        string? sourcePath = null, string? sourceFingerprint = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(stagingRoot))
        {
            yield break;
        }

        foreach (var directory in Directory.EnumerateDirectories(stagingRoot, $"{JobPrefix}*"))
        {
            var output = TryReadCore(directory, profileId, cancellationToken, sourcePath, sourceFingerprint);
            if (output is not null)
            {
                yield return output;
            }
        }
    }

    public static CompletedStagedOutput? TryRead(
        string jobDirectory, string profileId, CancellationToken cancellationToken = default) =>
        TryReadCore(jobDirectory, profileId, cancellationToken, null, null);

    private static CompletedStagedOutput? TryReadCore(
        string jobDirectory, string profileId, CancellationToken cancellationToken, string? sourcePath, string? sourceFingerprint)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            jobDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(jobDirectory));
            ValidateJobDirectory(jobDirectory);
            var marker = Path.Combine(jobDirectory, MarkerName);
            if (GetSafeAttributes(marker).HasFlag(FileAttributes.Directory))
            {
                return null;
            }

            using var stream = File.OpenRead(marker);
            var manifest = JsonSerializer.Deserialize<Manifest>(stream);
            if (manifest is null || manifest.Version != FormatVersion ||
                !string.Equals(manifest.ProfileId, profileId, StringComparison.OrdinalIgnoreCase) ||
                !Path.IsPathFullyQualified(manifest.SourcePath ?? "") || manifest.Files is null ||
                (sourcePath is not null && Path.GetRelativePath(sourcePath, manifest.SourcePath!) != ".") ||
                (sourceFingerprint is not null && !string.Equals(sourceFingerprint, manifest.SourceFingerprint, StringComparison.Ordinal)) ||
                (manifest.RemoteFolderName is not null && !IsPathSegment(manifest.RemoteFolderName)) ||
                !IsRemoteRelativeParent(manifest.RemoteRelativeParent))
            {
                return null;
            }

            var outputPath = ResolveOutputPath(jobDirectory, manifest.OutputRelativePath);
            return ReadInventory(outputPath, cancellationToken).SequenceEqual(manifest.Files)
                ? new CompletedStagedOutput(jobDirectory, outputPath, manifest)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException)
        {
            return null;
        }
    }

    public void Delete()
    {
        ValidateJobDirectory(JobDirectory);
        File.Delete(Path.Combine(JobDirectory, MarkerName));
        Directory.Delete(JobDirectory, recursive: true);
    }

    private static string ResolveOutputPath(string jobDirectory, string? relativePath)
    {
        ValidateJobDirectory(jobDirectory);
        var segments = relativePath?.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments is null || !segments.All(IsPathSegment) ||
            string.Equals(segments[0], MarkerName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Staged output is not contained in its job directory.");
        }

        var path = jobDirectory;
        foreach (var segment in segments)
        {
            path = Path.Combine(path, segment);
            GetSafeAttributes(path);
        }

        return path;
    }

    private static void ValidateJobDirectory(string path)
    {
        var name = Path.GetFileName(path);
        if (!name.StartsWith(JobPrefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(name[JobPrefix.Length..], "N", out _) ||
            !GetSafeAttributes(path).HasFlag(FileAttributes.Directory))
        {
            throw new InvalidDataException("Invalid staged job directory.");
        }
    }

    private static bool IsPathSegment(string name) =>
        !string.IsNullOrWhiteSpace(name) && name is not "." and not ".." &&
        !name.EndsWith('.') && !name.EndsWith(' ') && name.IndexOfAny(['/', '\\', ':']) < 0 &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.Any(char.IsControl);

    private static bool IsRemoteRelativeParent(string? path) => path is null || path.Split('/').All(segment =>
        IsPathSegment(segment) && segment.Trim() == segment && segment.IndexOfAny(['<', '>', '"', '|', '?', '*']) < 0);

    private static FileAttributes GetSafeAttributes(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
        {
            throw new InvalidDataException("Staged jobs cannot reference symbolic links or devices.");
        }

        return attributes;
    }

    private static FileDigest[] ReadInventory(string outputPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!GetSafeAttributes(outputPath).HasFlag(FileAttributes.Directory))
        {
            var fileName = Path.GetFileName(outputPath);
            if (FileSystemItemFilter.ShouldIgnoreFileSystemItem(fileName))
            {
                throw new InvalidDataException("The staged output filename is excluded from delivery.");
            }

            return [ReadDigest(outputPath, fileName, cancellationToken)];
        }

        var files = new List<FileDigest>();
        var directories = new Stack<string>();
        directories.Push(outputPath);
        while (directories.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = GetSafeAttributes(entry);
                if (FileSystemItemFilter.ShouldIgnoreFileSystemItem(Path.GetFileName(entry)))
                {
                    continue;
                }

                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    directories.Push(entry);
                }
                else
                {
                    files.Add(ReadDigest(entry, Path.GetRelativePath(outputPath, entry), cancellationToken));
                }
            }
        }

        return files.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
    }

    private static FileDigest ReadDigest(string fullPath, string relativePath, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(fullPath);
        var digest = SHA256.HashDataAsync(stream, cancellationToken).AsTask().GetAwaiter().GetResult();
        return new FileDigest(relativePath, Convert.ToHexString(digest));
    }

    private sealed record Manifest(int Version, string ProfileId, string SourcePath, string? SourceFingerprint,
        string OutputRelativePath, string? RemoteFolderName, DateTime CompletedAtUtc, FileDigest[] Files, string? RemoteRelativeParent);

    private sealed record FileDigest(string Path, string Sha256);
}
