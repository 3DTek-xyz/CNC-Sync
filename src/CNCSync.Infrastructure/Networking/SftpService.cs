using CNCSync.Core.Configuration;
using CNCSync.Core.Services;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace CNCSync.Infrastructure.Networking;

public sealed class SftpService : ISftpService
{
    public Task<(bool Success, string Message)> TestConnectionAsync(DestinationSettings destination, CancellationToken cancellationToken = default) =>
        Task.Run(() => SshConnectionFactory.TestConnectionAsync(
            destination, "SFTP", SshConnectionFactory.ConnectSftpAsync, cancellationToken), cancellationToken);

    public Task<(bool Success, string Message)> UploadDirectoryAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        UploadFileSystemItemAsync(localPath, destination, remoteDirectoryPath, progress, cancellationToken);

    public Task<(bool Success, string Message)> UploadFileSystemItemAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            if (string.IsNullOrWhiteSpace(destination.Host))
            {
                return (false, "SFTP upload skipped because no SFTP host is configured.");
            }

            var isDirectory = Directory.Exists(localPath);
            var isFile = File.Exists(localPath);
            if (!isDirectory && !isFile)
            {
                return (false, $"SFTP upload skipped because local path does not exist: {localPath}");
            }

            try
            {
                using var client = await SshConnectionFactory.ConnectSftpAsync(destination, cancellationToken);

                var targetRoot = NormalizeSessionDirectoryPath(remoteDirectoryPath);
                await EnsureDirectoryExistsAsync(client, targetRoot, cancellationToken);

                if (isFile)
                {
                    var fileName = Path.GetFileName(localPath);
                    if (FileSystemItemFilter.ShouldIgnoreFileSystemItem(fileName))
                    {
                        return (true, $"Skipped ignored file: {fileName}");
                    }

                    var remoteFilePath = CombineRemoteFilePath(targetRoot, fileName);
                    progress?.Report($"Uploading {fileName}...");
                    using var fileStream = File.OpenRead(localPath);
                    await client.UploadFileAsync(fileStream, remoteFilePath, cancellationToken);
                }
                else
                {
                    var files = FileSystemItemFilter.EnumerateIncludedFiles(localPath).ToList();
                    for (var index = 0; index < files.Count; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var file = files[index];

                        var relativePath = Path.GetRelativePath(localPath, file).Replace('\\', '/');
                        var remoteFilePath = CombineRemoteFilePath(targetRoot, relativePath);
                        var remoteDirectory = GetParentRemotePath(remoteFilePath);
                        await EnsureDirectoryExistsAsync(client, remoteDirectory, cancellationToken);

                        progress?.Report($"Uploading {index + 1}/{files.Count}: {relativePath}");
                        using var fileStream = File.OpenRead(file);
                        await client.UploadFileAsync(fileStream, remoteFilePath, cancellationToken);
                    }
                }

                return (true, $"SFTP upload completed to {DescribeRemotePath(targetRoot)}: {localPath}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return (false, $"SFTP upload failed: {ex.Message}");
            }
        }, cancellationToken);

    public Task<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)> ListRootEntriesAsync(DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default) =>
        Task.Run<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)>(async () =>
        {
            if (string.IsNullOrWhiteSpace(destination.Host))
            {
                return (false, [], "SFTP listing skipped because no SFTP host is configured.");
            }

            try
            {
                using var client = await SshConnectionFactory.ConnectSftpAsync(destination, cancellationToken);

                var targetPath = NormalizeSessionDirectoryPath(remoteDirectoryPath);

                if (!await client.ExistsAsync(targetPath, cancellationToken))
                {
                    return (true, [], "SFTP path does not exist yet; treating it as empty.");
                }

                var entries = new List<RemoteEntryInfo>();
                await foreach (var entry in client.ListDirectoryAsync(targetPath, cancellationToken))
                {
                    if (entry.Name is "." or "..")
                    {
                        continue;
                    }

                    entries.Add(new RemoteEntryInfo
                    {
                        Name = entry.Name,
                        FullPath = NormalizeRemotePath(entry.FullName),
                        IsDirectory = entry.IsDirectory,
                        SizeBytes = entry.IsDirectory ? null : entry.Attributes.Size
                    });
                }

                var sortedEntries = entries
                    .OrderByDescending(entry => entry.IsDirectory)
                    .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var describedPath = DescribeRemotePath(targetPath);
                return (true, sortedEntries, $"Listed {entries.Count} item(s) from SFTP path {describedPath}.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SftpPathNotFoundException)
            {
                return (true, [], "SFTP path does not exist yet; treating it as empty.");
            }
            catch (Exception ex)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return (false, [], $"SFTP listing failed: {ex.Message}");
            }
        }, cancellationToken);

    public Task<(bool Exists, long? SizeBytes, string Message)> TryGetFileSizeAsync(DestinationSettings destination, string remoteFilePath, CancellationToken cancellationToken = default) =>
        Task.Run<(bool Exists, long? SizeBytes, string Message)>(async () =>
        {
            try
            {
                using var client = await SshConnectionFactory.ConnectSftpAsync(destination, cancellationToken);

                var normalizedPath = NormalizeSessionPath(remoteFilePath);
                if (!await client.ExistsAsync(normalizedPath, cancellationToken))
                {
                    return (false, null, $"Remote file does not exist: {DescribeRemotePath(normalizedPath)}");
                }

                var attributes = await client.GetAttributesAsync(normalizedPath, cancellationToken);
                if (attributes.IsDirectory)
                {
                    return (false, null, $"Remote path is a directory: {DescribeRemotePath(normalizedPath)}");
                }

                return (true, (long?)attributes.Size, $"Remote file exists: {DescribeRemotePath(normalizedPath)}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return (false, null, $"Could not read SFTP file size: {ex.Message}");
            }
        }, cancellationToken);

    public Task<(bool Success, string Message)> DeleteRemoteItemAsync(DestinationSettings destination, string remotePath, bool isDirectory, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            if (string.IsNullOrWhiteSpace(destination.Host))
            {
                return (false, "SFTP delete skipped because no SFTP host is configured.");
            }

            try
            {
                using var client = await SshConnectionFactory.ConnectSftpAsync(destination, cancellationToken);

                var normalizedPath = NormalizeSessionPath(remotePath);
                if (normalizedPath == ".")
                {
                    return (true, "Remote root cleanup skipped.");
                }

                if (isDirectory)
                {
                    await DeleteDirectoryRecursiveAsync(client, normalizedPath, cancellationToken);
                }
                else if (await client.ExistsAsync(normalizedPath, cancellationToken))
                {
                    await client.DeleteFileAsync(normalizedPath, cancellationToken);
                }

                return (true, $"{(isDirectory ? "Remote folder" : "Remote file")} deleted: {DescribeRemotePath(normalizedPath)}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return (false, $"SFTP delete failed: {ex.Message}");
            }
        }, cancellationToken);

    private static async Task EnsureDirectoryExistsAsync(SftpClient client, string? remotePath, CancellationToken cancellationToken)
    {
        var normalizedPath = NormalizeSessionDirectoryPath(remotePath);
        if (string.IsNullOrWhiteSpace(normalizedPath) || normalizedPath is "/" or ".")
        {
            return;
        }

        var currentPath = string.Empty;
        foreach (var segment in normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            currentPath = string.IsNullOrWhiteSpace(currentPath) ? $"/{segment}" : $"{currentPath}/{segment}";

            if (!await client.ExistsAsync(currentPath, cancellationToken))
            {
                await client.CreateDirectoryAsync(currentPath, cancellationToken);
            }
        }
    }

    private static async Task DeleteDirectoryRecursiveAsync(SftpClient client, string remotePath, CancellationToken cancellationToken)
    {
        if (!await client.ExistsAsync(remotePath, cancellationToken))
        {
            return;
        }

        await foreach (var entry in client.ListDirectoryAsync(remotePath, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Name is "." or "..")
            {
                continue;
            }

            if (entry.IsDirectory)
            {
                await DeleteDirectoryRecursiveAsync(client, NormalizeRemotePath(entry.FullName), cancellationToken);
            }
            else
            {
                await client.DeleteFileAsync(NormalizeRemotePath(entry.FullName), cancellationToken);
            }
        }

        await client.DeleteDirectoryAsync(remotePath, cancellationToken);
    }

    private static string NormalizeSessionDirectoryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ".";
        }

        return NormalizeRemotePath(path);
    }

    private static string NormalizeSessionPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ".";
        }

        return NormalizeRemotePath(path);
    }

    private static string CombineRemoteFilePath(string basePath, string relativePath)
    {
        var normalizedRelativePath = relativePath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(basePath) || basePath == ".")
        {
            return normalizedRelativePath;
        }

        return CombineRemotePath(basePath, normalizedRelativePath);
    }

    private static string DescribeRemotePath(string path) =>
        string.IsNullOrWhiteSpace(path) || path == "." ? "/" : path;

    private static string NormalizeRemotePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path.Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return "/" + normalized.Trim('/');
    }

    private static string CombineRemotePath(string? basePath, string? childPath)
    {
        var segments = new[] { basePath, childPath }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var combined = string.Join("/", segments);
        return string.IsNullOrWhiteSpace(combined) ? string.Empty : $"/{combined}";
    }

    private static string GetParentRemotePath(string remotePath)
    {
        var normalized = NormalizeRemotePath(remotePath);
        if (string.IsNullOrWhiteSpace(normalized) || normalized == "/")
        {
            return "/";
        }

        var slashIndex = normalized.LastIndexOf('/');
        return slashIndex <= 0 ? "/" : normalized[..slashIndex];
    }
}
