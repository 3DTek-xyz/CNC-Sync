using System.Threading.Channels;
using CNCSync.Core.Configuration;
using CNCSync.Core.Services;
using CNCSync.Infrastructure.Monitoring;

namespace CNCSync.Tests;

public sealed class FolderMonitorRecoveryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("cncsync-watch-recovery-").FullName;

    [Fact]
    public async Task StartDiscoversExistingInputAndWaitsForItToStopChanging()
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

        await Task.Delay(TimeSpan.FromMilliseconds(1150), timeout.Token);
        Assert.False(ready.Reader.TryRead(out _));
        await File.AppendAllTextAsync(source, "\nG1 X10", timeout.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(1150), timeout.Token);
        Assert.False(ready.Reader.TryRead(out _));

        var item = await ready.Reader.ReadAsync(timeout.Token);
        Assert.Equal(source, item.Path);
        Assert.Equal(WorkItemFingerprint.Capture(source, includeContent: false), item.StableMetadataFingerprint);
    }

    [Fact]
    public async Task RestartDiscoversFilesCreatedWhileStopped()
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

        Assert.Equal(source, (await ready.Reader.ReadAsync(timeout.Token)).Path);
    }

    [Fact]
    public async Task WatcherOverflowIsReportedAndRescansExistingInput()
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
        Assert.Equal(source, (await ready.Reader.ReadAsync(timeout.Token)).Path);

        monitor.SignalWatcherErrorForTesting(profile, new InternalBufferOverflowException("overflow test"));

        Assert.Contains("overflow test", await reported.Task.WaitAsync(timeout.Token));
        Assert.Equal(source, (await ready.Reader.ReadAsync(timeout.Token)).Path);
    }

    private WatchProfileSettings CreateProfile() => new()
    {
        WatchFolder = _root,
        StabilityDelaySeconds = 1,
        StabilityPollingSeconds = 1
    };

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
