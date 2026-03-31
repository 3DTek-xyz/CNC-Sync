using CNCSync.Core.Configuration;
using CNCSync.Core.Services;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace CNCSync.Infrastructure.Networking;

public sealed class SftpService : ISftpService
{
    public Task<(bool Success, string Message)> TestConnectionAsync(DestinationSettings destination, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(destination.Host))
            {
                return (false, "SFTP host is not configured.");
            }

            if (string.IsNullOrWhiteSpace(destination.Username))
            {
                return (false, "SFTP username is not configured.");
            }

            if (destination.SshAuthenticationMode == SshAuthenticationMode.Password &&
                string.IsNullOrWhiteSpace(destination.Password))
            {
                return (false, "SFTP password is not configured.");
            }

            if (destination.SshAuthenticationMode == SshAuthenticationMode.PrivateKey &&
                string.IsNullOrWhiteSpace(destination.PrivateKeyPath))
            {
                return (false, "SFTP private key path is not configured.");
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var client = CreateClient(destination);
                client.Connect();
                return (true, $"SFTP connection successful: {destination.Host}:{destination.Port}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (false, $"SFTP connection failed: {ex.Message}");
            }
        }, cancellationToken);

    public Task<(bool Success, string Message)> UploadDirectoryAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default) =>
        UploadFileSystemItemAsync(localPath, destination, remoteDirectoryPath, cancellationToken);

    public Task<(bool Success, string Message)> UploadFileSystemItemAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
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
                using var client = CreateClient(destination);
                client.Connect();

                var targetRoot = NormalizeSessionDirectoryPath(remoteDirectoryPath);
                EnsureDirectoryExists(client, targetRoot, cancellationToken);

                if (isFile)
                {
                    var fileName = Path.GetFileName(localPath);
                    if (FileSystemItemFilter.ShouldIgnoreFileSystemItem(fileName))
                    {
                        return (true, $"Skipped ignored file: {fileName}");
                    }

                    var remoteFilePath = CombineRemoteFilePath(targetRoot, fileName);
                    using var fileStream = File.OpenRead(localPath);
                    client.UploadFile(fileStream, remoteFilePath, true, null);
                }
                else
                {
                    foreach (var file in FileSystemItemFilter.EnumerateIncludedFiles(localPath))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var relativePath = Path.GetRelativePath(localPath, file).Replace('\\', '/');
                        var remoteFilePath = CombineRemoteFilePath(targetRoot, relativePath);
                        var remoteDirectory = GetParentRemotePath(remoteFilePath);
                        EnsureDirectoryExists(client, remoteDirectory, cancellationToken);

                        using var fileStream = File.OpenRead(file);
                        client.UploadFile(fileStream, remoteFilePath, true, null);
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
                return (false, $"SFTP upload failed: {ex.Message}");
            }
        }, cancellationToken);

    public Task<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)> ListRootEntriesAsync(DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default) =>
        Task.Run<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)>(() =>
        {
            if (string.IsNullOrWhiteSpace(destination.Host))
            {
                return (false, [], "SFTP listing skipped because no SFTP host is configured.");
            }

            try
            {
                using var client = CreateClient(destination);
                client.Connect();

                var targetPath = NormalizeSessionDirectoryPath(remoteDirectoryPath);

                if (!client.Exists(targetPath))
                {
                    return (true, [], "SFTP path does not exist yet; treating it as empty.");
                }

                var entries = client.ListDirectory(targetPath)
                    .Where(entry => entry.Name is not "." and not "..")
                    .Select(entry => new RemoteEntryInfo
                    {
                        Name = entry.Name,
                        FullPath = NormalizeRemotePath(entry.FullName),
                        IsDirectory = entry.IsDirectory,
                        SizeBytes = entry.IsDirectory ? null : entry.Attributes.Size
                    })
                    .OrderByDescending(entry => entry.IsDirectory)
                    .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var describedPath = DescribeRemotePath(targetPath);
                return (true, entries, $"Listed {entries.Count} item(s) from SFTP path {describedPath}.");
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
                return (false, [], $"SFTP listing failed: {ex.Message}");
            }
        }, cancellationToken);

    public Task<(bool Exists, long? SizeBytes, string Message)> TryGetFileSizeAsync(DestinationSettings destination, string remoteFilePath, CancellationToken cancellationToken = default) =>
        Task.Run<(bool Exists, long? SizeBytes, string Message)>(() =>
        {
            try
            {
                using var client = CreateClient(destination);
                client.Connect();

                var normalizedPath = NormalizeSessionPath(remoteFilePath);
                if (!client.Exists(normalizedPath))
                {
                    return (false, null, $"Remote file does not exist: {DescribeRemotePath(normalizedPath)}");
                }

                var attributes = client.GetAttributes(normalizedPath);
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
                return (false, null, $"Could not read SFTP file size: {ex.Message}");
            }
        }, cancellationToken);

    public Task<(bool Success, string Message)> DeleteRemoteItemAsync(DestinationSettings destination, string remotePath, bool isDirectory, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(destination.Host))
            {
                return (false, "SFTP delete skipped because no SFTP host is configured.");
            }

            try
            {
                using var client = CreateClient(destination);
                client.Connect();

                var normalizedPath = NormalizeSessionPath(remotePath);
                if (normalizedPath == ".")
                {
                    return (true, "Remote root cleanup skipped.");
                }

                if (isDirectory)
                {
                    DeleteDirectoryRecursive(client, normalizedPath, cancellationToken);
                }
                else if (client.Exists(normalizedPath))
                {
                    client.DeleteFile(normalizedPath);
                }

                return (true, $"{(isDirectory ? "Remote folder" : "Remote file")} deleted: {DescribeRemotePath(normalizedPath)}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (false, $"SFTP delete failed: {ex.Message}");
            }
        }, cancellationToken);

    private static SftpClient CreateClient(DestinationSettings destination)
    {
        return new SftpClient(SshConnectionFactory.CreateConnectionInfo(destination));
    }

    private static void EnsureDirectoryExists(SftpClient client, string? remotePath, CancellationToken cancellationToken)
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

            if (!client.Exists(currentPath))
            {
                client.CreateDirectory(currentPath);
            }
        }
    }

    private static void DeleteDirectoryRecursive(SftpClient client, string remotePath, CancellationToken cancellationToken)
    {
        if (!client.Exists(remotePath))
        {
            return;
        }

        foreach (var entry in client.ListDirectory(remotePath).Where(entry => entry.Name is not "." and not ".."))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.IsDirectory)
            {
                DeleteDirectoryRecursive(client, NormalizeRemotePath(entry.FullName), cancellationToken);
            }
            else
            {
                client.DeleteFile(NormalizeRemotePath(entry.FullName));
            }
        }

        client.DeleteDirectory(remotePath);
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
