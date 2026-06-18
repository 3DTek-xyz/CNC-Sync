using CNCSync.Core.Configuration;
using CNCSync.Core.Services;
using FluentFTP;
using FluentFTP.Exceptions;
using CNCSync.Infrastructure.Logging;
using System.Net.Sockets;

namespace CNCSync.Infrastructure.Networking;

public sealed class FtpService : IFtpService
{
    private const int RequestTimeoutMilliseconds = 8000;

    public async Task<(bool Success, string Message)> TestConnectionAsync(DestinationSettings destination, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destination.Host))
        {
            return (false, "FTP host is not configured.");
        }

        try
        {
            await using var client = CreateFluentClient(destination);
            await client.AutoConnect(cancellationToken);
            return (true, $"FTP connection successful: {destination.Host}:{destination.Port}");
        }
        catch (TimeoutException)
        {
            return (false, $"No FTP server responded at {destination.Host}:{destination.Port} within {RequestTimeoutMilliseconds / 1000} seconds.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.WriteException("FTP connection test failed.", ex);
            return (false, $"FTP connection failed: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> UploadDirectoryAsync(
        string localPath,
        DestinationSettings destination,
        string remoteDirectoryPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await UploadFileSystemItemAsync(localPath, destination, remoteDirectoryPath, progress, cancellationToken);
    }

    public async Task<(bool Success, string Message)> UploadFileSystemItemAsync(
        string localPath,
        DestinationSettings destination,
        string remoteDirectoryPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destination.Host))
        {
            return (false, "FTP upload skipped because no FTP host is configured.");
        }

        var isDirectory = Directory.Exists(localPath);
        var isFile = File.Exists(localPath);
        if (!isDirectory && !isFile)
        {
            return (false, $"FTP upload skipped because local path does not exist: {localPath}");
        }

        try
        {
            var targetRoot = string.IsNullOrWhiteSpace(remoteDirectoryPath) ? string.Empty : remoteDirectoryPath;
            AsyncFtpClient? client = null;
            async Task DisposeClientAsync()
            {
                if (client is null)
                {
                    return;
                }

                await client.DisposeAsync();
                client = null;
            }

            async Task<AsyncFtpClient> GetClientForUploadAttemptAsync(int attemptNumber, CancellationToken token)
            {
                if (attemptNumber == 1 && client is not null)
                {
                    return client;
                }

                await DisposeClientAsync();
                client = await ConnectClientAsync(destination, token);
                if (attemptNumber > 1)
                {
                    DiagnosticLog.WriteInfo($"FTP upload reconnected to {destination.Host}:{destination.Port} for retry attempt {attemptNumber - 1}/{FtpUploadRetryPolicy.MaxRetries}.");
                }

                return client;
            }

            try
            {
                client = await ConnectClientAsync(destination, cancellationToken);
                DiagnosticLog.WriteInfo($"FTP upload session connected to {destination.Host}:{destination.Port} using {destination.FtpDataMode} mode for local path '{localPath}' and remote root '{(string.IsNullOrWhiteSpace(targetRoot) ? "/" : targetRoot)}'.");
                await CreateDirectoryChainIfNeededAsync(client, targetRoot, cancellationToken);

                if (isFile)
                {
                    var fileName = Path.GetFileName(localPath);
                    if (FileSystemItemFilter.ShouldIgnoreFileSystemItem(fileName))
                    {
                        return (true, $"Skipped ignored file: {fileName}");
                    }

                    var remoteFilePath = CombineRemotePath(targetRoot, fileName);
                    progress?.Report($"Uploading {fileName}...");
                    await UploadSingleFileWithDiagnosticsAsync(GetClientForUploadAttemptAsync, DisposeClientAsync, localPath, remoteFilePath, fileIndex: 1, totalFiles: 1, cancellationToken);
                }
                else
                {
                    var files = FileSystemItemFilter.EnumerateIncludedFiles(localPath).ToList();
                    DiagnosticLog.WriteInfo($"FTP upload discovered {files.Count} file(s) under '{localPath}'.");
                    for (var index = 0; index < files.Count; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var file = files[index];

                        var relativePath = Path.GetRelativePath(localPath, file).Replace('\\', '/');
                        var remoteFilePath = CombineRemotePath(targetRoot, relativePath);
                        var remoteDir = Path.GetDirectoryName(remoteFilePath)?.Replace('\\', '/');
                        if (!string.IsNullOrWhiteSpace(remoteDir))
                        {
                            await CreateDirectoryChainIfNeededAsync(client, remoteDir, cancellationToken);
                        }

                        progress?.Report($"Uploading {index + 1}/{files.Count}: {relativePath}");
                        await UploadSingleFileWithDiagnosticsAsync(GetClientForUploadAttemptAsync, DisposeClientAsync, file, remoteFilePath, index + 1, files.Count, cancellationToken);
                    }
                }

                var targetDescription = string.IsNullOrWhiteSpace(targetRoot) ? "/" : targetRoot;
                return (true, $"FTP upload completed to {targetDescription}: {localPath}");
            }
            finally
            {
                await DisposeClientAsync();
            }
        }
        catch (TimeoutException)
        {
            return (false, $"FTP upload timed out because no FTP server responded at {destination.Host}:{destination.Port} within {RequestTimeoutMilliseconds / 1000} seconds.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.WriteException($"FTP upload failed for {localPath} to {remoteDirectoryPath}.", ex);
            return (false, $"FTP upload failed: {ex.Message}");
        }
    }

    public async Task<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)> ListRootEntriesAsync(
        DestinationSettings destination,
        string remoteDirectoryPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destination.Host))
        {
                return (false, [], "FTP listing skipped because no FTP host is configured.");
        }

        try
        {
            await using var client = CreateFluentClient(destination);
            await client.AutoConnect(cancellationToken);
            var fluentPath = ToFluentBrowserPath(remoteDirectoryPath);
            var names = await client.GetNameListing(fluentPath, cancellationToken);

            var entries = new List<RemoteEntryInfo>(names.Length);
            foreach (var rawName in names)
            {
                var entryName = rawName?
                    .Trim()
                    .Replace('\\', '/')
                    .TrimEnd('/')
                    .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .LastOrDefault();
                if (string.IsNullOrWhiteSpace(entryName))
                {
                    continue;
                }

                if (entryName is "." or "..")
                {
                    continue;
                }

                var fullPath = CombineRemotePath(remoteDirectoryPath, entryName);
                var sizeResult = await TryGetFileSizeAsync(destination, fullPath, cancellationToken);

                entries.Add(new RemoteEntryInfo
                {
                    Name = entryName,
                    FullPath = fullPath,
                    IsDirectory = !sizeResult.Exists,
                    SizeBytes = sizeResult.Exists ? sizeResult.SizeBytes : null
                });
            }

            var targetDescription = string.IsNullOrWhiteSpace(remoteDirectoryPath) ? "/" : remoteDirectoryPath;
            return (true, entries, $"Listed {entries.Count} item(s) from FTP path {targetDescription}.");
        }
        catch (TimeoutException)
        {
            return (false, [], $"No FTP server responded at {destination.Host}:{destination.Port} within {RequestTimeoutMilliseconds / 1000} seconds.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.WriteException($"FTP listing failed for {remoteDirectoryPath}.", ex);
            return (false, [], $"FTP listing failed: {ex.Message}");
        }
    }

    public async Task<(bool Exists, long? SizeBytes, string Message)> TryGetFileSizeAsync(
        DestinationSettings destination,
        string remoteFilePath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var client = CreateFluentClient(destination);
            await client.AutoConnect(cancellationToken);
            var sizeBytes = await client.GetFileSize(ToFluentBrowserPath(remoteFilePath), -1, cancellationToken);
            if (sizeBytes < 0)
            {
                return (false, null, $"Remote file does not exist: {remoteFilePath}");
            }

            return (true, sizeBytes, $"Remote file exists: {remoteFilePath}");
        }
        catch (Exception ex)
        {
            DiagnosticLog.WriteException($"FTP file size lookup failed for {remoteFilePath}.", ex);
            return (false, null, $"Could not read remote file size: {remoteFilePath}");
        }
    }

    public async Task<(bool Success, string Message)> DeleteRemoteItemAsync(
        DestinationSettings destination,
        string remotePath,
        bool isDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destination.Host))
        {
            return (false, "FTP delete skipped because no FTP host is configured.");
        }

        try
        {
            await using var client = CreateFluentClient(destination);
            await client.AutoConnect(cancellationToken);
            var fluentPath = ToFluentBrowserPath(remotePath);

            if (isDirectory)
            {
                await client.DeleteDirectory(fluentPath, cancellationToken);
            }
            else
            {
                await client.DeleteFile(fluentPath, cancellationToken);
            }

            return (true, $"{(isDirectory ? "Remote folder" : "Remote file")} deleted: {remotePath}");
        }
        catch (TimeoutException)
        {
            return (false, $"No FTP server responded at {destination.Host}:{destination.Port} within {RequestTimeoutMilliseconds / 1000} seconds.");
        }
        catch (Exception ex) when (IsRetryable550(ex))
        {
            DiagnosticLog.WriteInfo($"FTP delete intercepted 550 for {remotePath} (likely not found). Treating as success.");
            return (true, $"Remote item was already unavailable: {remotePath}");
        }
        catch (Exception ex)
        {
            DiagnosticLog.WriteException($"FTP delete failed for {remotePath}.", ex);
            return (false, $"FTP delete failed: {ex.Message}");
        }
    }

    private static async Task CreateDirectoryChainIfNeededAsync(
        AsyncFtpClient client,
        string? remotePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
        {
            return;
        }

        var segments = remotePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var currentPath = string.Empty;
        foreach (var segment in segments)
        {
            currentPath = CombineRemotePath(currentPath, segment);
            await CreateDirectoryIfNeededAsync(client, currentPath, cancellationToken);
        }
    }

    private static async Task CreateDirectoryIfNeededAsync(
        AsyncFtpClient client,
        string remotePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await client.CreateDirectory(ToFluentBrowserPath(remotePath), cancellationToken);
            DiagnosticLog.WriteInfo($"FTP ensured remote directory exists: {remotePath}");
        }
        catch
        {
            // Directory may already exist or the server may not support explicit create checks cleanly.
        }
    }

    private static string CombineRemotePath(string? basePath, string? relativePath)
    {
        var segments = new[] { basePath, relativePath }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var combined = string.Join("/", segments);
        return string.IsNullOrWhiteSpace(combined) ? string.Empty : $"/{combined}";
    }

    private static AsyncFtpClient CreateFluentClient(DestinationSettings destination)
    {
        var userName = destination.UseAnonymousFtp ? "anonymous" : destination.Username;
        var password = destination.UseAnonymousFtp ? "anonymous@example.com" : destination.Password;
        var client = new AsyncFtpClient(destination.Host, userName, password, destination.Port);
        client.Config.ConnectTimeout = RequestTimeoutMilliseconds;
        client.Config.ReadTimeout = RequestTimeoutMilliseconds;
        client.Config.DataConnectionConnectTimeout = RequestTimeoutMilliseconds;
        client.Config.DataConnectionReadTimeout = RequestTimeoutMilliseconds;
        client.Config.DataConnectionType = destination.FtpDataMode switch
        {
            FtpDataMode.Passive => FtpDataConnectionType.PASV,
            FtpDataMode.Active => FtpDataConnectionType.AutoActive,
            _ => FtpDataConnectionType.AutoPassive
        };
        return client;
    }

    private static async Task<AsyncFtpClient> ConnectClientAsync(DestinationSettings destination, CancellationToken cancellationToken)
    {
        var client = CreateFluentClient(destination);
        try
        {
            await client.AutoConnect(cancellationToken);
            return client;
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    private static async Task UploadSingleFileWithDiagnosticsAsync(
        Func<int, CancellationToken, Task<AsyncFtpClient>> getClientForAttemptAsync,
        Func<Task> discardClientAsync,
        string localFilePath,
        string remoteFilePath,
        int fileIndex,
        int totalFiles,
        CancellationToken cancellationToken)
    {
        var fluentRemotePath = ToFluentBrowserPath(remoteFilePath);
        var fileSize = new FileInfo(localFilePath).Length;
        DiagnosticLog.WriteInfo($"FTP upload starting file {fileIndex}/{totalFiles}: local='{localFilePath}', remote='{remoteFilePath}', size={fileSize} bytes.");

        await FtpUploadRetryPolicy.ExecuteAsync(
            async (attemptNumber, token) =>
            {
                var client = await getClientForAttemptAsync(attemptNumber, token);
                await client.UploadFile(localFilePath, fluentRemotePath, FtpRemoteExists.Overwrite, createRemoteDir: true, token: token);
                var retryDescription = attemptNumber == 1 ? string.Empty : $" after retry {attemptNumber - 1}/{FtpUploadRetryPolicy.MaxRetries}";
                DiagnosticLog.WriteInfo($"FTP upload completed file {fileIndex}/{totalFiles}{retryDescription}: remote='{remoteFilePath}'.");
            },
            onRetryAsync: async (retryAttempt, ex, delay, _) =>
            {
                DiagnosticLog.WriteException($"FTP upload failed for file {fileIndex}/{totalFiles}: local='{localFilePath}', remote='{remoteFilePath}'. Retrying {retryAttempt}/{FtpUploadRetryPolicy.MaxRetries} in {delay.TotalSeconds:0.#}s.", ex);
                await discardClientAsync();
            },
            cancellationToken: cancellationToken);
    }

    private static bool IsRetryable550(Exception exception) => FtpUploadRetryPolicy.IsRetryable550(exception);

    private static string ToFluentBrowserPath(string? remotePath)
    {
        if (string.IsNullOrWhiteSpace(remotePath) || remotePath == "/")
        {
            return ".";
        }

        return remotePath.Trim().Replace('\\', '/').TrimStart('/');
    }

}

internal static class FtpUploadRetryPolicy
{
    internal const int MaxRetries = 5;

    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(15)
    ];

    internal static async Task ExecuteAsync(
        Func<int, CancellationToken, Task> attemptAsync,
        CancellationToken cancellationToken = default,
        Func<int, Exception, TimeSpan, CancellationToken, Task>? onRetryAsync = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        ArgumentNullException.ThrowIfNull(attemptAsync);

        delayAsync ??= Task.Delay;

        for (var attemptNumber = 1; ; attemptNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await attemptAsync(attemptNumber, cancellationToken);
                return;
            }
            catch (Exception ex) when (ShouldRetry(ex, attemptNumber))
            {
                var retryAttempt = attemptNumber;
                var delay = GetDelay(retryAttempt);
                if (onRetryAsync is not null)
                {
                    await onRetryAsync(retryAttempt, ex, delay, cancellationToken);
                }

                await delayAsync(delay, cancellationToken);
            }
        }
    }

    internal static bool IsRetryable(Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            return false;
        }

        if (exception is FtpAuthenticationException
            or FtpHashUnsupportedException
            or FtpInvalidCertificateException
            or FtpProtocolUnsupportedException
            or FtpSecurityNotAvailableException)
        {
            return false;
        }

        if (exception is TimeoutException or IOException or SocketException)
        {
            return true;
        }

        if (exception is FtpCommandException ftpCommandException)
        {
            return IsRetryableFtpCommand(ftpCommandException);
        }

        if (exception is FtpException ftpException)
        {
            return ftpException.InnerException is null || IsRetryable(ftpException.InnerException);
        }

        return false;
    }

    private static bool IsRetryableFtpCommand(FtpCommandException exception)
    {
        if (string.Equals(exception.CompletionCode, "550", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(exception.CompletionCode) &&
            exception.CompletionCode.StartsWith('4');
    }

    internal static bool IsRetryable550(Exception exception)
    {
        if (exception is FtpCommandException ftpCommandException)
        {
            return string.Equals(ftpCommandException.CompletionCode, "550", StringComparison.OrdinalIgnoreCase);
        }

        if (exception is FtpException ftpException && ftpException.InnerException is Exception innerException)
        {
            return IsRetryable550(innerException);
        }

        return false;
    }

    private static TimeSpan GetDelay(int retryAttempt)
    {
        var delayIndex = Math.Clamp(retryAttempt - 1, 0, RetryDelays.Length - 1);
        return RetryDelays[delayIndex];
    }

    private static bool ShouldRetry(Exception exception, int failedAttemptNumber) =>
        failedAttemptNumber <= MaxRetries && IsRetryable(exception);
}
