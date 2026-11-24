using CNCSync.Core.Configuration;
using CNCSync.Core.Processing;
using CNCSync.Core.Services;
using CNCSync.Infrastructure.Monitoring;

namespace CNCSync.Tests;

public sealed class SyncCoordinatorTests
{
    [Fact]
    public async Task WatchQueue_BoundsConcurrentWorkAndKeepsTheLatestInFlightChange()
    {
        using var fixture = new QueueFixture();
        var paths = new[] { fixture.Write("first.nc", "old"), fixture.Write("second.nc"), fixture.Write("third.nc") };
        var started = System.Threading.Channels.Channel.CreateUnbounded<(string Path, string Content)>();
        var release = new SemaphoreSlim(0);
        var processor = new DelegateProcessor(async (path, token) =>
        {
            await started.Writer.WriteAsync((path, File.ReadAllText(path)), token);
            await release.WaitAsync(token);
            return new ProcessingResult { Success = true, SourcePath = path };
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var coordinator = fixture.Create(processor);
        await coordinator.StartAsync(fixture.Settings, timeout.Token);

        fixture.Emit(paths[0]);
        Assert.Equal((paths[0], "old"), await started.Reader.ReadAsync(timeout.Token));
        fixture.Emit(paths[1]);
        Assert.Equal(paths[1], (await started.Reader.ReadAsync(timeout.Token)).Path);
        fixture.Emit(paths[2]);
        await Task.Delay(200, timeout.Token);
        Assert.False(started.Reader.TryRead(out _));

        File.WriteAllText(paths[0], "new content");
        fixture.Emit(paths[0]);
        release.Release(4);
        var remaining = new[] { await started.Reader.ReadAsync(timeout.Token), await started.Reader.ReadAsync(timeout.Token) };
        Assert.Contains((paths[0], "new content"), remaining);
        Assert.Contains(remaining, item => item.Path == paths[2]);
    }

    [Fact]
    public async Task WatchQueue_SuppressesCompletedInputsAcrossCoordinatorRestart()
    {
        using var fixture = new QueueFixture();
        var path = fixture.Write("input.nc", "before");
        var processed = System.Threading.Channels.Channel.CreateUnbounded<string>();
        var processor = new DelegateProcessor((source, _) =>
            Task.FromResult(new ProcessingResult { Success = true, SourcePath = source, Message = File.ReadAllText(source) }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using (var coordinator = fixture.Create(processor))
        {
            coordinator.ProcessingCompleted += result => processed.Writer.TryWrite(result.Message);
            await coordinator.StartAsync(fixture.Settings, timeout.Token);
            fixture.Emit(path);
            Assert.Equal("before", await processed.Reader.ReadAsync(timeout.Token));
        }

        await using var restarted = fixture.Create(processor);
        restarted.ProcessingCompleted += result => processed.Writer.TryWrite(result.Message);
        await restarted.StartAsync(fixture.Settings, timeout.Token);
        fixture.Emit(path);
        await Task.Delay(300, timeout.Token);
        Assert.False(processed.Reader.TryRead(out _));
        File.WriteAllText(path, "after");
        fixture.Emit(path);
        Assert.Equal("after", await processed.Reader.ReadAsync(timeout.Token));
    }

    [Fact]
    public async Task WatchQueue_HonorsPersistedRetryAfterAndStopsAtFourAttempts()
    {
        using var fixture = new QueueFixture();
        var path = fixture.Write("throttled.nc");
        var attempts = 0;
        var clock = new AdjustableClock();
        var outcomes = System.Threading.Channels.Channel.CreateUnbounded<string>();
        var processor = new DelegateProcessor((source, _) => Task.FromResult(new ProcessingResult
        {
            SourcePath = source, Message = $"attempt {Interlocked.Increment(ref attempts)}",
            IsRetryable = true, RetryAfter = TimeSpan.FromSeconds(30)
        }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using (var coordinator = fixture.Create(processor, clock))
        {
            coordinator.ProcessingCompleted += result => outcomes.Writer.TryWrite(result.Message);
            await coordinator.StartAsync(fixture.Settings, timeout.Token);
            fixture.Emit(path);
            Assert.Equal("attempt 1", await outcomes.Reader.ReadAsync(timeout.Token));
        }

        await using var restarted = fixture.Create(processor, clock);
        restarted.ProcessingCompleted += result => outcomes.Writer.TryWrite(result.Message);
        await restarted.StartAsync(fixture.Settings, timeout.Token);
        clock.Advance(TimeSpan.FromSeconds(29));
        fixture.Emit(path);
        await Task.Delay(400, timeout.Token);
        Assert.False(outcomes.Reader.TryRead(out _));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("attempt 2", await outcomes.Reader.ReadAsync(timeout.Token));
        for (var attempt = 3; attempt <= 4; attempt++)
        {
            clock.Advance(TimeSpan.FromSeconds(30));
            Assert.Equal($"attempt {attempt}", await outcomes.Reader.ReadAsync(timeout.Token));
        }

        clock.Advance(TimeSpan.FromDays(1));
        fixture.Emit(path);
        await Task.Delay(400, timeout.Token);
        Assert.Equal(4, Volatile.Read(ref attempts));
    }

    [Fact]
    public async Task WatchQueue_DoesNotRetryPermanentFailureButAllowsManualProcessing()
    {
        using var fixture = new QueueFixture();
        var path = fixture.Write("permanent.nc");
        var attempts = 0;
        var outcomes = System.Threading.Channels.Channel.CreateUnbounded<ProcessingResult>();
        var processor = new DelegateProcessor((source, _) => Task.FromResult(new ProcessingResult
        {
            SourcePath = source, Success = Interlocked.Increment(ref attempts) == 2, Message = "test processing"
        }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var coordinator = fixture.Create(processor);
        coordinator.ProcessingCompleted += result => outcomes.Writer.TryWrite(result);
        await coordinator.StartAsync(fixture.Settings, timeout.Token);
        fixture.Emit(path);
        Assert.False((await outcomes.Reader.ReadAsync(timeout.Token)).Success);
        fixture.Emit(path);
        await Task.Delay(300, timeout.Token);
        Assert.False(outcomes.Reader.TryRead(out _));

        Assert.True((await coordinator.ProcessPathAsync(path, fixture.Settings.WatchProfiles[0], null,
            fixture.Settings.ProcessingSetups[0], cancellationToken: timeout.Token)).Success);
    }

    [Fact]
    public async Task WatchQueue_RetriesPermanentFailureWhenApiSettingsAreCorrected()
    {
        using var fixture = new QueueFixture();
        var path = fixture.Write("corrected.nc");
        fixture.UseApiProcessing();
        var attempts = 0;
        var outcomes = System.Threading.Channels.Channel.CreateUnbounded<ProcessingResult>();
        var processor = new DelegateProcessor((source, _) => Task.FromResult(new ProcessingResult
        {
            SourcePath = source, Success = Interlocked.Increment(ref attempts) == 2
        }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using (var coordinator = fixture.Create(processor))
        {
            coordinator.ProcessingCompleted += result => outcomes.Writer.TryWrite(result);
            await coordinator.StartAsync(fixture.Settings, timeout.Token);
            fixture.Emit(path);
            Assert.False((await outcomes.Reader.ReadAsync(timeout.Token)).Success);
        }

        fixture.Settings.ProCutApi.ApiKey += "-corrected";
        await using var restarted = fixture.Create(processor);
        restarted.ProcessingCompleted += result => outcomes.Writer.TryWrite(result);
        await restarted.StartAsync(fixture.Settings, timeout.Token);
        fixture.Emit(path);
        Assert.True((await outcomes.Reader.ReadAsync(timeout.Token)).Success);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task WatchQueue_UsesUpdatedApiSettingsWithoutRestartingMonitoring()
    {
        using var fixture = new QueueFixture();
        fixture.UseApiProcessing();
        var path = fixture.Write("rotate-key.nc");
        var updatedApi = new ProCutApiSettings
        {
            BaseUrl = "https://api.example.com",
            ApiKey = TestSecrets.ProCutApiKey + "-updated"
        };
        var outcomes = System.Threading.Channels.Channel.CreateUnbounded<ProcessingResult>();
        var processor = new DelegateProcessor((source, api, _) => Task.FromResult(new ProcessingResult
        {
            SourcePath = source,
            Success = api?.BaseUrl == updatedApi.BaseUrl && api.ApiKey == updatedApi.ApiKey
        }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var coordinator = fixture.Create(processor);
        coordinator.ProcessingCompleted += result => outcomes.Writer.TryWrite(result);
        await coordinator.StartAsync(fixture.Settings, timeout.Token);
        fixture.Emit(path);
        Assert.False((await outcomes.Reader.ReadAsync(timeout.Token)).Success);

        coordinator.UpdateProCutApiSettings(updatedApi);
        Assert.True(coordinator.IsRunning);
        fixture.Emit(path);
        Assert.True((await outcomes.Reader.ReadAsync(timeout.Token)).Success);
    }

    [Fact]
    public async Task WatchQueue_DropsOldRetryDeadlineWhenApiSettingsAreCorrected()
    {
        using var fixture = new QueueFixture();
        var path = fixture.Write("retry.nc");
        fixture.UseApiProcessing();
        var attempts = 0;
        var clock = new AdjustableClock();
        var outcomes = System.Threading.Channels.Channel.CreateUnbounded<ProcessingResult>();
        var processor = new DelegateProcessor((source, _) => Task.FromResult(Interlocked.Increment(ref attempts) switch
        {
            1 => new ProcessingResult { SourcePath = source, IsRetryable = true, RetryAfter = TimeSpan.FromDays(1) },
            2 => new ProcessingResult { SourcePath = source, IsRetryable = true },
            _ => new ProcessingResult { SourcePath = source, Success = true }
        }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using (var coordinator = fixture.Create(processor, clock))
        {
            coordinator.ProcessingCompleted += result => outcomes.Writer.TryWrite(result);
            await coordinator.StartAsync(fixture.Settings, timeout.Token);
            fixture.Emit(path);
            Assert.False((await outcomes.Reader.ReadAsync(timeout.Token)).Success);
        }

        fixture.Settings.ProCutApi.ApiKey += "-corrected";
        await using var restarted = fixture.Create(processor, clock);
        restarted.ProcessingCompleted += result => outcomes.Writer.TryWrite(result);
        await restarted.StartAsync(fixture.Settings, timeout.Token);
        fixture.Emit(path);
        Assert.False((await outcomes.Reader.ReadAsync(timeout.Token)).Success);
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.True((await outcomes.Reader.ReadAsync(timeout.Token)).Success);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task WatchQueue_RebuildsMissingOutboxEntryInsteadOfStrandingInput()
    {
        using var fixture = new QueueFixture();
        var source = fixture.Write("staged.nc");
        fixture.AddLocalDestination();
        var processor = CreateManifestProcessor(fixture.Settings.WatchProfiles[0]);
        var destination = new CapturingDestinationService((false, "Offline"));
        var outcomes = System.Threading.Channels.Channel.CreateUnbounded<ProcessingResult>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using (var coordinator = fixture.Create(processor, destinationService: destination))
        {
            coordinator.ProcessingCompleted += result => outcomes.Writer.TryWrite(result);
            await coordinator.StartAsync(fixture.Settings, timeout.Token);
            fixture.Emit(source);
            var result = await outcomes.Reader.ReadAsync(timeout.Token);
            Assert.False(result.Success);
            Assert.NotNull(result.CompletedOutput);
            Directory.Delete(result.CompletedOutput.JobDirectory, recursive: true);
        }

        destination.UploadResult = (true, "Delivered");
        await using var restarted = fixture.Create(processor, destinationService: destination);
        restarted.ProcessingCompleted += result => outcomes.Writer.TryWrite(result);
        await restarted.StartAsync(fixture.Settings, timeout.Token);
        fixture.Emit(source);
        Assert.True((await outcomes.Reader.ReadAsync(timeout.Token)).Success);
        Assert.Equal(2, destination.UploadCount);
    }

    [Fact]
    public async Task WatchQueue_SeparatesApiFilesWithIdenticalNamesInDifferentSourceFolders()
    {
        using var fixture = new QueueFixture();
        fixture.UseApiProcessing();
        fixture.AddLocalDestination();
        var first = fixture.Write(Path.Combine("Job-A", "program.nc"));
        var second = fixture.Write(Path.Combine("Job-B", "program.nc"));
        var destination = new CapturingDestinationService();
        var outcomes = System.Threading.Channels.Channel.CreateUnbounded<ProcessingResult>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var processor = CreateManifestProcessor(fixture.Settings.WatchProfiles[0], path => Path.GetFileName(Path.GetDirectoryName(path)));
        await using var coordinator = fixture.Create(processor, destinationService: destination);
        coordinator.ProcessingCompleted += result => outcomes.Writer.TryWrite(result);
        await coordinator.StartAsync(fixture.Settings, timeout.Token);
        fixture.Emit(first);
        fixture.Emit(second);
        Assert.True((await outcomes.Reader.ReadAsync(timeout.Token)).Success);
        Assert.True((await outcomes.Reader.ReadAsync(timeout.Token)).Success);
        Assert.Equal(new[] { "/Job-A", "/Job-B" }, destination.UploadPaths.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task CatchUp_UsesStagedRouteAfterProcessingModeChanges()
    {
        using var fixture = new QueueFixture();
        var source = fixture.Write(Path.Combine("Job-A", "NC", "part.nc"));
        var profile = fixture.Settings.WatchProfiles[0];
        fixture.UseApiProcessing();
        var setup = fixture.Settings.ProcessingSetups[0];
        var destination = new DestinationSettings { RemoteBasePath = "/upload" };
        var processor = CreateManifestProcessor(profile, _ => "Job-A/NC");
        var service = new CapturingDestinationService((false, "Destination offline"));
        await using (var coordinator = fixture.Create(processor, destinationService: service))
        {
            Assert.False((await coordinator.ProcessPathAsync(source, profile, destination, setup)).Success);
        }

        setup.Mode = ProcessingMode.DefaultUpload;
        service.UploadResult = (true, "Delivered");
        await using var restarted = fixture.Create(processor, destinationService: service);
        Assert.True((await restarted.CatchUpMissingItemsAsync(profile, destination, setup)).Success);
        Assert.Equal(new[] { "/upload/Job-A/NC", "/upload/Job-A/NC" }, service.UploadPaths);
    }

    [Fact]
    public async Task WatchQueue_StopCancelsAndAwaitsAnActiveAttemptWithoutLosingInput()
    {
        using var fixture = new QueueFixture();
        var path = fixture.Write("interrupted.nc");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var processor = new DelegateProcessor(async (source, token) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                finally
                {
                    canceled.TrySetResult();
                }
            }

            return new ProcessingResult { SourcePath = source, Success = true };
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var coordinator = fixture.Create(processor);
        var completed = new TaskCompletionSource<ProcessingResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.ProcessingCompleted += result => completed.TrySetResult(result);
        await coordinator.StartAsync(fixture.Settings, timeout.Token);
        fixture.Emit(path);
        await started.Task.WaitAsync(timeout.Token);
        await coordinator.StopAsync(timeout.Token);
        Assert.True(canceled.Task.IsCompletedSuccessfully);
        Assert.False(completed.Task.IsCompleted);

        await coordinator.StartAsync(fixture.Settings, timeout.Token);
        fixture.Emit(path);
        Assert.True((await completed.Task.WaitAsync(timeout.Token)).Success);
    }

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
    public async Task CatchUpMissingItemsAsync_DoesNotDeliverUnverifiedStaging()
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
            Assert.Null(destinationService.LastUploadRemoteDirectoryPath);
            Assert.True(Directory.Exists(stagedChildFolder));
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
    public async Task CatchUp_DoesNotRollBackSuccessfulDeliveryAfterSourceIsRemoved()
    {
        using var fixture = new QueueFixture();
        var source = fixture.Write("source.nc", "version A");
        var profile = fixture.Settings.WatchProfiles[0];
        var setup = fixture.Settings.ProcessingSetups[0];
        var destination = new DestinationSettings();
        var processor = CreateManifestProcessor(profile);
        var destinationService = new CapturingDestinationService();
        await using (var coordinator = fixture.Create(processor, destinationService: destinationService))
        {
            destinationService.UploadResult = (false, "Destination offline");
            Assert.False((await coordinator.ProcessPathAsync(source, profile, destination, setup)).Success);
            fixture.Write("source.nc", "version B");
            destinationService.UploadResult = (true, "Delivered");
            Assert.True((await coordinator.ProcessPathAsync(source, profile, destination, setup)).Success);
            Assert.Equal(2, destinationService.UploadCount);
        }

        File.Delete(source);
        await using var restarted = fixture.Create(processor, destinationService: destinationService);
        Assert.True((await restarted.CatchUpMissingItemsAsync(profile, destination, setup)).Success);
        Assert.Equal(2, destinationService.UploadCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CatchUp_DoesNotFallbackToOlderVersionWhenLatestStagingCannotBeVerified(bool deleteLatest)
    {
        using var fixture = new QueueFixture();
        var source = fixture.Write("source.nc", "version A");
        var profile = fixture.Settings.WatchProfiles[0];
        var setup = fixture.Settings.ProcessingSetups[0];
        var destination = new DestinationSettings();
        var processor = CreateManifestProcessor(profile);
        var destinationService = new CapturingDestinationService((false, "Destination offline"));
        ProcessingResult latest;
        await using (var coordinator = fixture.Create(processor, destinationService: destinationService))
        {
            Assert.False((await coordinator.ProcessPathAsync(source, profile, destination, setup)).Success);
            fixture.Write("source.nc", "version B");
            latest = await coordinator.ProcessPathAsync(source, profile, destination, setup);
            Assert.False(latest.Success);
            Assert.NotNull(latest.CompletedOutput);
        }

        if (deleteLatest)
        {
            Directory.Delete(latest.CompletedOutput.JobDirectory, recursive: true);
        }
        else
        {
            File.WriteAllText(latest.OutputPath, "corrupted");
        }

        File.Delete(source);
        destinationService.UploadResult = (true, "Delivered");
        await using var restarted = fixture.Create(processor, destinationService: destinationService);
        await restarted.CatchUpMissingItemsAsync(profile, destination, setup);
        Assert.Equal(2, destinationService.UploadCount);
    }

    private static IProjectProcessor CreateManifestProcessor(WatchProfileSettings profile, Func<string, string?>? remoteParent = null) =>
        new DelegateProcessor((path, token) =>
        {
            var job = CompletedStagedOutput.CreateJobDirectory(profile.StagingFolder);
            var output = Path.Combine(job, "program.nc");
            File.Copy(path, output);
            var completed = CompletedStagedOutput.Publish(job, profile.Id, path, output, remoteFolderName: null,
                WorkItemFingerprint.Capture(path, includeContent: true, token), token,
                remoteRelativeParent: remoteParent?.Invoke(path));
            return Task.FromResult(new ProcessingResult { Success = true, SourcePath = path, OutputPath = output, CompletedOutput = completed });
        });

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
    [InlineData("job-123/NC/program.nc", "job-123/NC/program.nc")]
    [InlineData("job-123/AutoStickLabel/label.jpg", "job-123/AutoStickLabel/label.jpg")]
    [InlineData("top-level-file.nc", "top-level-file.nc")]
    public void ResolveWorkItemPath_LeavesNestedChangesUntouchedInChangedFilesAndFoldersMode(string relativePath, string expectedRelativeWorkItem)
    {
        var watchFolder = Path.Combine(Path.GetTempPath(), $"cncsync-watch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(watchFolder);

        try
        {
            var profile = new WatchProfileSettings
            {
                WatchFolder = watchFolder,
                WorkItemMode = WatchProfileWorkItemMode.ChangedFilesAndFolders
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

    [Theory]
    [InlineData("job-123", "job-123")]
    [InlineData("job-123/NC/program.nc", "job-123")]
    [InlineData("job-123/AutoStickLabel/label.jpg", "job-123")]
    [InlineData("top-level-file.nc", "top-level-file.nc")]
    public void ResolveWorkItemPath_CollapsesNestedChangesToTopLevelWatchItemInTopLevelChildFoldersMode(string relativePath, string expectedRelativeWorkItem)
    {
        var watchFolder = Path.Combine(Path.GetTempPath(), $"cncsync-watch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(watchFolder);

        try
        {
            var profile = new WatchProfileSettings
            {
                WatchFolder = watchFolder,
                WorkItemMode = WatchProfileWorkItemMode.TopLevelChildFolders
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

    private sealed class EmittingMonitor : IFolderMonitor
    {
        public event Action<WorkItemReadyEvent>? WorkItemReady;
        public bool IsRunning { get; private set; }
        public Task StartAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            IsRunning = true;
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsRunning = false;
            return Task.CompletedTask;
        }
        public void Emit(WatchProfileSettings profile, string path) => WorkItemReady?.Invoke(new WorkItemReadyEvent { Profile = profile, Path = path });
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DelegateProcessor(Func<string, ProCutApiSettings?, CancellationToken, Task<ProcessingResult>> process) : IProjectProcessor
    {
        public DelegateProcessor(Func<string, CancellationToken, Task<ProcessingResult>> process)
            : this((path, _, token) => process(path, token))
        {
        }

        public Task<ProcessingResult> ProcessAsync(string sourcePath, WatchProfileSettings profile, ProcessingSetupSettings processingSetup, ProCutApiSettings? proCutApi = null, CancellationToken cancellationToken = default) =>
            process(sourcePath, proCutApi, cancellationToken);
    }

    private sealed class QueueFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"cncsync-queue-{Guid.NewGuid():N}");
        private EmittingMonitor _monitor = new();
        public AppSettings Settings { get; }

        public QueueFixture()
        {
            Settings = AppSettings.CreateDefault();
            Settings.Destinations.Clear();
            Settings.WatchProfiles[0].DestinationId = string.Empty;
            Settings.WatchProfiles[0].WatchFolder = Directory.CreateDirectory(Path.Combine(_root, "input")).FullName;
            Settings.WatchProfiles[0].StagingFolder = Path.Combine(_root, "stage");
        }

        public SyncCoordinator Create(IProjectProcessor processor, TimeProvider? clock = null, IDestinationService? destinationService = null)
        {
            _monitor = new EmittingMonitor();
            return new SyncCoordinator(_monitor, processor, destinationService ?? new CapturingDestinationService(), new AppSettingsValidator(), timeProvider: clock);
        }

        public string Write(string name, string content = "G1 X1")
        {
            var path = Path.Combine(Settings.WatchProfiles[0].WatchFolder, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public void UseApiProcessing()
        {
            Settings.ProcessingSetups[0].Mode = ProcessingMode.ProCutApi;
            Settings.ProCutApi.ApiKey = TestSecrets.ProCutApiKey;
        }

        public void AddLocalDestination()
        {
            var destination = new DestinationSettings
            {
                Name = "Local output", Type = DestinationType.LocalFolder, LocalRootPath = Path.Combine(_root, "output")
            };
            Settings.Destinations.Add(destination);
            Settings.WatchProfiles[0].DestinationId = destination.Id;
        }

        public void Emit(string path) => _monitor.Emit(Settings.WatchProfiles[0], path);
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }

    private sealed class AdjustableClock : TimeProvider
    {
        private long _elapsedTicks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(Interlocked.Read(ref _elapsedTicks));
        public void Advance(TimeSpan amount) => Interlocked.Add(ref _elapsedTicks, amount.Ticks);
    }

    private sealed class StubProjectProcessor(
        string outputPath,
        string? remoteFolderName = null,
        Func<string, ProcessingResult>? processingResultFactory = null) : IProjectProcessor
    {
        public Task<ProcessingResult> ProcessAsync(
            string sourcePath,
            WatchProfileSettings profile,
            ProcessingSetupSettings processingSetup,
            ProCutApiSettings? proCutApi = null,
            CancellationToken cancellationToken = default) =>
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
        public int UploadCount { get; private set; }
        public List<string> UploadPaths { get; } = [];
        public (bool Success, string Message) UploadResult { get; set; } = uploadResult ?? (true, "uploaded");
        public string? LastUploadRemoteDirectoryPath { get; private set; }
        public string? LastDeletedRemotePath { get; private set; }

        public Task<(bool Success, string Message)> TestConnectionAsync(DestinationSettings destination, CancellationToken cancellationToken = default) =>
            Task.FromResult<(bool Success, string Message)>((true, "ok"));

        public Task<(bool Success, string Message)> UploadDirectoryAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            LastUploadRemoteDirectoryPath = remoteDirectoryPath;
            return Task.FromResult(UploadResult);
        }

        public Task<(bool Success, string Message)> UploadFileSystemItemAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            UploadCount++;
            UploadPaths.Add(remoteDirectoryPath);
            LastUploadRemoteDirectoryPath = remoteDirectoryPath;
            return Task.FromResult(UploadResult);
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
