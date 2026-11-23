using System.Collections.Concurrent;
using CNCSync.Core.Configuration;
using CNCSync.Core.Services;

namespace CNCSync.Infrastructure.Monitoring;

public sealed class FileSystemFolderMonitor(TimeProvider? timeProvider = null) : IFolderMonitor
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _lifecycle = new(1);
    private MonitorSession? _session;

    public event Action<WorkItemReadyEvent>? WorkItemReady;
    public event Action<string, string>? MonitoringError;
    public bool IsRunning => Volatile.Read(ref _session) is not null;

    public async Task StartAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            var profiles = settings.WatchProfiles.Where(profile => profile.Enabled).ToArray();
            if (_session is not null || profiles.Length == 0)
            {
                return;
            }

            Volatile.Write(ref _session, new MonitorSession(settings, profiles, _clock,
                item => WorkItemReady?.Invoke(item),
                (profile, message) => MonitoringError?.Invoke(profile.Name, message)));
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            var session = Interlocked.Exchange(ref _session, null);
            if (session is not null)
            {
                await session.StopAsync();
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public void RequestRecheck(WorkItemReadyEvent item) => Volatile.Read(ref _session)?.QueuePath(item.Profile, item.Path, changed: false);

    internal void SignalWatcherErrorForTesting(WatchProfileSettings profile, Exception error) =>
        Volatile.Read(ref _session)?.SignalWatcherError(profile, error);

    public async ValueTask DisposeAsync() => await StopAsync();

    private static string ResolveWorkItemPath(WatchProfileSettings profile, string path)
    {
        if (profile.WorkItemMode != WatchProfileWorkItemMode.TopLevelChildFolders)
        {
            return path;
        }

        var relativePath = Path.GetRelativePath(profile.WatchFolder, path);
        if (!IsIncludedRelativePath(relativePath))
        {
            return path;
        }

        var firstSegment = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return Path.Combine(Path.GetFullPath(profile.WatchFolder), firstSegment);
    }

    internal static string ResolveWorkItemPathForTesting(WatchProfileSettings profile, string path) =>
        ResolveWorkItemPath(profile, path);

    private static bool IsIncludedRelativePath(string relativePath) =>
        !Path.IsPathRooted(relativePath) &&
        relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .All(segment => !FileSystemItemFilter.ShouldIgnoreFileSystemItem(segment));

    private sealed class MonitorSession
    {
        private readonly TimeProvider _clock;
        private readonly Action<WorkItemReadyEvent> _onReady;
        private readonly Action<WatchProfileSettings, string> _onError;
        private readonly ConcurrentDictionary<(string ProfileId, string Path), Observation> _pending = new();
        private readonly ConcurrentDictionary<string, (WatchProfileSettings Profile, Exception Error)> _faults = new();
        private readonly Dictionary<string, FileSystemWatcher> _watchers = new();
        private readonly Dictionary<string, (WatchProfileSettings Profile, DateTimeOffset Due)> _rescans = new();
        private readonly HashSet<string> _apiFileProfiles;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;

        public MonitorSession(AppSettings settings, WatchProfileSettings[] profiles, TimeProvider clock,
            Action<WorkItemReadyEvent> onReady, Action<WatchProfileSettings, string> onError)
        {
            _clock = clock;
            _onReady = onReady;
            _onError = onError;
            _apiFileProfiles = profiles.Where(profile => profile.WorkItemMode == WatchProfileWorkItemMode.ChangedFilesAndFolders &&
                    settings.ProcessingSetups.Any(setup => setup.Mode == ProcessingMode.ProCutApi &&
                        string.Equals(setup.Id, profile.ProcessingSetupId, StringComparison.OrdinalIgnoreCase)))
                .Select(profile => profile.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in profiles)
            {
                _rescans[profile.Id] = (profile, clock.GetUtcNow());
                TryAttachWatcher(profile);
            }

            _loop = Task.Run(() => PollAsync(_stop.Token));
        }

        public async Task StopAsync()
        {
            await _stop.CancelAsync();
            try
            {
                await _loop;
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
            finally
            {
                foreach (var watcher in _watchers.Values)
                {
                    watcher.Dispose();
                }

                _stop.Dispose();
            }
        }

        public void QueuePath(WatchProfileSettings profile, string path, bool changed)
        {
            if (_stop.IsCancellationRequested || !IsIncludedRelativePath(Path.GetRelativePath(profile.WatchFolder, path)))
            {
                return;
            }

            var workPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ResolveWorkItemPath(profile, path)));
            var key = (profile.Id, OperatingSystem.IsWindows() ? workPath.ToUpperInvariant() : workPath);
            var observation = new Observation(profile, workPath, _clock.GetUtcNow());
            if (changed)
            {
                _pending[key] = observation;
            }
            else
            {
                _pending.TryAdd(key, observation);
            }
        }

        public void SignalWatcherError(WatchProfileSettings profile, Exception error) => _faults[profile.Id] = (profile, error);

        private void TryAttachWatcher(WatchProfileSettings profile)
        {
            FileSystemWatcher? watcher = null;
            try
            {
                watcher = new FileSystemWatcher(profile.WatchFolder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastWrite |
                        NotifyFilters.CreationTime | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024
                };
                watcher.Created += (_, args) => QueuePath(profile, args.FullPath, changed: true);
                watcher.Changed += (_, args) => QueuePath(profile, args.FullPath, changed: true);
                watcher.Deleted += (_, args) => QueuePath(profile, args.FullPath, changed: true);
                watcher.Renamed += (_, args) =>
                {
                    QueuePath(profile, args.OldFullPath, changed: true);
                    QueuePath(profile, args.FullPath, changed: true);
                };
                watcher.Error += (_, args) => SignalWatcherError(profile, args.GetException());
                watcher.EnableRaisingEvents = true;
                _watchers.Add(profile.Id, watcher);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                watcher?.Dispose();
                _onError(profile, $"Watcher unavailable for {profile.WatchFolder}; rescan and reconnect will retry: {ex.Message}");
            }
        }

        private async Task PollAsync(CancellationToken token)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), _clock);
            do
            {
                token.ThrowIfCancellationRequested();
                RecoverWatchers(token);
                foreach (var entry in _pending)
                {
                    token.ThrowIfCancellationRequested();
                    Probe(entry, token);
                }
            }
            while (await timer.WaitForNextTickAsync(token));
        }

        private void RecoverWatchers(CancellationToken token)
        {
            var now = _clock.GetUtcNow();
            foreach (var (key, fault) in _faults)
            {
                token.ThrowIfCancellationRequested();
                if (!_faults.TryRemove(key, out var latest))
                {
                    continue;
                }

                _onError(latest.Profile, $"Watcher error for {latest.Profile.WatchFolder}; rescanning: {latest.Error.Message}");
                if (_watchers.Remove(key, out var previous))
                {
                    previous.Dispose();
                }

                _rescans[key] = (fault.Profile, now);
            }

            foreach (var (key, scan) in _rescans.Where(entry => entry.Value.Due <= now).ToArray())
            {
                token.ThrowIfCancellationRequested();
                if (!_watchers.ContainsKey(key))
                {
                    TryAttachWatcher(scan.Profile);
                }

                if (TryDiscover(scan.Profile, token) && _watchers.ContainsKey(key))
                {
                    _rescans.Remove(key);
                }
                else
                {
                    _rescans[key] = (scan.Profile, now.AddSeconds(10));
                }
            }
        }

        private bool TryDiscover(WatchProfileSettings profile, CancellationToken token)
        {
            try
            {
                DiscoverDirectory(profile, profile.WatchFolder, token);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _onError(profile, $"Watch folder rescan failed for {profile.WatchFolder}: {ex.Message}");
                return false;
            }
        }

        private void DiscoverDirectory(WatchProfileSettings profile, string path, CancellationToken token)
        {
            foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
            {
                token.ThrowIfCancellationRequested();
                if (FileSystemItemFilter.ShouldIgnoreFileSystemItem(entry.Name) ||
                    entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                var directory = entry.Attributes.HasFlag(FileAttributes.Directory);
                if (!directory || !_apiFileProfiles.Contains(profile.Id))
                {
                    QueuePath(profile, entry.FullName, changed: false);
                }

                if (directory && profile.WorkItemMode == WatchProfileWorkItemMode.ChangedFilesAndFolders)
                {
                    DiscoverDirectory(profile, entry.FullName, token);
                }
            }
        }

        private void Probe(KeyValuePair<(string ProfileId, string Path), Observation> entry, CancellationToken token)
        {
            var item = entry.Value;
            var now = _clock.GetUtcNow();
            var interval = TimeSpan.FromSeconds(item.Profile.StabilityPollingSeconds);
            if (item.NextCheck > now)
            {
                return;
            }

            try
            {
                if (HasUnsafePathComponent(item.Profile.WatchFolder, item.Path))
                {
                    _pending.TryRemove(entry);
                    return;
                }

                if (_apiFileProfiles.Contains(item.Profile.Id) && Directory.Exists(item.Path))
                {
                    // An individually watched API file should not be billed again as part of its parent directory.
                    DiscoverDirectory(item.Profile, item.Path, token);
                    _pending.TryRemove(entry);
                    return;
                }

                var fingerprint = WorkItemFingerprint.Capture(item.Path, includeContent: false, token);
                if (fingerprint != item.Fingerprint)
                {
                    _pending.TryUpdate(entry.Key, item with { Fingerprint = fingerprint, StableSince = now, NextCheck = now + interval }, item);
                }
                else if (now - item.StableSince < TimeSpan.FromSeconds(item.Profile.StabilityDelaySeconds))
                {
                    _pending.TryUpdate(entry.Key, item with { NextCheck = now + interval }, item);
                }
                else if (_pending.TryRemove(entry))
                {
                    token.ThrowIfCancellationRequested();
                    _onReady(new WorkItemReadyEvent { Path = item.Path, Profile = item.Profile, StableMetadataFingerprint = fingerprint });
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (ex is FileNotFoundException or DirectoryNotFoundException && !File.Exists(item.Path) && !Directory.Exists(item.Path))
                {
                    _pending.TryRemove(entry);
                    return;
                }

                if (_pending.TryUpdate(entry.Key, item with { Fingerprint = null, NextCheck = now + interval, ErrorReported = true }, item) &&
                    !item.ErrorReported)
                {
                    _onError(item.Profile, $"Could not verify input stability for {item.Path}: {ex.Message}");
                }
            }
        }

        private static bool HasUnsafePathComponent(string root, string path)
        {
            foreach (var segment in Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                root = Path.Combine(root, segment);
                if (File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
                {
                    return true;
                }
            }

            return false;
        }

        private sealed record Observation(WatchProfileSettings Profile, string Path, DateTimeOffset NextCheck,
            string? Fingerprint = null, DateTimeOffset StableSince = default, bool ErrorReported = false);
    }
}
