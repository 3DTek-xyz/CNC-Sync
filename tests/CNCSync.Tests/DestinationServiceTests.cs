using CNCSync.Core.Configuration;
using CNCSync.Core.Services;
using CNCSync.Infrastructure.Networking;

namespace CNCSync.Tests;

public sealed class DestinationServiceTests
{
    [Fact]
    public async Task LocalFolderDestination_SupportsUploadBrowseSizeAndDelete()
    {
        var sourceRoot = Path.Combine(Path.GetTempPath(), $"cncsync-source-{Guid.NewGuid():N}");
        var destinationRoot = Path.Combine(Path.GetTempPath(), $"cncsync-destination-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceRoot);

        try
        {
            var nestedSourceFolder = Path.Combine(sourceRoot, "nested");
            Directory.CreateDirectory(nestedSourceFolder);

            var rootFilePath = Path.Combine(sourceRoot, "program.nc");
            var nestedFilePath = Path.Combine(nestedSourceFolder, "part.nc");
            await File.WriteAllTextAsync(rootFilePath, "G0 X0 Y0");
            await File.WriteAllTextAsync(nestedFilePath, "G1 X1 Y1");

            var destination = new DestinationSettings
            {
                Name = "Local Test",
                Type = DestinationType.LocalFolder,
                LocalRootPath = destinationRoot
            };

            var service = new DestinationService(new StubFtpService(), new StubSftpService(), new StubScpService(), new StubNetworkShareService(), new StubVpnService());

            var testResult = await service.TestConnectionAsync(destination);
            Assert.True(testResult.Success);
            Assert.True(Directory.Exists(destinationRoot));

            var uploadResult = await service.UploadDirectoryAsync(sourceRoot, destination, "/machine-a/jobs");
            Assert.True(uploadResult.Success);

            var uploadedRootFile = Path.Combine(destinationRoot, "machine-a", "jobs", "program.nc");
            var uploadedNestedFile = Path.Combine(destinationRoot, "machine-a", "jobs", "nested", "part.nc");
            Assert.True(File.Exists(uploadedRootFile));
            Assert.True(File.Exists(uploadedNestedFile));

            var browseResult = await service.ListRootEntriesAsync(destination, "/machine-a/jobs");
            Assert.True(browseResult.Success);
            Assert.Contains(browseResult.Entries, entry => entry.Name == "program.nc" && !entry.IsDirectory);
            Assert.Contains(browseResult.Entries, entry => entry.Name == "nested" && entry.IsDirectory);

            var sizeResult = await service.TryGetFileSizeAsync(destination, "/machine-a/jobs/program.nc");
            Assert.True(sizeResult.Exists);
            Assert.Equal(new FileInfo(uploadedRootFile).Length, sizeResult.SizeBytes);

            var deleteFileResult = await service.DeleteRemoteItemAsync(destination, "/machine-a/jobs/program.nc", isDirectory: false);
            Assert.True(deleteFileResult.Success);
            Assert.False(File.Exists(uploadedRootFile));

            var deleteDirectoryResult = await service.DeleteRemoteItemAsync(destination, "/machine-a/jobs/nested", isDirectory: true);
            Assert.True(deleteDirectoryResult.Success);
            Assert.False(Directory.Exists(Path.Combine(destinationRoot, "machine-a", "jobs", "nested")));
        }
        finally
        {
            if (Directory.Exists(sourceRoot))
            {
                Directory.Delete(sourceRoot, recursive: true);
            }

            if (Directory.Exists(destinationRoot))
            {
                Directory.Delete(destinationRoot, recursive: true);
            }
        }
    }

    private sealed class StubFtpService : IFtpService
    {
        public Task<(bool Success, string Message)> TestConnectionAsync(DestinationSettings destination, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("FTP should not be used in local destination tests.");

        public Task<(bool Success, string Message)> UploadDirectoryAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("FTP should not be used in local destination tests.");

        public Task<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)> ListRootEntriesAsync(DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("FTP should not be used in local destination tests.");

        public Task<(bool Exists, long? SizeBytes, string Message)> TryGetFileSizeAsync(DestinationSettings destination, string remoteFilePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("FTP should not be used in local destination tests.");

        public Task<(bool Success, string Message)> DeleteRemoteItemAsync(DestinationSettings destination, string remotePath, bool isDirectory, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("FTP should not be used in local destination tests.");
    }

    private sealed class StubSftpService : ISftpService
    {
        public Task<(bool Success, string Message)> TestConnectionAsync(DestinationSettings destination, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("SFTP should not be used in local destination tests.");

        public Task<(bool Success, string Message)> UploadDirectoryAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("SFTP should not be used in local destination tests.");

        public Task<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)> ListRootEntriesAsync(DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("SFTP should not be used in local destination tests.");

        public Task<(bool Exists, long? SizeBytes, string Message)> TryGetFileSizeAsync(DestinationSettings destination, string remoteFilePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("SFTP should not be used in local destination tests.");

        public Task<(bool Success, string Message)> DeleteRemoteItemAsync(DestinationSettings destination, string remotePath, bool isDirectory, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("SFTP should not be used in local destination tests.");
    }

    private sealed class StubScpService : IScpService
    {
        public Task<(bool Success, string Message)> TestConnectionAsync(DestinationSettings destination, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("SCP should not be used in local destination tests.");

        public Task<(bool Success, string Message)> UploadDirectoryAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("SCP should not be used in local destination tests.");

        public Task<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)> ListRootEntriesAsync(DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("SCP should not be used in local destination tests.");

        public Task<(bool Exists, long? SizeBytes, string Message)> TryGetFileSizeAsync(DestinationSettings destination, string remoteFilePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("SCP should not be used in local destination tests.");

        public Task<(bool Success, string Message)> DeleteRemoteItemAsync(DestinationSettings destination, string remotePath, bool isDirectory, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("SCP should not be used in local destination tests.");
    }

    private sealed class StubNetworkShareService : INetworkShareService
    {
        public Task<(bool Success, string Message)> TestConnectionAsync(DestinationSettings destination, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Network shares should not be used in local destination tests.");

        public Task<(bool Success, string Message)> UploadDirectoryAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Network shares should not be used in local destination tests.");

        public Task<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)> ListRootEntriesAsync(DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Network shares should not be used in local destination tests.");

        public Task<(bool Exists, long? SizeBytes, string Message)> TryGetFileSizeAsync(DestinationSettings destination, string remoteFilePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Network shares should not be used in local destination tests.");

        public Task<(bool Success, string Message)> DeleteRemoteItemAsync(DestinationSettings destination, string remotePath, bool isDirectory, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Network shares should not be used in local destination tests.");
    }

    [Fact]
    public async Task LocalFolderDestination_IgnoresMetadataAndHiddenFilesDuringUpload()
    {
        var sourceRoot = Path.Combine(Path.GetTempPath(), $"cncsync-source-{Guid.NewGuid():N}");
        var destinationRoot = Path.Combine(Path.GetTempPath(), $"cncsync-destination-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceRoot);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "program.nc"), "G0 X0 Y0");
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, ".DS_Store"), "junk");
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "Thumbs.db"), "junk");
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "desktop.ini"), "junk");
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "._program.nc"), "junk");

            var destination = new DestinationSettings
            {
                Name = "Local Test",
                Type = DestinationType.LocalFolder,
                LocalRootPath = destinationRoot
            };

            var service = new DestinationService(new StubFtpService(), new StubSftpService(), new StubScpService(), new StubNetworkShareService(), new StubVpnService());
            var uploadResult = await service.UploadDirectoryAsync(sourceRoot, destination, "/");

            Assert.True(uploadResult.Success);
            Assert.True(File.Exists(Path.Combine(destinationRoot, "program.nc")));
            Assert.False(File.Exists(Path.Combine(destinationRoot, ".DS_Store")));
            Assert.False(File.Exists(Path.Combine(destinationRoot, "Thumbs.db")));
            Assert.False(File.Exists(Path.Combine(destinationRoot, "desktop.ini")));
            Assert.False(File.Exists(Path.Combine(destinationRoot, "._program.nc")));
        }
        finally
        {
            if (Directory.Exists(sourceRoot))
            {
                Directory.Delete(sourceRoot, recursive: true);
            }

            if (Directory.Exists(destinationRoot))
            {
                Directory.Delete(destinationRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RemoteDestination_WithRequiredVpn_EnsuresVpnBeforeTesting()
    {
        var ftpService = new TrackingFtpService();
        var vpnService = new StubVpnService();
        var destination = new DestinationSettings
        {
            Type = DestinationType.Ftp,
            Host = "example.local",
            RequiredVpnConnectionName = "Office VPN"
        };

        var service = new DestinationService(ftpService, new StubSftpService(), new StubScpService(), new StubNetworkShareService(), vpnService);
        var result = await service.TestConnectionAsync(destination);

        Assert.True(result.Success);
        Assert.Equal("Office VPN", vpnService.LastEnsuredConnectionName);
        Assert.Equal(1, ftpService.TestCallCount);
        Assert.Contains("Connected required VPN", result.Message, StringComparison.Ordinal);
    }

    private sealed class StubVpnService : IVpnService
    {
        public string? LastEnsuredConnectionName { get; private set; }
        public string? LastDisconnectedConnectionName { get; private set; }
        public int DisconnectCallCount { get; private set; }

        public Task<IReadOnlyList<VpnConnectionInfo>> ListConnectionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VpnConnectionInfo>>([]);

        public Task<VpnConnectionEnsureResult> EnsureConnectedAsync(string connectionName, CancellationToken cancellationToken = default)
        {
            LastEnsuredConnectionName = connectionName;
            return Task.FromResult(new VpnConnectionEnsureResult
            {
                Success = true,
                ConnectedNow = true,
                ConnectionStateChanged = true,
                Message = $"Connected required VPN '{connectionName}'."
            });
        }

        public Task<(bool Success, string Message)> DisconnectAsync(string connectionName, CancellationToken cancellationToken = default)
        {
            LastDisconnectedConnectionName = connectionName;
            DisconnectCallCount++;
            return Task.FromResult<(bool Success, string Message)>((true, $"Disconnected required VPN '{connectionName}'."));
        }
    }

    private sealed class TrackingFtpService : IFtpService
    {
        public int TestCallCount { get; private set; }

        public Task<(bool Success, string Message)> TestConnectionAsync(DestinationSettings destination, CancellationToken cancellationToken = default)
        {
            TestCallCount++;
            return Task.FromResult<(bool Success, string Message)>((true, "FTP destination reachable."));
        }

        public Task<(bool Success, string Message)> UploadDirectoryAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)> ListRootEntriesAsync(DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<(bool Exists, long? SizeBytes, string Message)> TryGetFileSizeAsync(DestinationSettings destination, string remoteFilePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<(bool Success, string Message)> DeleteRemoteItemAsync(DestinationSettings destination, string remotePath, bool isDirectory, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task TestConnectionAsync_DisconnectsVpnAfterIdleTimeoutWhenConfigured()
    {
        var ftpService = new TrackingFtpService();
        var vpnService = new StubVpnService();
        var destination = new DestinationSettings
        {
            Type = DestinationType.Ftp,
            Host = "example.local",
            Port = 21,
            RequiredVpnConnectionName = "CBWSS",
            DisconnectVpnWhenFinished = true
        };

        var service = new DestinationService(ftpService, new StubSftpService(), new StubScpService(), new StubNetworkShareService(), vpnService, TimeSpan.FromMilliseconds(50));

        var result = await service.TestConnectionAsync(destination);

        Assert.True(result.Success);
        Assert.Equal("CBWSS", vpnService.LastEnsuredConnectionName);
        Assert.Null(vpnService.LastDisconnectedConnectionName);

        await Task.Delay(150);

        Assert.Equal("CBWSS", vpnService.LastDisconnectedConnectionName);
    }

    [Fact]
    public async Task SecondOperationWithinIdleTimeout_ReusesVpnLeaseBeforeDisconnecting()
    {
        var ftpService = new TrackingFtpService();
        var vpnService = new StubVpnService();
        var destination = new DestinationSettings
        {
            Type = DestinationType.Ftp,
            Host = "example.local",
            Port = 21,
            RequiredVpnConnectionName = "CBWSS",
            DisconnectVpnWhenFinished = true
        };

        var service = new DestinationService(ftpService, new StubSftpService(), new StubScpService(), new StubNetworkShareService(), vpnService, TimeSpan.FromMilliseconds(120));

        var firstResult = await service.TestConnectionAsync(destination);
        Assert.True(firstResult.Success);

        await Task.Delay(60);
        var secondResult = await service.TestConnectionAsync(destination);
        Assert.True(secondResult.Success);

        await Task.Delay(80);
        Assert.Equal(0, vpnService.DisconnectCallCount);

        await Task.Delay(100);
        Assert.Equal(1, vpnService.DisconnectCallCount);
        Assert.Equal("CBWSS", vpnService.LastDisconnectedConnectionName);
    }
}
