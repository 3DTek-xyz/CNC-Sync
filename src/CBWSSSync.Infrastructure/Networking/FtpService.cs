using System.Net;
using CBWSSSync.Core.Configuration;
using CBWSSSync.Core.Services;

namespace CBWSSSync.Infrastructure.Networking;

public sealed class FtpService : IFtpService
{
    private const int RequestTimeoutMilliseconds = 8000;

    public async Task<(bool Success, string Message)> TestConnectionAsync(FtpDestinationSettings destination, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destination.Host))
        {
            return (false, "FTP host is not configured.");
        }

        try
        {
            var request = CreateRequest(destination, "/", WebRequestMethods.Ftp.ListDirectory);
            using var response = (FtpWebResponse)await GetResponseWithTimeoutAsync(request, cancellationToken);
            return (true, $"FTP connection successful: {destination.Host}:{destination.Port}");
        }
        catch (TimeoutException)
        {
            return (false, $"No FTP server responded at {destination.Host}:{destination.Port} within {RequestTimeoutMilliseconds / 1000} seconds.");
        }
        catch (WebException ex)
        {
            return (false, $"FTP connection failed: {FormatWebException(ex)}");
        }
        catch (Exception ex)
        {
            return (false, $"FTP connection failed: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> UploadDirectoryAsync(
        string localPath,
        FtpDestinationSettings destination,
        string remoteDirectoryPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destination.Host))
        {
            return (false, "FTP upload skipped because no FTP host is configured.");
        }

        if (!Directory.Exists(localPath))
        {
            return (false, $"FTP upload skipped because local path does not exist: {localPath}");
        }

        try
        {
            await CreateDirectoryChainIfNeededAsync(destination, remoteDirectoryPath, cancellationToken);

            foreach (var file in Directory.GetFiles(localPath, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = Path.GetRelativePath(localPath, file).Replace('\\', '/');
                var remoteFilePath = CombineRemotePath(remoteDirectoryPath, relativePath);
                var remoteDir = Path.GetDirectoryName(remoteFilePath)?.Replace('\\', '/');
                if (!string.IsNullOrWhiteSpace(remoteDir))
                {
                    await CreateDirectoryChainIfNeededAsync(destination, remoteDir, cancellationToken);
                }

                var request = CreateRequest(destination, remoteFilePath, WebRequestMethods.Ftp.UploadFile);
                await using var fileStream = File.OpenRead(file);
                await using var requestStream = await GetRequestStreamWithTimeoutAsync(request, cancellationToken);
                await fileStream.CopyToAsync(requestStream, cancellationToken);
                using var response = (FtpWebResponse)await GetResponseWithTimeoutAsync(request, cancellationToken);
            }

            var targetDescription = string.IsNullOrWhiteSpace(remoteDirectoryPath) ? "/" : remoteDirectoryPath;
            return (true, $"FTP upload completed to {targetDescription}: {localPath}");
        }
        catch (TimeoutException)
        {
            return (false, $"FTP upload timed out because no FTP server responded at {destination.Host}:{destination.Port} within {RequestTimeoutMilliseconds / 1000} seconds.");
        }
        catch (WebException ex)
        {
            return (false, $"FTP upload failed: {FormatWebException(ex)}");
        }
        catch (Exception ex)
        {
            return (false, $"FTP upload failed: {ex.Message}");
        }
    }

    public async Task<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)> ListRootEntriesAsync(
        FtpDestinationSettings destination,
        string remoteDirectoryPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destination.Host))
        {
                return (false, [], "FTP listing skipped because no FTP host is configured.");
        }

        try
        {
            var request = CreateRequest(destination, remoteDirectoryPath, WebRequestMethods.Ftp.ListDirectory);
            using var response = (FtpWebResponse)await GetResponseWithTimeoutAsync(request, cancellationToken);
            await using var responseStream = response.GetResponseStream();
            using var reader = new StreamReader(responseStream ?? Stream.Null);
            var contents = await reader.ReadToEndAsync(cancellationToken);
            var entryNames = contents
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(entry => !string.IsNullOrWhiteSpace(entry))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var entries = new List<RemoteEntryInfo>(entryNames.Count);
            foreach (var entryName in entryNames)
            {
                var sizeResult = await TryGetFileSizeAsync(destination, CombineRemotePath(remoteDirectoryPath, entryName), cancellationToken);
                entries.Add(new RemoteEntryInfo
                {
                    Name = entryName,
                    SizeBytes = sizeResult.Exists ? sizeResult.SizeBytes : null
                });
            }

            var targetDescription = string.IsNullOrWhiteSpace(remoteDirectoryPath) ? "/" : remoteDirectoryPath;
            return (true, entries, $"Listed {entries.Count} item(s) from FTP path {targetDescription}.");
        }
        catch (WebException ex) when (ex.Response is FtpWebResponse ftpResponse &&
                                      ftpResponse.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable)
        {
            return (true, [], $"FTP path does not exist yet; treating it as empty.");
        }
        catch (TimeoutException)
        {
            return (false, [], $"No FTP server responded at {destination.Host}:{destination.Port} within {RequestTimeoutMilliseconds / 1000} seconds.");
        }
        catch (WebException ex)
        {
            return (false, [], $"FTP listing failed: {FormatWebException(ex)}");
        }
        catch (Exception ex)
        {
            return (false, [], $"FTP listing failed: {ex.Message}");
        }
    }

    public async Task<(bool Exists, long? SizeBytes, string Message)> TryGetFileSizeAsync(
        FtpDestinationSettings destination,
        string remoteFilePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = CreateRequest(destination, remoteFilePath, WebRequestMethods.Ftp.GetFileSize);
            using var response = (FtpWebResponse)await GetResponseWithTimeoutAsync(request, cancellationToken);
            long? sizeBytes = response.ContentLength >= 0 ? response.ContentLength : null;
            return (true, sizeBytes, $"Remote file exists: {remoteFilePath}");
        }
        catch (WebException ex) when (ex.Response is FtpWebResponse ftpResponse &&
                                      ftpResponse.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable)
        {
            return (false, null, $"Remote file does not exist: {remoteFilePath}");
        }
        catch (WebException)
        {
            return (false, null, $"Could not read remote file size: {remoteFilePath}");
        }
    }

    private static async Task CreateDirectoryChainIfNeededAsync(
        FtpDestinationSettings destination,
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
            await CreateDirectoryIfNeededAsync(destination, currentPath, cancellationToken);
        }
    }

    private static async Task CreateDirectoryIfNeededAsync(
        FtpDestinationSettings destination,
        string remotePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = CreateRequest(destination, remotePath, WebRequestMethods.Ftp.MakeDirectory);
            using var response = (FtpWebResponse)await GetResponseWithTimeoutAsync(request, cancellationToken);
        }
        catch (WebException ex) when (ex.Response is FtpWebResponse ftpResponse &&
                                      ftpResponse.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable)
        {
            // Directory already exists.
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

    private static FtpWebRequest CreateRequest(FtpDestinationSettings destination, string remotePath, string method)
    {
#pragma warning disable SYSLIB0014
        var request = (FtpWebRequest)WebRequest.Create(new Uri($"ftp://{destination.Host}:{destination.Port}/{remotePath.TrimStart('/')}"));
#pragma warning restore SYSLIB0014
        request.Method = method;
        request.UseBinary = true;
        request.UsePassive = true;
        request.KeepAlive = false;
        request.Timeout = RequestTimeoutMilliseconds;
        request.ReadWriteTimeout = RequestTimeoutMilliseconds;
        request.Credentials = destination.UseAnonymousFtp
            ? new NetworkCredential("anonymous", "anonymous@example.com")
            : new NetworkCredential(destination.Username, destination.Password);
        return request;
    }

    private static async Task<WebResponse> GetResponseWithTimeoutAsync(FtpWebRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await request.GetResponseAsync().WaitAsync(
                TimeSpan.FromMilliseconds(RequestTimeoutMilliseconds),
                cancellationToken);
        }
        catch (TimeoutException)
        {
            request.Abort();
            throw;
        }
    }

    private static async Task<Stream> GetRequestStreamWithTimeoutAsync(FtpWebRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await request.GetRequestStreamAsync().WaitAsync(
                TimeSpan.FromMilliseconds(RequestTimeoutMilliseconds),
                cancellationToken);
        }
        catch (TimeoutException)
        {
            request.Abort();
            throw;
        }
    }

    private static string FormatWebException(WebException exception)
    {
        if (exception.Response is not FtpWebResponse ftpResponse)
        {
            return exception.Message;
        }

        var statusDescription = ftpResponse.StatusDescription?.Trim();
        return ftpResponse.StatusCode switch
        {
            FtpStatusCode.NotLoggedIn =>
                string.IsNullOrWhiteSpace(statusDescription)
                    ? "authentication failed (530 NotLoggedIn). Check the username and password."
                    : $"authentication failed (530 NotLoggedIn): {statusDescription}",
            FtpStatusCode.ActionNotTakenFileUnavailable =>
                string.IsNullOrWhiteSpace(statusDescription)
                    ? "the requested file or directory is unavailable on the FTP server."
                    : statusDescription,
            _ =>
                string.IsNullOrWhiteSpace(statusDescription)
                    ? $"{ftpResponse.StatusCode} ({exception.Message})"
                    : $"{ftpResponse.StatusCode}: {statusDescription}"
        };
    }
}
