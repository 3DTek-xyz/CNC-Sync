using System.Threading.Channels;
using CNCSync.Core.Configuration;
using CNCSync.Core.Services;
using CNCSync.Infrastructure.Monitoring;

namespace CNCSync.Tests;

public sealed class FolderMonitorRecoveryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("cncsync-watch-recovery-").FullName;

    [Fact]
    public async Task StartLeavesExistingInputIdleAndWaitsForALiveChangeToSettle()
    {
        var profile = CreateProfile();
        profile.StabilityDelaySeconds = 2;
        var source = Path.Combine(_root, "existing.nc");
        await File.WriteAllTextAsync(source, "G0 X0");
        var ready = Channel.CreateUnbounded<WorkItemReadyEvent>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var monitor = new FileSystemFolderMonitor();
        monitor.WorkItemReady += item => ready.Writer.TryWrite(item);
        await monitor.StartAsync(new AppSettings { WatchProfiles = [profile] }, timeout.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(4200), timeout.Token);
        Assert.False(ready.Reader.TryRead(out _));
        await File.AppendAllTextAsync(source, "\nG1 X10", timeout.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(1150), timeout.Token);
        Assert.False(ready.Reader.TryRead(out _));

        var item = await ready.Reader.ReadAsync(timeout.Token);
        Assert.Equal(source, item.Path);
        Assert.Equal(WorkItemFingerprint.Capture(source, includeContent: false), item.StableMetadataFingerprint);
    }

    [Fact]
    public async Task RestartQueuesOnlyFilesCreatedAfterMonitoringResumes()
    {
        var profile = CreateProfile();
        var ready = Channel.CreateUnbounded<WorkItemReadyEvent>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var monitor = new FileSystemFolderMonitor();
        monitor.WorkItemReady += item => ready.Writer.TryWrite(item);
        var settings = new AppSettings { WatchProfiles = [profile] };
        await monitor.StartAsync(settings, timeout.Token);
        await monitor.StopAsync(timeout.Token);
        var source = Path.Combine(_root, "offline.nc");
        await File.WriteAllTextAsync(source, "G0 X0", timeout.Token);
        await monitor.StartAsync(settings, timeout.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(3200), timeout.Token);
        Assert.False(ready.Reader.TryRead(out _));

        var liveFile = Path.Combine(_root, "live.nc");
        await File.WriteAllTextAsync(liveFile, "G0 X1", timeout.Token);
        Assert.Equal(liveFile, (await ready.Reader.ReadAsync(timeout.Token)).Path);
    }

    [Fact]
    public async Task WatcherOverflowReconnectsWithoutQueuingExistingInput()
    {
        var profile = CreateProfile();
        var source = Path.Combine(_root, "existing.nc");
        await File.WriteAllTextAsync(source, "G0 X0");
        var ready = Channel.CreateUnbounded<WorkItemReadyEvent>();
        var reported = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var monitor = new FileSystemFolderMonitor();
        monitor.WorkItemReady += item => ready.Writer.TryWrite(item);
        monitor.MonitoringError += (_, message) => reported.TrySetResult(message);
        await monitor.StartAsync(new AppSettings { WatchProfiles = [profile] }, timeout.Token);
        monitor.SignalWatcherErrorForTesting(profile, new InternalBufferOverflowException("overflow test"));

        Assert.Contains("overflow test", await reported.Task.WaitAsync(timeout.Token));
        await Task.Delay(TimeSpan.FromMilliseconds(3200), timeout.Token);
        Assert.False(ready.Reader.TryRead(out _));

        var liveFile = Path.Combine(_root, "after-reconnect.nc");
        await File.WriteAllTextAsync(liveFile, "G0 X1", timeout.Token);
        Assert.Equal(liveFile, (await ready.Reader.ReadAsync(timeout.Token)).Path);
    }

    [Fact]
    public async Task IndividualApiModeQueuesAnArrivingFolderWithoutQueuingExistingSiblings()
    {
        var profile = CreateProfile();
        profile.WatchFolder = Directory.CreateDirectory(Path.Combine(_root, "watch")).FullName;
        var setup = new ProcessingSetupSettings { Id = "api", Mode = ProcessingMode.ProCutApi };
        profile.ProcessingSetupId = setup.Id;
        var history = Directory.CreateDirectory(Path.Combine(profile.WatchFolder, "history")).FullName;
        await File.WriteAllTextAsync(Path.Combine(history, "old.nc"), "G0 X0");
        var incoming = Directory.CreateDirectory(Path.Combine(_root, "incoming")).FullName;
        await File.WriteAllTextAsync(Path.Combine(incoming, "new.nc"), "G0 X1");
        var ready = Channel.CreateUnbounded<WorkItemReadyEvent>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        await using var monitor = new FileSystemFolderMonitor();
        monitor.WorkItemReady += item => ready.Writer.TryWrite(item);
        await monitor.StartAsync(new AppSettings { WatchProfiles = [profile], ProcessingSetups = [setup] }, timeout.Token);

        Directory.SetLastWriteTimeUtc(history, DateTime.UtcNow.AddMinutes(-1));
        await Task.Delay(TimeSpan.FromMilliseconds(4200), timeout.Token);
        Assert.False(ready.Reader.TryRead(out var unexpected), $"Unexpected work item: {unexpected?.Path}");

        var job = Path.Combine(profile.WatchFolder, "new-export");
        Directory.Move(incoming, job);
        Directory.SetLastWriteTimeUtc(job, DateTime.UtcNow);
        Assert.Equal(Path.Combine(job, "new.nc"), (await ready.Reader.ReadAsync(timeout.Token)).Path);
    }

    [Fact]
    public async Task GroupedModeQueuesOnlyTheTopLevelProjectWithALiveChange()
    {
        var profile = CreateProfile();
        profile.WorkItemMode = WatchProfileWorkItemMode.TopLevelChildFolders;
        var job = Directory.CreateDirectory(Path.Combine(_root, "job")).FullName;
        var unchangedJob = Directory.CreateDirectory(Path.Combine(_root, "unchanged-job")).FullName;
        var existingFile = Path.Combine(job, "panel.nc");
        await File.WriteAllTextAsync(existingFile, "G0 X0");
        await File.WriteAllTextAsync(Path.Combine(unchangedJob, "panel.nc"), "G0 X0");
        var ready = Channel.CreateUnbounded<WorkItemReadyEvent>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var monitor = new FileSystemFolderMonitor();
        monitor.WorkItemReady += item => ready.Writer.TryWrite(item);
        await monitor.StartAsync(new AppSettings { WatchProfiles = [profile] }, timeout.Token);

        await File.AppendAllTextAsync(existingFile, "\nG0 X1", timeout.Token);
        Assert.Equal(job, (await ready.Reader.ReadAsync(timeout.Token)).Path);
        await Task.Delay(TimeSpan.FromMilliseconds(2200), timeout.Token);
        Assert.False(ready.Reader.TryRead(out _));
    }

    [Fact]
    public async Task InitiallyUnavailableWatchReconnectsWithoutQueuingFilesAlreadyThere()
    {
        var profile = CreateProfile();
        profile.WatchFolder = Path.Combine(_root, "unavailable");
        var ready = Channel.CreateUnbounded<WorkItemReadyEvent>();
        var reported = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(22));
        await using var monitor = new FileSystemFolderMonitor();
        monitor.WorkItemReady += item => ready.Writer.TryWrite(item);
        monitor.MonitoringError += (_, message) => reported.TrySetResult(message);
        await monitor.StartAsync(new AppSettings { WatchProfiles = [profile] }, timeout.Token);
        Assert.Contains("Watcher unavailable", await reported.Task.WaitAsync(timeout.Token));

        Directory.CreateDirectory(profile.WatchFolder);
        await File.WriteAllTextAsync(Path.Combine(profile.WatchFolder, "offline.nc"), "G0 X0", timeout.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(13500), timeout.Token);
        Assert.False(ready.Reader.TryRead(out _));

        var liveFile = Path.Combine(profile.WatchFolder, "after-reconnect.nc");
        await File.WriteAllTextAsync(liveFile, "G0 X1", timeout.Token);
        Assert.Equal(liveFile, (await ready.Reader.ReadAsync(timeout.Token)).Path);
    }

    private WatchProfileSettings CreateProfile() => new()
    {
        WatchFolder = _root,
        StabilityDelaySeconds = 1,
        StabilityPollingSeconds = 1
    };

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
