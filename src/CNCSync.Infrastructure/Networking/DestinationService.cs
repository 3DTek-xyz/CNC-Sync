using CNCSync.Core.Configuration;
using CNCSync.Core.Services;

namespace CNCSync.Infrastructure.Networking;

public sealed class DestinationService : IDestinationService
{
    private readonly IFtpService _ftpService;
    private readonly ISftpService _sftpService;
    private readonly IScpService _scpService;
    private readonly INetworkShareService _networkShareService;

    public DestinationService(IFtpService ftpService, ISftpService sftpService, IScpService scpService, INetworkShareService networkShareService)
    {
        _ftpService = ftpService;
        _sftpService = sftpService;
        _scpService = scpService;
        _networkShareService = networkShareService;
    }

    public Task<(bool Success, string Message)> TestConnectionAsync(DestinationSettings destination, CancellationToken cancellationToken = default)
    {
        return destination.Type switch
        {
            DestinationType.LocalFolder => Task.FromResult(TestLocalDestination(destination)),
            DestinationType.NetworkShare => _networkShareService.TestConnectionAsync(destination, cancellationToken),
            DestinationType.Sftp => _sftpService.TestConnectionAsync(destination, cancellationToken),
            DestinationType.Scp => _scpService.TestConnectionAsync(destination, cancellationToken),
            _ => _ftpService.TestConnectionAsync(destination, cancellationToken)
        };
    }

    public Task<(bool Success, string Message)> UploadDirectoryAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default)
    {
        return destination.Type switch
        {
            DestinationType.LocalFolder => UploadToLocalFolderAsync(localPath, destination, remoteDirectoryPath, cancellationToken),
            DestinationType.NetworkShare => _networkShareService.UploadDirectoryAsync(localPath, destination, remoteDirectoryPath, cancellationToken),
            DestinationType.Sftp => _sftpService.UploadDirectoryAsync(localPath, destination, remoteDirectoryPath, cancellationToken),
            DestinationType.Scp => _scpService.UploadDirectoryAsync(localPath, destination, remoteDirectoryPath, cancellationToken),
            _ => _ftpService.UploadDirectoryAsync(localPath, destination, remoteDirectoryPath, cancellationToken)
        };
    }

    public Task<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)> ListRootEntriesAsync(DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default)
    {
        return destination.Type switch
        {
            DestinationType.LocalFolder => Task.FromResult(ListLocalEntries(destination, remoteDirectoryPath)),
            DestinationType.NetworkShare => _networkShareService.ListRootEntriesAsync(destination, remoteDirectoryPath, cancellationToken),
            DestinationType.Sftp => _sftpService.ListRootEntriesAsync(destination, remoteDirectoryPath, cancellationToken),
            DestinationType.Scp => _scpService.ListRootEntriesAsync(destination, remoteDirectoryPath, cancellationToken),
            _ => _ftpService.ListRootEntriesAsync(destination, remoteDirectoryPath, cancellationToken)
        };
    }

    public Task<(bool Exists, long? SizeBytes, string Message)> TryGetFileSizeAsync(DestinationSettings destination, string remoteFilePath, CancellationToken cancellationToken = default)
    {
        return destination.Type switch
        {
            DestinationType.LocalFolder => Task.FromResult(GetLocalFileSize(destination, remoteFilePath)),
            DestinationType.NetworkShare => _networkShareService.TryGetFileSizeAsync(destination, remoteFilePath, cancellationToken),
            DestinationType.Sftp => _sftpService.TryGetFileSizeAsync(destination, remoteFilePath, cancellationToken),
            DestinationType.Scp => _scpService.TryGetFileSizeAsync(destination, remoteFilePath, cancellationToken),
            _ => _ftpService.TryGetFileSizeAsync(destination, remoteFilePath, cancellationToken)
        };
    }

    public Task<(bool Success, string Message)> DeleteRemoteItemAsync(DestinationSettings destination, string remotePath, bool isDirectory, CancellationToken cancellationToken = default)
    {
        return destination.Type switch
        {
            DestinationType.LocalFolder => Task.FromResult(DeleteLocalItem(destination, remotePath, isDirectory)),
            DestinationType.NetworkShare => _networkShareService.DeleteRemoteItemAsync(destination, remotePath, isDirectory, cancellationToken),
            DestinationType.Sftp => _sftpService.DeleteRemoteItemAsync(destination, remotePath, isDirectory, cancellationToken),
            DestinationType.Scp => _scpService.DeleteRemoteItemAsync(destination, remotePath, isDirectory, cancellationToken),
            _ => _ftpService.DeleteRemoteItemAsync(destination, remotePath, isDirectory, cancellationToken)
        };
    }

    private static (bool Success, string Message) TestLocalDestination(DestinationSettings destination)
    {
        if (string.IsNullOrWhiteSpace(destination.LocalRootPath))
        {
            return (false, "Local destination test skipped because no destination folder is configured.");
        }

        try
        {
            Directory.CreateDirectory(destination.LocalRootPath);
            return (true, $"Local destination is ready: {destination.LocalRootPath}");
        }
        catch (Exception ex)
        {
            return (false, $"Local destination test failed: {ex.Message}");
        }
    }

    private static async Task<(bool Success, string Message)> UploadToLocalFolderAsync(
        string localPath,
        DestinationSettings destination,
        string remoteDirectoryPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(destination.LocalRootPath))
        {
            return (false, "Local upload skipped because no destination folder is configured.");
        }

        if (!Directory.Exists(localPath))
        {
            return (false, $"Local upload skipped because source path does not exist: {localPath}");
        }

        try
        {
            var targetRoot = CombineLocalPath(destination.LocalRootPath, remoteDirectoryPath);
            Directory.CreateDirectory(targetRoot);

            foreach (var file in FileSystemItemFilter.EnumerateIncludedFiles(localPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(localPath, file);
                var destinationFile = Path.Combine(targetRoot, relativePath);
                var destinationDirectory = Path.GetDirectoryName(destinationFile);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                File.Copy(file, destinationFile, overwrite: true);
            }

            return (true, $"Local upload completed to {targetRoot}: {localPath}");
        }
        catch (Exception ex)
        {
            return (false, $"Local upload failed: {ex.Message}");
        }
    }

    private static (bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message) ListLocalEntries(DestinationSettings destination, string remoteDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(destination.LocalRootPath))
        {
            return (false, [], "Local browser skipped because no destination folder is configured.");
        }

        try
        {
            var rootPath = CombineLocalPath(destination.LocalRootPath, remoteDirectoryPath);
            if (!Directory.Exists(rootPath))
            {
                return (true, [], "Local destination path does not exist yet; treating it as empty.");
            }

            var entries = Directory
                .EnumerateFileSystemEntries(rootPath, "*", SearchOption.TopDirectoryOnly)
                .Where(path => !FileSystemItemFilter.ShouldIgnoreFileSystemItem(Path.GetFileName(path)))
                .Select(path =>
                {
                    var isDirectory = Directory.Exists(path);
                    return new RemoteEntryInfo
                    {
                        Name = Path.GetFileName(path),
                        FullPath = CombineRemotePath(remoteDirectoryPath, Path.GetFileName(path)),
                        IsDirectory = isDirectory,
                        SizeBytes = isDirectory ? null : new FileInfo(path).Length
                    };
                })
                .ToList();

            return (true, entries, $"Listed {entries.Count} item(s) from local destination path {rootPath}.");
        }
        catch (Exception ex)
        {
            return (false, [], $"Local destination browser failed: {ex.Message}");
        }
    }

    private static (bool Exists, long? SizeBytes, string Message) GetLocalFileSize(DestinationSettings destination, string remoteFilePath)
    {
        if (string.IsNullOrWhiteSpace(destination.LocalRootPath))
        {
            return (false, null, "Local destination folder is not configured.");
        }

        try
        {
            var fullPath = CombineLocalPath(destination.LocalRootPath, remoteFilePath);
            if (!File.Exists(fullPath))
            {
                return (false, null, $"Local destination file does not exist: {fullPath}");
            }

            return (true, new FileInfo(fullPath).Length, $"Local destination file exists: {fullPath}");
        }
        catch (Exception ex)
        {
            return (false, null, $"Could not read local destination file size: {ex.Message}");
        }
    }

    private static (bool Success, string Message) DeleteLocalItem(DestinationSettings destination, string remotePath, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(destination.LocalRootPath))
        {
            return (false, "Local delete skipped because no destination folder is configured.");
        }

        try
        {
            var fullPath = CombineLocalPath(destination.LocalRootPath, remotePath);
            if (isDirectory)
            {
                if (Directory.Exists(fullPath))
                {
                    Directory.Delete(fullPath, recursive: true);
                }
            }
            else if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            return (true, $"{(isDirectory ? "Local folder" : "Local file")} deleted: {fullPath}");
        }
        catch (Exception ex)
        {
            return (false, $"Local delete failed: {ex.Message}");
        }
    }

    private static string CombineLocalPath(string rootPath, string relativePath)
    {
        var segments = relativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Aggregate(rootPath, Path.Combine);
    }

    private static string CombineRemotePath(string basePath, string name)
    {
        var segments = new[] { basePath, name }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var combined = string.Join("/", segments);
        return string.IsNullOrWhiteSpace(combined) ? string.Empty : $"/{combined}";
    }
}
