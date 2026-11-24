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
        private readonly Dictionary<string, (WatchProfileSettings Profile, DateTimeOffset Due)> _reconnects = new();
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
                if (!TryAttachWatcher(profile))
                {
                    _reconnects[profile.Id] = (profile, clock.GetUtcNow().AddSeconds(10));
                }
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

        public void QueuePath(WatchProfileSettings profile, string path, bool changed, bool? directoryArrived = null)
        {
            if (_stop.IsCancellationRequested || !IsIncludedRelativePath(Path.GetRelativePath(profile.WatchFolder, path)))
            {
                return;
            }

            var workPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ResolveWorkItemPath(profile, path)));
            var key = (profile.Id, OperatingSystem.IsWindows() ? workPath.ToUpperInvariant() : workPath);
            var observation = new Observation(profile, workPath, _clock.GetUtcNow(), directoryArrived ?? false);
            if (changed)
            {
                _pending.AddOrUpdate(key, observation, (_, previous) =>
                    observation with { DirectoryArrived = directoryArrived ?? previous.DirectoryArrived });
            }
            else
            {
                _pending.TryAdd(key, observation);
            }
        }

        public void SignalWatcherError(WatchProfileSettings profile, Exception error) => _faults[profile.Id] = (profile, error);

        private bool TryAttachWatcher(WatchProfileSettings profile)
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
                var existingDirectories = GetExistingMacDirectories(profile);
                watcher.Created += (_, args) => QueuePath(profile, args.FullPath, changed: true,
                    directoryArrived: existingDirectories?.ContainsKey(args.FullPath) != true);
                watcher.Changed += (_, args) => QueuePath(profile, args.FullPath, changed: true);
                watcher.Deleted += (sender, args) =>
                {
                    existingDirectories?.TryRemove(args.FullPath, out _);
                    QueuePath(profile, args.FullPath, changed: true, directoryArrived: false);
                };
                watcher.Renamed += (sender, args) =>
                {
                    QueuePath(profile, args.OldFullPath, changed: true, directoryArrived: false);
                    if (existingDirectories?.TryRemove(args.OldFullPath, out _) == true)
                    {
                        existingDirectories.TryAdd(args.FullPath, 0);
                    }

                    var arrived = !IsIncludedRelativePath(Path.GetRelativePath(profile.WatchFolder, args.OldFullPath));
                    QueuePath(profile, args.FullPath, changed: true, directoryArrived: arrived);
                };
                watcher.Error += (_, args) => SignalWatcherError(profile, args.GetException());
                watcher.EnableRaisingEvents = true;
                _watchers.Add(profile.Id, watcher);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                watcher?.Dispose();
                _onError(profile, $"Watcher unavailable for {profile.WatchFolder}; reconnect will retry: {ex.Message}");
                return false;
            }
        }

        private static ConcurrentDictionary<string, byte>? GetExistingMacDirectories(WatchProfileSettings profile)
        {
            if (!OperatingSystem.IsMacOS() || profile.WorkItemMode != WatchProfileWorkItemMode.ChangedFilesAndFolders)
            {
                return null;
            }

            // FSEvents can report Created alongside a metadata change to an existing directory.
            // Record directory names so those events cannot queue the directory's existing contents.
            var directories = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden,
                IgnoreInaccessible = true
            };
            foreach (var path in Directory.EnumerateDirectories(profile.WatchFolder, "*", options))
            {
                directories.TryAdd(path, 0);
            }

            return directories;
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
            foreach (var key in _faults.Keys)
            {
                token.ThrowIfCancellationRequested();
                if (!_faults.TryRemove(key, out var latest))
                {
                    continue;
                }

                _onError(latest.Profile, $"Watcher error for {latest.Profile.WatchFolder}; reconnecting. Some changes may have been missed: {latest.Error.Message}");
                if (_watchers.Remove(key, out var previous))
                {
                    previous.Dispose();
                }

                _reconnects[key] = (latest.Profile, now);
            }

            foreach (var (key, reconnect) in _reconnects.Where(entry => entry.Value.Due <= now).ToArray())
            {
                token.ThrowIfCancellationRequested();
                if (TryAttachWatcher(reconnect.Profile))
                {
                    _reconnects.Remove(key);
                }
                else
                {
                    _reconnects[key] = (reconnect.Profile, now.AddSeconds(10));
                }
            }
        }

        private void QueueFilesInArrivingDirectory(WatchProfileSettings profile, string path, CancellationToken token)
        {
            foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
            {
                token.ThrowIfCancellationRequested();
                if (FileSystemItemFilter.ShouldIgnoreFileSystemItem(entry.Name) ||
                    entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                if (entry.Attributes.HasFlag(FileAttributes.Directory))
                {
                    QueueFilesInArrivingDirectory(profile, entry.FullName, token);
                }
                else
                {
                    QueuePath(profile, entry.FullName, changed: false);
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

                if (item.Profile.WorkItemMode == WatchProfileWorkItemMode.ChangedFilesAndFolders &&
                    Directory.Exists(item.Path))
                {
                    if (!item.DirectoryArrived)
                    {
                        _pending.TryRemove(entry);
                        return;
                    }

                    if (_apiFileProfiles.Contains(item.Profile.Id))
                    {
                        QueueFilesInArrivingDirectory(item.Profile, item.Path, token);
                        _pending.TryRemove(entry);
                        return;
                    }
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
            bool DirectoryArrived, string? Fingerprint = null,
            DateTimeOffset StableSince = default, bool ErrorReported = false);
    }
}
