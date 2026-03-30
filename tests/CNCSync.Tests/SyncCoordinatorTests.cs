using CNCSync.Core.Configuration;
using CNCSync.Core.Processing;
using CNCSync.Core.Services;

namespace CNCSync.Tests;

public sealed class SyncCoordinatorTests
{
    [Fact]
    public async Task ProcessPathAsync_AppendsWatchAdditionalRemotePathToDestinationBasePath()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"cncsync-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputPath);

        try
        {
            var destinationService = new CapturingDestinationService();
            var processor = new StubProjectProcessor(outputPath);
            var coordinator = new SyncCoordinator(new StubFolderMonitor(), processor, destinationService, new AppSettingsValidator());

            var profile = new WatchProfileSettings
            {
                Name = "Watch 1",
                StagingFolder = outputPath,
                RemoteSubfolder = "/watch1"
            };

            var destination = new DestinationSettings
            {
                Name = "Destination 1",
                Type = DestinationType.Sftp,
                Host = "example.local",
                Port = 22,
                Username = "test",
                Password = "test123",
                RemoteBasePath = "/upload"
            };

            var result = await coordinator.ProcessPathAsync(
                "/tmp/source-file.nc",
                profile,
                destination,
                ProcessingSetupSettings.CreateDefault("Default"));

            Assert.True(result.Success);
            Assert.Equal("/upload/watch1", destinationService.LastUploadRemoteDirectoryPath);
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ProcessPathAsync_DoesNotAppendSourceFolderNameWhenAdditionalRemotePathIsBlank()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"cncsync-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputPath);
        var watchFolder = Path.Combine(Path.GetTempPath(), $"cncsync-watch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(watchFolder);

        try
        {
            var destinationService = new CapturingDestinationService();
            var processor = new StubProjectProcessor(outputPath, remoteFolderName: "watch1");
            var coordinator = new SyncCoordinator(new StubFolderMonitor(), processor, destinationService, new AppSettingsValidator());

            var profile = new WatchProfileSettings
            {
                Name = "Watch 1",
                WatchFolder = watchFolder,
                StagingFolder = outputPath,
                RemoteSubfolder = string.Empty
            };

            var destination = new DestinationSettings
            {
                Name = "Destination 1",
                Type = DestinationType.Sftp,
                Host = "example.local",
                Port = 22,
                Username = "test",
                Password = "test123",
                RemoteBasePath = "/upload"
            };

            var result = await coordinator.ProcessPathAsync(
                watchFolder,
                profile,
                destination,
                ProcessingSetupSettings.CreateDefault("Default"));

            Assert.True(result.Success);
            Assert.Equal("/upload", destinationService.LastUploadRemoteDirectoryPath);
        }
        finally
        {
            if (Directory.Exists(watchFolder))
            {
                Directory.Delete(watchFolder, recursive: true);
            }

            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ProcessPathAsync_AppendsDroppedChildFolderNameUnderConfiguredDestinationPath()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"cncsync-output-{Guid.NewGuid():N}");
        var watchFolder = Path.Combine(Path.GetTempPath(), $"cncsync-watch-{Guid.NewGuid():N}");
        var droppedFolder = Path.Combine(watchFolder, "job-123");
        Directory.CreateDirectory(outputPath);
        Directory.CreateDirectory(droppedFolder);

        try
        {
            var destinationService = new CapturingDestinationService();
            var processor = new StubProjectProcessor(outputPath, remoteFolderName: "job-123");
            var coordinator = new SyncCoordinator(new StubFolderMonitor(), processor, destinationService, new AppSettingsValidator());

            var profile = new WatchProfileSettings
            {
                Name = "Watch 1",
                WatchFolder = watchFolder,
                StagingFolder = outputPath,
                RemoteSubfolder = string.Empty
            };

            var destination = new DestinationSettings
            {
                Name = "Destination 1",
                Type = DestinationType.Sftp,
                Host = "example.local",
                Port = 22,
                Username = "test",
                Password = "test123",
                RemoteBasePath = "/upload"
            };

            var result = await coordinator.ProcessPathAsync(
                droppedFolder,
                profile,
                destination,
                ProcessingSetupSettings.CreateDefault("Default"));

            Assert.True(result.Success);
            Assert.Equal("/upload/job-123", destinationService.LastUploadRemoteDirectoryPath);
        }
        finally
        {
            if (Directory.Exists(watchFolder))
            {
                Directory.Delete(watchFolder, recursive: true);
            }

            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, recursive: true);
            }
        }
    }

    private sealed class StubFolderMonitor : IFolderMonitor
    {
        public event Action<WorkItemReadyEvent>? WorkItemReady { add { } remove { } }
        public bool IsRunning => false;
        public Task StartAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubProjectProcessor(string outputPath, string? remoteFolderName = null) : IProjectProcessor
    {
        public Task<ProcessingResult> ProcessAsync(string sourcePath, WatchProfileSettings profile, ProcessingSetupSettings processingSetup, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProcessingResult
            {
                Success = true,
                Message = "Processed",
                SourcePath = sourcePath,
                OutputPath = outputPath,
                RemoteFolderName = remoteFolderName,
                StartedAtUtc = DateTime.UtcNow,
                FinishedAtUtc = DateTime.UtcNow
            });
    }

    private sealed class CapturingDestinationService : IDestinationService
    {
        public string? LastUploadRemoteDirectoryPath { get; private set; }

        public Task<(bool Success, string Message)> TestConnectionAsync(DestinationSettings destination, CancellationToken cancellationToken = default) =>
            Task.FromResult<(bool Success, string Message)>((true, "ok"));

        public Task<(bool Success, string Message)> UploadDirectoryAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default)
        {
            LastUploadRemoteDirectoryPath = remoteDirectoryPath;
            return Task.FromResult<(bool Success, string Message)>((true, "uploaded"));
        }

        public Task<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)> ListRootEntriesAsync(DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default) =>
            Task.FromResult<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)>((true, [], "ok"));

        public Task<(bool Exists, long? SizeBytes, string Message)> TryGetFileSizeAsync(DestinationSettings destination, string remoteFilePath, CancellationToken cancellationToken = default) =>
            Task.FromResult((false, (long?)null, "missing"));

        public Task<(bool Success, string Message)> DeleteRemoteItemAsync(DestinationSettings destination, string remotePath, bool isDirectory, CancellationToken cancellationToken = default) =>
            Task.FromResult<(bool Success, string Message)>((true, "deleted"));
    }
}
