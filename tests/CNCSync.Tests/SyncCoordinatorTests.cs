using CNCSync.Core.Configuration;
using CNCSync.Core.Processing;
using CNCSync.Core.Services;
using CNCSync.Infrastructure.Monitoring;

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
                Password = "dummy-password",
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
                Password = "dummy-password",
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
                Password = "dummy-password",
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

    [Fact]
    public async Task CatchUpMissingItemsAsync_UsesStagedOutboxAndPreservesChildFolderName()
    {
        var watchFolder = Path.Combine(Path.GetTempPath(), $"cncsync-watch-{Guid.NewGuid():N}");
        var stagingFolder = Path.Combine(Path.GetTempPath(), $"cncsync-stage-{Guid.NewGuid():N}");
        var stagedChildFolder = Path.Combine(stagingFolder, "job-123");
        Directory.CreateDirectory(watchFolder);
        Directory.CreateDirectory(stagingFolder);
        Directory.CreateDirectory(Path.Combine(watchFolder, "job-123"));
        Directory.CreateDirectory(stagedChildFolder);
        await File.WriteAllTextAsync(Path.Combine(stagedChildFolder, "program.nc"), "G1 X1");

        try
        {
            var destinationService = new CapturingDestinationService();
            var processor = new StubProjectProcessor(stagingFolder, remoteFolderName: "job-123");
            var coordinator = new SyncCoordinator(new StubFolderMonitor(), processor, destinationService, new AppSettingsValidator());

            var profile = new WatchProfileSettings
            {
                Name = "Watch 1",
                WatchFolder = watchFolder,
                StagingFolder = stagingFolder,
                RemoteSubfolder = string.Empty
            };

            var destination = new DestinationSettings
            {
                Name = "Destination 1",
                Type = DestinationType.Sftp,
                Host = "example.local",
                Port = 22,
                Username = "test",
                Password = "dummy-password",
                RemoteBasePath = "/upload"
            };

            var result = await coordinator.CatchUpMissingItemsAsync(
                profile,
                destination,
                ProcessingSetupSettings.CreateDefault("Default"));

            Assert.True(result.Success);
            Assert.Equal("/upload/job-123", destinationService.LastUploadRemoteDirectoryPath);
            Assert.False(Directory.Exists(stagedChildFolder));
        }
        finally
        {
            if (Directory.Exists(watchFolder))
            {
                Directory.Delete(watchFolder, recursive: true);
            }

            if (Directory.Exists(stagingFolder))
            {
                Directory.Delete(stagingFolder, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ProcessPathAsync_DoesNotDeleteConfiguredBasePathWhenReplacingRootUpload()
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
                Password = "dummy-password",
                RemoteBasePath = "/upload",
                ReplaceRemoteFolderOnUpload = true
            };

            var result = await coordinator.ProcessPathAsync(
                "/tmp/source-file.nc",
                profile,
                destination,
                ProcessingSetupSettings.CreateDefault("Default"));

            Assert.True(result.Success);
            Assert.Null(destinationService.LastDeletedRemotePath);
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
    public async Task ProcessPathAsync_DeletesOnlyItemSpecificChildFolderWhenReplacingUpload()
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
                Password = "dummy-password",
                RemoteBasePath = "/upload",
                ReplaceRemoteFolderOnUpload = true
            };

            var result = await coordinator.ProcessPathAsync(
                droppedFolder,
                profile,
                destination,
                ProcessingSetupSettings.CreateDefault("Default"));

            Assert.True(result.Success);
            Assert.Equal("/upload/job-123", destinationService.LastDeletedRemotePath);
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

    [Fact]
    public async Task ProcessPathAsync_FailedUploadKeepsMonitoringStatusRunningWhenWatcherIsActive()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"cncsync-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputPath);

        try
        {
            var destinationService = new CapturingDestinationService(uploadResult: (false, "FTP upload failed: test failure"));
            var processor = new StubProjectProcessor(outputPath);
            var coordinator = new SyncCoordinator(new StubFolderMonitor(isRunning: true), processor, destinationService, new AppSettingsValidator());
            var statuses = new List<string>();
            coordinator.StatusChanged += statuses.Add;

            var result = await coordinator.ProcessPathAsync(
                "/tmp/source-file.nc",
                new WatchProfileSettings
                {
                    Name = "Watch 1",
                    StagingFolder = outputPath
                },
                new DestinationSettings
                {
                    Name = "Destination 1",
                    Type = DestinationType.Ftp,
                    Host = "example.local",
                    Port = 21
                },
                ProcessingSetupSettings.CreateDefault("Default"));

            Assert.False(result.Success);
            Assert.Equal("Running", statuses.Last());
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
    public async Task ProcessPathAsync_FailedProcessingKeepsMonitoringStatusRunningWhenWatcherIsActive()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"cncsync-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputPath);

        try
        {
            var destinationService = new CapturingDestinationService();
            var processor = new StubProjectProcessor(outputPath, processingResultFactory: sourcePath => new ProcessingResult
            {
                Success = false,
                Message = "External processing failed: test failure",
                SourcePath = sourcePath,
                OutputPath = outputPath,
                StartedAtUtc = DateTime.UtcNow,
                FinishedAtUtc = DateTime.UtcNow,
                Errors = ["External processing failed: test failure"]
            });
            var coordinator = new SyncCoordinator(new StubFolderMonitor(isRunning: true), processor, destinationService, new AppSettingsValidator());
            var statuses = new List<string>();
            coordinator.StatusChanged += statuses.Add;

            var result = await coordinator.ProcessPathAsync(
                "/tmp/source-file.nc",
                new WatchProfileSettings
                {
                    Name = "Watch 1",
                    StagingFolder = outputPath
                },
                new DestinationSettings
                {
                    Name = "Destination 1",
                    Type = DestinationType.Ftp,
                    Host = "example.local",
                    Port = 21
                },
                ProcessingSetupSettings.CreateDefault("Default"));

            Assert.False(result.Success);
            Assert.Equal("Running", statuses.Last());
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("job-123", "job-123")]
    [InlineData("job-123/NC/program.nc", "job-123")]
    [InlineData("job-123/AutoStickLabel/label.jpg", "job-123")]
    [InlineData("top-level-file.nc", "top-level-file.nc")]
    public void ResolveWorkItemPath_CollapsesNestedChangesToTopLevelWatchItem(string relativePath, string expectedRelativeWorkItem)
    {
        var watchFolder = Path.Combine(Path.GetTempPath(), $"cncsync-watch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(watchFolder);

        try
        {
            var profile = new WatchProfileSettings
            {
                WatchFolder = watchFolder
            };

            var fullPath = Path.Combine(watchFolder, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var resolved = FileSystemFolderMonitor.ResolveWorkItemPathForTesting(profile, fullPath);

            Assert.Equal(
                Path.Combine(watchFolder, expectedRelativeWorkItem.Replace('/', Path.DirectorySeparatorChar)),
                resolved);
        }
        finally
        {
            if (Directory.Exists(watchFolder))
            {
                Directory.Delete(watchFolder, recursive: true);
            }
        }
    }

    private sealed class StubFolderMonitor(bool isRunning = false) : IFolderMonitor
    {
        public event Action<WorkItemReadyEvent>? WorkItemReady { add { } remove { } }
        public bool IsRunning => isRunning;
        public Task StartAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubProjectProcessor(
        string outputPath,
        string? remoteFolderName = null,
        Func<string, ProcessingResult>? processingResultFactory = null) : IProjectProcessor
    {
        public Task<ProcessingResult> ProcessAsync(string sourcePath, WatchProfileSettings profile, ProcessingSetupSettings processingSetup, CancellationToken cancellationToken = default) =>
            Task.FromResult(processingResultFactory?.Invoke(sourcePath) ?? new ProcessingResult
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

    private sealed class CapturingDestinationService((bool Success, string Message)? uploadResult = null) : IDestinationService
    {
        public string? LastUploadRemoteDirectoryPath { get; private set; }
        public string? LastDeletedRemotePath { get; private set; }

        public Task<(bool Success, string Message)> TestConnectionAsync(DestinationSettings destination, CancellationToken cancellationToken = default) =>
            Task.FromResult<(bool Success, string Message)>((true, "ok"));

        public Task<(bool Success, string Message)> UploadDirectoryAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            LastUploadRemoteDirectoryPath = remoteDirectoryPath;
            return Task.FromResult(uploadResult ?? (true, "uploaded"));
        }

        public Task<(bool Success, string Message)> UploadFileSystemItemAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            LastUploadRemoteDirectoryPath = remoteDirectoryPath;
            return Task.FromResult(uploadResult ?? (true, "uploaded"));
        }

        public Task<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)> ListRootEntriesAsync(DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default) =>
            Task.FromResult<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)>((true, [], "ok"));

        public Task<(bool Exists, long? SizeBytes, string Message)> TryGetFileSizeAsync(DestinationSettings destination, string remoteFilePath, CancellationToken cancellationToken = default) =>
            Task.FromResult((false, (long?)null, "missing"));

        public Task<(bool Success, string Message)> DeleteRemoteItemAsync(DestinationSettings destination, string remotePath, bool isDirectory, CancellationToken cancellationToken = default)
        {
            LastDeletedRemotePath = remotePath;
            return Task.FromResult<(bool Success, string Message)>((true, "deleted"));
        }
    }
}
