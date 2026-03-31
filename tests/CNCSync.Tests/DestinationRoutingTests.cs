using CNCSync.Core.Configuration;
using CNCSync.Core.Services;
using CNCSync.Infrastructure.Networking;

namespace CNCSync.Tests;

public sealed class DestinationRoutingTests
{
    [Fact]
    public async Task ScpDestination_RoutesOperationsToScpService()
    {
        var ftpService = new TrackingFtpService();
        var sftpService = new TrackingSftpService();
        var scpService = new TrackingScpService();
        var networkShareService = new TrackingNetworkShareService();
        var destinationService = new DestinationService(ftpService, sftpService, scpService, networkShareService, new StubVpnService());
        var destination = new DestinationSettings
        {
            Type = DestinationType.Scp,
            Host = "example.local",
            Port = 22,
            Username = "test",
            Password = "test123"
        };

        await destinationService.TestConnectionAsync(destination);
        await destinationService.UploadDirectoryAsync(Path.GetTempPath(), destination, "/uploads");
        await destinationService.ListRootEntriesAsync(destination, "/uploads");
        await destinationService.TryGetFileSizeAsync(destination, "/uploads/program.nc");
        await destinationService.DeleteRemoteItemAsync(destination, "/uploads/program.nc", isDirectory: false);

        Assert.Equal(5, scpService.CallCount);
        Assert.Equal(0, ftpService.CallCount);
        Assert.Equal(0, sftpService.CallCount);
        Assert.Equal(0, networkShareService.CallCount);
    }

    [Fact]
    public async Task NetworkShareDestination_RoutesOperationsToNetworkShareService()
    {
        var ftpService = new TrackingFtpService();
        var sftpService = new TrackingSftpService();
        var scpService = new TrackingScpService();
        var networkShareService = new TrackingNetworkShareService();
        var destinationService = new DestinationService(ftpService, sftpService, scpService, networkShareService, new StubVpnService());
        var destination = new DestinationSettings
        {
            Type = DestinationType.NetworkShare,
            NetworkHost = "fileserver.local",
            NetworkShareName = "Jobs",
            UseCurrentUserCredentials = false,
            Username = "test",
            Password = "test123"
        };

        await destinationService.TestConnectionAsync(destination);
        await destinationService.UploadDirectoryAsync(Path.GetTempPath(), destination, "/uploads");
        await destinationService.ListRootEntriesAsync(destination, "/uploads");
        await destinationService.TryGetFileSizeAsync(destination, "/uploads/program.nc");
        await destinationService.DeleteRemoteItemAsync(destination, "/uploads/program.nc", isDirectory: false);

        Assert.Equal(5, networkShareService.CallCount);
        Assert.Equal(0, ftpService.CallCount);
        Assert.Equal(0, sftpService.CallCount);
        Assert.Equal(0, scpService.CallCount);
    }

    private sealed class TrackingFtpService : IFtpService
    {
        public int CallCount { get; private set; }

        public Task<(bool Success, string Message)> TestConnectionAsync(DestinationSettings destination, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<(bool Success, string Message)>((true, "ftp"));
        }

        public Task<(bool Success, string Message)> UploadDirectoryAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<(bool Success, string Message)>((true, "ftp"));
        }

        public Task<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)> ListRootEntriesAsync(DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)>((true, [], "ftp"));
        }

        public Task<(bool Exists, long? SizeBytes, string Message)> TryGetFileSizeAsync(DestinationSettings destination, string remoteFilePath, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult((false, (long?)null, "ftp"));
        }

        public Task<(bool Success, string Message)> DeleteRemoteItemAsync(DestinationSettings destination, string remotePath, bool isDirectory, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<(bool Success, string Message)>((true, "ftp"));
        }
    }

    private sealed class TrackingSftpService : ISftpService
    {
        public int CallCount { get; private set; }

        public Task<(bool Success, string Message)> TestConnectionAsync(DestinationSettings destination, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<(bool Success, string Message)>((true, "sftp"));
        }

        public Task<(bool Success, string Message)> UploadDirectoryAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<(bool Success, string Message)>((true, "sftp"));
        }

        public Task<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)> ListRootEntriesAsync(DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)>((true, [], "sftp"));
        }

        public Task<(bool Exists, long? SizeBytes, string Message)> TryGetFileSizeAsync(DestinationSettings destination, string remoteFilePath, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult((false, (long?)null, "sftp"));
        }

        public Task<(bool Success, string Message)> DeleteRemoteItemAsync(DestinationSettings destination, string remotePath, bool isDirectory, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<(bool Success, string Message)>((true, "sftp"));
        }
    }

    private sealed class TrackingScpService : IScpService
    {
        public int CallCount { get; private set; }

        public Task<(bool Success, string Message)> TestConnectionAsync(DestinationSettings destination, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<(bool Success, string Message)>((true, "scp"));
        }

        public Task<(bool Success, string Message)> UploadDirectoryAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<(bool Success, string Message)>((true, "scp"));
        }

        public Task<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)> ListRootEntriesAsync(DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)>((true, [], "scp"));
        }

        public Task<(bool Exists, long? SizeBytes, string Message)> TryGetFileSizeAsync(DestinationSettings destination, string remoteFilePath, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult((false, (long?)null, "scp"));
        }

        public Task<(bool Success, string Message)> DeleteRemoteItemAsync(DestinationSettings destination, string remotePath, bool isDirectory, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<(bool Success, string Message)>((true, "scp"));
        }
    }

    private sealed class TrackingNetworkShareService : INetworkShareService
    {
        public int CallCount { get; private set; }

        public Task<(bool Success, string Message)> TestConnectionAsync(DestinationSettings destination, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<(bool Success, string Message)>((true, "network"));
        }

        public Task<(bool Success, string Message)> UploadDirectoryAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<(bool Success, string Message)>((true, "network"));
        }

        public Task<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)> ListRootEntriesAsync(DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)>((true, [], "network"));
        }

        public Task<(bool Exists, long? SizeBytes, string Message)> TryGetFileSizeAsync(DestinationSettings destination, string remoteFilePath, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult((false, (long?)null, "network"));
        }

        public Task<(bool Success, string Message)> DeleteRemoteItemAsync(DestinationSettings destination, string remotePath, bool isDirectory, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<(bool Success, string Message)>((true, "network"));
        }
    }

    private sealed class StubVpnService : IVpnService
    {
        public Task<IReadOnlyList<VpnConnectionInfo>> ListConnectionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VpnConnectionInfo>>([]);

        public Task<VpnConnectionEnsureResult> EnsureConnectedAsync(string connectionName, CancellationToken cancellationToken = default) =>
            Task.FromResult(VpnConnectionEnsureResult.NoRequirement());

        public Task<(bool Success, string Message)> DisconnectAsync(string connectionName, CancellationToken cancellationToken = default) =>
            Task.FromResult<(bool Success, string Message)>((true, string.Empty));
    }
}
