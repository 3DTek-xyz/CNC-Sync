using CNCSync.Core.Configuration;
using CNCSync.Core.Services;

namespace CNCSync.Infrastructure.Networking;

public sealed class DestinationService : IDestinationService
{
    private readonly IFtpService _ftpService;
    private readonly ISftpService _sftpService;
    private readonly IScpService _scpService;
    private readonly INetworkShareService _networkShareService;
    private readonly IVpnService _vpnService;

    public DestinationService(IFtpService ftpService, ISftpService sftpService, IScpService scpService, INetworkShareService networkShareService, IVpnService vpnService)
    {
        _ftpService = ftpService;
        _sftpService = sftpService;
        _scpService = scpService;
        _networkShareService = networkShareService;
        _vpnService = vpnService;
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(DestinationSettings destination, CancellationToken cancellationToken = default)
    {
        var vpnResult = await EnsureVpnConnectedIfRequiredAsync(destination, cancellationToken);
        if (!vpnResult.Success)
        {
            return (false, vpnResult.Message);
        }

        var result = destination.Type switch
        {
            DestinationType.LocalFolder => TestLocalDestination(destination),
            DestinationType.NetworkShare => await _networkShareService.TestConnectionAsync(destination, cancellationToken),
            DestinationType.Sftp => await _sftpService.TestConnectionAsync(destination, cancellationToken),
            DestinationType.Scp => await _scpService.TestConnectionAsync(destination, cancellationToken),
            _ => await _ftpService.TestConnectionAsync(destination, cancellationToken)
        };

        return DecorateWithVpnMessage(result, vpnResult);
    }

    public async Task<(bool Success, string Message)> UploadDirectoryAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default)
    {
        var vpnResult = await EnsureVpnConnectedIfRequiredAsync(destination, cancellationToken);
        if (!vpnResult.Success)
        {
            return (false, vpnResult.Message);
        }

        var result = destination.Type switch
        {
            DestinationType.LocalFolder => await UploadToLocalFolderAsync(localPath, destination, remoteDirectoryPath, cancellationToken),
            DestinationType.NetworkShare => await _networkShareService.UploadDirectoryAsync(localPath, destination, remoteDirectoryPath, cancellationToken),
            DestinationType.Sftp => await _sftpService.UploadDirectoryAsync(localPath, destination, remoteDirectoryPath, cancellationToken),
            DestinationType.Scp => await _scpService.UploadDirectoryAsync(localPath, destination, remoteDirectoryPath, cancellationToken),
            _ => await _ftpService.UploadDirectoryAsync(localPath, destination, remoteDirectoryPath, cancellationToken)
        };

        return DecorateWithVpnMessage(result, vpnResult);
    }

    public async Task<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)> ListRootEntriesAsync(DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default)
    {
        var vpnResult = await EnsureVpnConnectedIfRequiredAsync(destination, cancellationToken);
        if (!vpnResult.Success)
        {
            return (false, [], vpnResult.Message);
        }

        var result = destination.Type switch
        {
            DestinationType.LocalFolder => ListLocalEntries(destination, remoteDirectoryPath),
            DestinationType.NetworkShare => await _networkShareService.ListRootEntriesAsync(destination, remoteDirectoryPath, cancellationToken),
            DestinationType.Sftp => await _sftpService.ListRootEntriesAsync(destination, remoteDirectoryPath, cancellationToken),
            DestinationType.Scp => await _scpService.ListRootEntriesAsync(destination, remoteDirectoryPath, cancellationToken),
            _ => await _ftpService.ListRootEntriesAsync(destination, remoteDirectoryPath, cancellationToken)
        };

        return (result.Success, result.Entries, DecorateMessageWithVpn(result.Message, vpnResult));
    }

    public async Task<(bool Exists, long? SizeBytes, string Message)> TryGetFileSizeAsync(DestinationSettings destination, string remoteFilePath, CancellationToken cancellationToken = default)
    {
        var vpnResult = await EnsureVpnConnectedIfRequiredAsync(destination, cancellationToken);
        if (!vpnResult.Success)
        {
            return (false, null, vpnResult.Message);
        }

        var result = destination.Type switch
        {
            DestinationType.LocalFolder => GetLocalFileSize(destination, remoteFilePath),
            DestinationType.NetworkShare => await _networkShareService.TryGetFileSizeAsync(destination, remoteFilePath, cancellationToken),
            DestinationType.Sftp => await _sftpService.TryGetFileSizeAsync(destination, remoteFilePath, cancellationToken),
            DestinationType.Scp => await _scpService.TryGetFileSizeAsync(destination, remoteFilePath, cancellationToken),
            _ => await _ftpService.TryGetFileSizeAsync(destination, remoteFilePath, cancellationToken)
        };

        return (result.Exists, result.SizeBytes, DecorateMessageWithVpn(result.Message, vpnResult));
    }

    public async Task<(bool Success, string Message)> DeleteRemoteItemAsync(DestinationSettings destination, string remotePath, bool isDirectory, CancellationToken cancellationToken = default)
    {
        var vpnResult = await EnsureVpnConnectedIfRequiredAsync(destination, cancellationToken);
        if (!vpnResult.Success)
        {
            return (false, vpnResult.Message);
        }

        var result = destination.Type switch
        {
            DestinationType.LocalFolder => DeleteLocalItem(destination, remotePath, isDirectory),
            DestinationType.NetworkShare => await _networkShareService.DeleteRemoteItemAsync(destination, remotePath, isDirectory, cancellationToken),
            DestinationType.Sftp => await _sftpService.DeleteRemoteItemAsync(destination, remotePath, isDirectory, cancellationToken),
            DestinationType.Scp => await _scpService.DeleteRemoteItemAsync(destination, remotePath, isDirectory, cancellationToken),
            _ => await _ftpService.DeleteRemoteItemAsync(destination, remotePath, isDirectory, cancellationToken)
        };

        return DecorateWithVpnMessage(result, vpnResult);
    }

    private async Task<VpnConnectionEnsureResult> EnsureVpnConnectedIfRequiredAsync(DestinationSettings destination, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(destination.RequiredVpnConnectionName))
        {
            return VpnConnectionEnsureResult.NoRequirement();
        }

        return await _vpnService.EnsureConnectedAsync(destination.RequiredVpnConnectionName, cancellationToken);
    }

    private static (bool Success, string Message) DecorateWithVpnMessage((bool Success, string Message) result, VpnConnectionEnsureResult vpnResult) =>
        (result.Success, DecorateMessageWithVpn(result.Message, vpnResult));

    private static string DecorateMessageWithVpn(string message, VpnConnectionEnsureResult vpnResult) =>
        vpnResult.ConnectionStateChanged && !string.IsNullOrWhiteSpace(vpnResult.Message)
            ? $"{vpnResult.Message} {message}".Trim()
            : message;

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
