using CNCSync.Core.Configuration;
using CNCSync.Core.Services;
using Renci.SshNet;

namespace CNCSync.Infrastructure.Networking;

public sealed class ScpService : IScpService, IDisposable
{
    private static readonly TimeSpan BrowserSessionIdleTimeout = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _browserOperationLock = new(1, 1);
    private readonly object _browserSessionGate = new();
    private readonly Timer _browserSessionTimer;
    private CachedBrowserSession? _cachedBrowserSession;
    private bool _disposed;

    public ScpService()
    {
        _browserSessionTimer = new Timer(OnBrowserSessionTimerElapsed, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public Task<(bool Success, string Message)> TestConnectionAsync(DestinationSettings destination, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var validation = ValidateSshDestination(destination, "SCP");
            if (!validation.Success)
            {
                return validation;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var client = CreateSshClient(destination);
                client.Connect();
                return (true, $"SCP connection successful: {destination.Host}:{destination.Port}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (false, $"SCP connection failed: {ex.Message}");
            }
        }, cancellationToken);

    public Task<(bool Success, string Message)> UploadDirectoryAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var validation = ValidateSshDestination(destination, "SCP");
            if (!validation.Success)
            {
                return validation;
            }

            if (!Directory.Exists(localPath))
            {
                return (false, $"SCP upload skipped because local path does not exist: {localPath}");
            }

            try
            {
                using var sshClient = CreateSshClient(destination);
                using var scpClient = CreateScpClient(destination);
                sshClient.Connect();
                scpClient.Connect();

                var targetRoot = NormalizeSessionDirectoryPath(remoteDirectoryPath);
                EnsureDirectoryExists(sshClient, targetRoot, cancellationToken);

                foreach (var file in FileSystemItemFilter.EnumerateIncludedFiles(localPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var relativePath = Path.GetRelativePath(localPath, file).Replace('\\', '/');
                    var remoteFilePath = CombineRemoteFilePath(targetRoot, relativePath);
                    var remoteDirectory = GetParentRemotePath(remoteFilePath);
                    EnsureDirectoryExists(sshClient, remoteDirectory, cancellationToken);

                    using var fileStream = File.OpenRead(file);
                    scpClient.Upload(fileStream, remoteFilePath);
                }

                return (true, $"SCP upload completed to {DescribeRemotePath(targetRoot)}: {localPath}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (false, $"SCP upload failed: {ex.Message}");
            }
        }, cancellationToken);

    public async Task<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)> ListRootEntriesAsync(DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default)
    {
        var validation = ValidateSshDestination(destination, "SCP");
        if (!validation.Success)
        {
            return (false, [], validation.Message);
        }

        await _browserOperationLock.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var sessionLease = AcquireBrowserSession(destination);
            var targetPath = NormalizeSessionDirectoryPath(remoteDirectoryPath);

            var command = BuildListDirectoryCommand(targetPath);
            var result = RunCommand(sessionLease.Client, command, "SCP listing");
            if (!result.Success)
            {
                return (false, [], result.Message);
            }

            if (ContainsMissingMarker(result.Output))
            {
                return (true, [], "SCP path does not exist yet; treating it as empty.");
            }

            var entries = ParseDirectoryEntries(result.Output);
            var describedPath = DescribeRemotePath(targetPath);
            return (true, entries, $"Listed {entries.Count} item(s) from SCP path {describedPath}.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (false, [], $"SCP listing failed: {ex.Message}");
        }
        finally
        {
            _browserOperationLock.Release();
        }
    }

    public async Task<(bool Exists, long? SizeBytes, string Message)> TryGetFileSizeAsync(DestinationSettings destination, string remoteFilePath, CancellationToken cancellationToken = default)
    {
        var validation = ValidateSshDestination(destination, "SCP");
        if (!validation.Success)
        {
            return (false, null, validation.Message);
        }

        await _browserOperationLock.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var sessionLease = AcquireBrowserSession(destination);
            var normalizedPath = NormalizeSessionPath(remoteFilePath);
            var result = RunCommand(sessionLease.Client, BuildReadFileSizeCommand(normalizedPath), "SCP size check");
            if (!result.Success)
            {
                return (false, null, result.Message);
            }

            if (result.Output.StartsWith("FILE\t", StringComparison.Ordinal) &&
                long.TryParse(result.Output["FILE\t".Length..].Trim(), out var sizeBytes))
            {
                return (true, sizeBytes, $"Remote file exists: {DescribeRemotePath(normalizedPath)}");
            }

            if (string.Equals(result.Output.Trim(), "DIR", StringComparison.Ordinal))
            {
                return (false, null, $"Remote path is a directory: {DescribeRemotePath(normalizedPath)}");
            }

            return (false, null, $"Remote file does not exist: {DescribeRemotePath(normalizedPath)}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (false, null, $"Could not read SCP file size: {ex.Message}");
        }
        finally
        {
            _browserOperationLock.Release();
        }
    }

    public Task<(bool Success, string Message)> DeleteRemoteItemAsync(DestinationSettings destination, string remotePath, bool isDirectory, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var validation = ValidateSshDestination(destination, "SCP");
            if (!validation.Success)
            {
                return validation;
            }

            try
            {
                using var client = CreateSshClient(destination);
                client.Connect();

                var normalizedPath = NormalizeSessionPath(remotePath);
                if (normalizedPath == ".")
                {
                    return (true, "Remote root cleanup skipped.");
                }

                var result = RunCommand(client, BuildDeleteCommand(normalizedPath, isDirectory), "SCP delete");
                if (!result.Success)
                {
                    return (false, result.Message);
                }

                return (true, $"{(isDirectory ? "Remote folder" : "Remote file")} deleted: {DescribeRemotePath(normalizedPath)}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (false, $"SCP delete failed: {ex.Message}");
            }
        }, cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _browserSessionTimer.Dispose();
        DisposeCachedBrowserSession();
        _browserOperationLock.Dispose();
    }

    private static (bool Success, string Message) ValidateSshDestination(DestinationSettings destination, string protocolLabel)
    {
        if (string.IsNullOrWhiteSpace(destination.Host))
        {
            return (false, $"{protocolLabel} host is not configured.");
        }

        if (string.IsNullOrWhiteSpace(destination.Username))
        {
            return (false, $"{protocolLabel} username is not configured.");
        }

        if (string.IsNullOrWhiteSpace(destination.Password))
        {
            return (false, $"{protocolLabel} password is not configured.");
        }

        return (true, string.Empty);
    }

    private BrowserSessionLease AcquireBrowserSession(DestinationSettings destination)
    {
        lock (_browserSessionGate)
        {
            ThrowIfDisposed();

            var connectionKey = BuildConnectionKey(destination);
            if (_cachedBrowserSession is not null &&
                !_cachedBrowserSession.IsExpired &&
                string.Equals(_cachedBrowserSession.ConnectionKey, connectionKey, StringComparison.Ordinal) &&
                _cachedBrowserSession.Client.IsConnected)
            {
                _cachedBrowserSession.Touch();
                ScheduleBrowserSessionExpiry();
                return new BrowserSessionLease(_cachedBrowserSession.Client);
            }

            DisposeCachedBrowserSessionUnsafe();

            var client = CreateSshClient(destination);
            client.Connect();
            _cachedBrowserSession = new CachedBrowserSession(connectionKey, client);
            ScheduleBrowserSessionExpiry();
            return new BrowserSessionLease(client);
        }
    }

    private void ScheduleBrowserSessionExpiry()
    {
        _browserSessionTimer.Change(BrowserSessionIdleTimeout, Timeout.InfiniteTimeSpan);
    }

    private void OnBrowserSessionTimerElapsed(object? state)
    {
        lock (_browserSessionGate)
        {
            if (_cachedBrowserSession is null)
            {
                return;
            }

            if (_cachedBrowserSession.IsExpired)
            {
                DisposeCachedBrowserSessionUnsafe();
                return;
            }

            var remaining = BrowserSessionIdleTimeout - (DateTime.UtcNow - _cachedBrowserSession.LastUsedUtc);
            _browserSessionTimer.Change(remaining > TimeSpan.Zero ? remaining : Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private void DisposeCachedBrowserSession()
    {
        lock (_browserSessionGate)
        {
            DisposeCachedBrowserSessionUnsafe();
        }
    }

    private void DisposeCachedBrowserSessionUnsafe()
    {
        if (_cachedBrowserSession is null)
        {
            return;
        }

        try
        {
            if (_cachedBrowserSession.Client.IsConnected)
            {
                _cachedBrowserSession.Client.Disconnect();
            }
        }
        catch
        {
        }

        _cachedBrowserSession.Client.Dispose();
        _cachedBrowserSession = null;
        _browserSessionTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private static IReadOnlyList<RemoteEntryInfo> ParseDirectoryEntries(string output)
    {
        var entries = new List<RemoteEntryInfo>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (ContainsMissingMarker(line))
            {
                continue;
            }

            var columns = line.Split('\t');
            if (columns.Length < 4)
            {
                continue;
            }

            var isDirectory = string.Equals(columns[0], "d", StringComparison.Ordinal);
            long? sizeBytes = isDirectory || !long.TryParse(columns[1], out var parsedSize) ? null : parsedSize;
            entries.Add(new RemoteEntryInfo
            {
                Name = columns[2],
                FullPath = NormalizeRemotePath(columns[3]),
                IsDirectory = isDirectory,
                SizeBytes = sizeBytes
            });
        }

        return entries
            .OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool ContainsMissingMarker(string output) =>
        output.Contains("__CNCSYNC_MISSING__", StringComparison.Ordinal);

    private static (bool Success, string Output, string Message) RunCommand(SshClient client, string commandText, string operationLabel)
    {
        var command = client.RunCommand(commandText);
        if (command.ExitStatus != 0)
        {
            var error = string.IsNullOrWhiteSpace(command.Error) ? command.Result.Trim() : command.Error.Trim();
            if (string.IsNullOrWhiteSpace(error))
            {
                error = $"Exit status {command.ExitStatus}";
            }

            return (false, string.Empty, $"{operationLabel} failed: {error}");
        }

        return (true, command.Result.Trim(), string.Empty);
    }

    private static SshClient CreateSshClient(DestinationSettings destination)
    {
        var port = destination.Port > 0 ? destination.Port : 22;
        return new SshClient(destination.Host, port, destination.Username, destination.Password);
    }

    private static ScpClient CreateScpClient(DestinationSettings destination)
    {
        var port = destination.Port > 0 ? destination.Port : 22;
        return new ScpClient(destination.Host, port, destination.Username, destination.Password);
    }

    private static void EnsureDirectoryExists(SshClient client, string? remotePath, CancellationToken cancellationToken)
    {
        var normalizedPath = NormalizeSessionDirectoryPath(remotePath);
        if (string.IsNullOrWhiteSpace(normalizedPath) || normalizedPath is "/" or ".")
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var result = RunCommand(client, $"mkdir -p -- {EscapeShellArgument(normalizedPath)}", "SCP mkdir");
        if (!result.Success)
        {
            throw new InvalidOperationException(result.Message);
        }
    }

    private static string BuildListDirectoryCommand(string remoteDirectoryPath)
    {
        var escapedPath = EscapeShellArgument(remoteDirectoryPath);
        return
            $"dir={escapedPath}; " +
            "if [ ! -d \"$dir\" ]; then printf '__CNCSYNC_MISSING__\\n'; exit 0; fi; " +
            "for entry in \"$dir\"/* \"$dir\"/.[!.]* \"$dir\"/..?*; do " +
            "if [ ! -e \"$entry\" ]; then continue; fi; " +
            "name=$(basename \"$entry\"); " +
            "if [ -d \"$entry\" ]; then printf 'd\\t\\t%s\\t%s\\n' \"$name\" \"$entry\"; " +
            "else size=$(wc -c < \"$entry\" | tr -d '[:space:]'); printf 'f\\t%s\\t%s\\t%s\\n' \"$size\" \"$name\" \"$entry\"; fi; " +
            "done";
    }

    private static string BuildReadFileSizeCommand(string remoteFilePath)
    {
        var escapedPath = EscapeShellArgument(remoteFilePath);
        return
            $"path={escapedPath}; " +
            "if [ -f \"$path\" ]; then size=$(wc -c < \"$path\" | tr -d '[:space:]'); printf 'FILE\\t%s\\n' \"$size\"; " +
            "elif [ -d \"$path\" ]; then printf 'DIR\\n'; " +
            "else printf 'MISSING\\n'; fi";
    }

    private static string BuildDeleteCommand(string remotePath, bool isDirectory)
    {
        var escapedPath = EscapeShellArgument(remotePath);
        return isDirectory
            ? $"path={escapedPath}; if [ -e \"$path\" ]; then rm -rf -- \"$path\"; fi"
            : $"path={escapedPath}; if [ -f \"$path\" ]; then rm -f -- \"$path\"; fi";
    }

    private static string EscapeShellArgument(string value) =>
        $"'{value.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";

    private static string NormalizeRemotePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.Trim().Replace('\\', '/').Trim('/');
        return string.IsNullOrWhiteSpace(trimmed) ? string.Empty : $"/{trimmed}";
    }

    private static string CombineRemotePath(string? basePath, string? childPath)
    {
        var segments = new[] { basePath, childPath }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var combined = string.Join("/", segments);
        return string.IsNullOrWhiteSpace(combined) ? string.Empty : $"/{combined}";
    }

    private static string NormalizeSessionDirectoryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ".";
        }

        return NormalizeRemotePath(path);
    }

    private static string NormalizeSessionPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ".";
        }

        return NormalizeRemotePath(path);
    }

    private static string CombineRemoteFilePath(string basePath, string relativePath)
    {
        var normalizedRelativePath = relativePath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(basePath) || basePath == ".")
        {
            return normalizedRelativePath;
        }

        return CombineRemotePath(basePath, normalizedRelativePath);
    }

    private static string GetParentRemotePath(string? remoteFilePath)
    {
        var normalizedPath = NormalizeRemotePath(remoteFilePath);
        if (string.IsNullOrWhiteSpace(normalizedPath) || normalizedPath == "/")
        {
            return string.Empty;
        }

        var lastSeparatorIndex = normalizedPath.LastIndexOf('/');
        if (lastSeparatorIndex <= 0)
        {
            return string.Empty;
        }

        return normalizedPath[..lastSeparatorIndex];
    }

    private static string BuildConnectionKey(DestinationSettings destination) =>
        $"{destination.Host}\n{destination.Port}\n{destination.Username}\n{destination.Password}";

    private static string DescribeRemotePath(string path) =>
        string.IsNullOrWhiteSpace(path) || path == "." ? "/" : path;

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class CachedBrowserSession(string connectionKey, SshClient client)
    {
        public string ConnectionKey { get; } = connectionKey;
        public SshClient Client { get; } = client;
        public DateTime LastUsedUtc { get; private set; } = DateTime.UtcNow;
        public bool IsExpired => DateTime.UtcNow - LastUsedUtc >= BrowserSessionIdleTimeout;
        public void Touch() => LastUsedUtc = DateTime.UtcNow;
    }

    private readonly struct BrowserSessionLease(SshClient client) : IDisposable
    {
        public SshClient Client { get; } = client;
        public void Dispose()
        {
        }
    }
}
