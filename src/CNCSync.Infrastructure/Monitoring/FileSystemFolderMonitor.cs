using System.Collections.Concurrent;
using CNCSync.Core.Configuration;
using CNCSync.Core.Services;

namespace CNCSync.Infrastructure.Monitoring;

public sealed class FileSystemFolderMonitor : IFolderMonitor
{
    private readonly ConcurrentDictionary<string, PendingWorkItem> _pendingPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<WatcherContext> _watchers = [];
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _timerCancellation;

    public event Action<WorkItemReadyEvent>? WorkItemReady;

    public bool IsRunning { get; private set; }

    public Task StartAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        foreach (var profile in settings.WatchProfiles.Where(profile => profile.Enabled))
        {
            var watcher = new FileSystemWatcher(profile.WatchFolder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
                InternalBufferSize = 64 * 1024
            };

            FileSystemEventHandler changedHandler = (_, args) => OnChanged(profile, args.FullPath);
            RenamedEventHandler renamedHandler = (_, args) => OnChanged(profile, args.FullPath);

            watcher.Created += changedHandler;
            watcher.Changed += changedHandler;
            watcher.Renamed += renamedHandler;

            _watchers.Add(new WatcherContext(profile, watcher, changedHandler, renamedHandler));
        }

        _timerCancellation = new CancellationTokenSource();
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        _ = Task.Run(() => PollAsync(_timer, _timerCancellation.Token), _timerCancellation.Token);

        IsRunning = _watchers.Count > 0;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning && _watchers.Count == 0)
        {
            return Task.CompletedTask;
        }

        foreach (var context in _watchers)
        {
            context.Watcher.EnableRaisingEvents = false;
            context.Watcher.Created -= context.ChangedHandler;
            context.Watcher.Changed -= context.ChangedHandler;
            context.Watcher.Renamed -= context.RenamedHandler;
            context.Watcher.Dispose();
        }

        _watchers.Clear();
        _timerCancellation?.Cancel();
        _timerCancellation?.Dispose();
        _timerCancellation = null;
        _timer?.Dispose();
        _timer = null;
        _pendingPaths.Clear();
        IsRunning = false;
        return Task.CompletedTask;
    }

    private void OnChanged(WatchProfileSettings profile, string path)
    {
        var workItemPath = ResolveWorkItemPath(profile, path);
        var key = BuildPendingKey(profile, workItemPath);
        var now = DateTime.UtcNow;
        _pendingPaths[key] = new PendingWorkItem(
            profile,
            workItemPath,
            now,
            now);
    }

    private async Task PollAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var now = DateTime.UtcNow;
            foreach (var entry in _pendingPaths.ToArray())
            {
                if (now - entry.Value.LastCheckedUtc < TimeSpan.FromSeconds(entry.Value.Profile.StabilityPollingSeconds))
                {
                    continue;
                }

                _pendingPaths[entry.Key] = entry.Value with { LastCheckedUtc = now };

                if (now - entry.Value.LastUpdatedUtc < TimeSpan.FromSeconds(entry.Value.Profile.StabilityDelaySeconds))
                {
                    continue;
                }

                if (_pendingPaths.TryRemove(entry.Key, out var pendingItem))
                {
                    if (Directory.Exists(pendingItem.Path) || File.Exists(pendingItem.Path))
                    {
                        WorkItemReady?.Invoke(new WorkItemReadyEvent
                        {
                            Path = pendingItem.Path,
                            Profile = pendingItem.Profile
                        });
                    }
                }
            }
        }
    }

    private static string ResolveWorkItemPath(WatchProfileSettings profile, string path)
    {
        if (Directory.Exists(path))
        {
            return path;
        }

        var fileDirectory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(fileDirectory))
        {
            return path;
        }

        if (string.Equals(fileDirectory, profile.WatchFolder, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return fileDirectory;
    }

    private static string BuildPendingKey(WatchProfileSettings profile, string path) => $"{profile.Id}:{path}";

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private sealed record PendingWorkItem(
        WatchProfileSettings Profile,
        string Path,
        DateTime LastUpdatedUtc,
        DateTime LastCheckedUtc);

    private sealed record WatcherContext(
        WatchProfileSettings Profile,
        FileSystemWatcher Watcher,
        FileSystemEventHandler ChangedHandler,
        RenamedEventHandler RenamedHandler);
}
