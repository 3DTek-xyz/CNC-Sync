using CNCSync.Core.Configuration;

namespace CNCSync.Core.Services;

public interface IFtpService
{
    Task<(bool Success, string Message)> TestConnectionAsync(FtpDestinationSettings destination, CancellationToken cancellationToken = default);
    Task<(bool Success, string Message)> UploadDirectoryAsync(string localPath, FtpDestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default);
    Task<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)> ListRootEntriesAsync(FtpDestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default);
    Task<(bool Exists, long? SizeBytes, string Message)> TryGetFileSizeAsync(FtpDestinationSettings destination, string remoteFilePath, CancellationToken cancellationToken = default);
    Task<(bool Success, string Message)> DeleteRemoteItemAsync(FtpDestinationSettings destination, string remotePath, bool isDirectory, CancellationToken cancellationToken = default);
}
