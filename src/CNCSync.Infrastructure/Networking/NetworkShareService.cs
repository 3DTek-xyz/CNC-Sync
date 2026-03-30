using System.Diagnostics;
using CNCSync.Core.Configuration;
using CNCSync.Core.Services;

namespace CNCSync.Infrastructure.Networking;

public sealed class NetworkShareService : INetworkShareService
{
    public async Task<(bool Success, string Message)> TestConnectionAsync(DestinationSettings destination, CancellationToken cancellationToken = default)
    {
        var validation = Validate(destination);
        if (!validation.Success)
        {
            return validation;
        }

        try
        {
            var rootPath = await EnsureAccessibleRootAsync(destination, cancellationToken);
            Directory.CreateDirectory(rootPath);
            return (true, $"Network share is ready: {DescribeNetworkShare(destination)}");
        }
        catch (Exception ex)
        {
            return (false, $"Network share test failed: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> UploadDirectoryAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default)
    {
        var validation = Validate(destination);
        if (!validation.Success)
        {
            return validation;
        }

        if (!Directory.Exists(localPath))
        {
            return (false, $"Network upload skipped because source path does not exist: {localPath}");
        }

        try
        {
            var shareRoot = await EnsureAccessibleRootAsync(destination, cancellationToken);
            var targetRoot = CombineLocalPath(shareRoot, remoteDirectoryPath);
            Directory.CreateDirectory(targetRoot);

            foreach (var file in FileSystemItemFilter.EnumerateIncludedFiles(localPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(localPath, file);
                var destinationFile = Path.Combine(targetRoot, relativePath);
                var destinationDirectory = Path.GetDirectoryName(destinationFile);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                File.Copy(file, destinationFile, overwrite: true);
            }

            return (true, $"Network upload completed to {targetRoot}: {localPath}");
        }
        catch (Exception ex)
        {
            return (false, $"Network upload failed: {ex.Message}");
        }
    }

    public async Task<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)> ListRootEntriesAsync(DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default)
    {
        var validation = Validate(destination);
        if (!validation.Success)
        {
            return (false, [], validation.Message);
        }

        try
        {
            var shareRoot = await EnsureAccessibleRootAsync(destination, cancellationToken);
            var rootPath = CombineLocalPath(shareRoot, remoteDirectoryPath);
            if (!Directory.Exists(rootPath))
            {
                return (true, [], "Network share path does not exist yet; treating it as empty.");
            }

            var entries = Directory
                .EnumerateFileSystemEntries(rootPath, "*", SearchOption.TopDirectoryOnly)
                .Where(path => !FileSystemItemFilter.ShouldIgnoreFileSystemItem(Path.GetFileName(path)))
                .Select(path =>
                {
                    var isDirectory = Directory.Exists(path);
                    return new RemoteEntryInfo
                    {
                        Name = Path.GetFileName(path),
                        FullPath = CombineRemotePath(remoteDirectoryPath, Path.GetFileName(path)),
                        IsDirectory = isDirectory,
                        SizeBytes = isDirectory ? null : new FileInfo(path).Length
                    };
                })
                .OrderByDescending(item => item.IsDirectory)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return (true, entries, $"Listed {entries.Count} item(s) from network share path {DescribeRemotePath(remoteDirectoryPath)}.");
        }
        catch (Exception ex)
        {
            return (false, [], $"Network share browser failed: {ex.Message}");
        }
    }

    public async Task<(bool Exists, long? SizeBytes, string Message)> TryGetFileSizeAsync(DestinationSettings destination, string remoteFilePath, CancellationToken cancellationToken = default)
    {
        var validation = Validate(destination);
        if (!validation.Success)
        {
            return (false, null, validation.Message);
        }

        try
        {
            var shareRoot = await EnsureAccessibleRootAsync(destination, cancellationToken);
            var fullPath = CombineLocalPath(shareRoot, remoteFilePath);
            if (!File.Exists(fullPath))
            {
                return (false, null, $"Network share file does not exist: {fullPath}");
            }

            return (true, new FileInfo(fullPath).Length, $"Network share file exists: {fullPath}");
        }
        catch (Exception ex)
        {
            return (false, null, $"Could not read network share file size: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> DeleteRemoteItemAsync(DestinationSettings destination, string remotePath, bool isDirectory, CancellationToken cancellationToken = default)
    {
        var validation = Validate(destination);
        if (!validation.Success)
        {
            return validation;
        }

        try
        {
            var shareRoot = await EnsureAccessibleRootAsync(destination, cancellationToken);
            var fullPath = CombineLocalPath(shareRoot, remotePath);
            if (isDirectory)
            {
                if (Directory.Exists(fullPath))
                {
                    Directory.Delete(fullPath, recursive: true);
                }
            }
            else if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            return (true, $"{(isDirectory ? "Network folder" : "Network file")} deleted: {fullPath}");
        }
        catch (Exception ex)
        {
            return (false, $"Network share delete failed: {ex.Message}");
        }
    }

    private static (bool Success, string Message) Validate(DestinationSettings destination)
    {
        if (string.IsNullOrWhiteSpace(destination.NetworkHost))
        {
            return (false, "Network share host is not configured.");
        }

        if (string.IsNullOrWhiteSpace(destination.NetworkShareName))
        {
            return (false, "Network share name is not configured.");
        }

        if (!destination.UseCurrentUserCredentials)
        {
            if (string.IsNullOrWhiteSpace(destination.Username))
            {
                return (false, "Network share username is not configured.");
            }

            if (string.IsNullOrWhiteSpace(destination.Password))
            {
                return (false, "Network share password is not configured.");
            }
        }

        return (true, string.Empty);
    }

    private static async Task<string> EnsureAccessibleRootAsync(DestinationSettings destination, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsMacOS())
        {
            var mountPoint = Path.Combine(Path.GetTempPath(), "cncsync-network-shares", destination.Id);
            Directory.CreateDirectory(mountPoint);

            if (!await IsMountedAsync(mountPoint, cancellationToken))
            {
                await MountMacShareAsync(destination, mountPoint, cancellationToken);
            }

            return mountPoint;
        }

        if (OperatingSystem.IsWindows())
        {
            if (!destination.UseCurrentUserCredentials)
            {
                throw new InvalidOperationException("Explicit Windows network-share credentials are not implemented yet.");
            }

            return $@"\\{destination.NetworkHost}\{destination.NetworkShareName}";
        }

        throw new PlatformNotSupportedException("Network share destinations are currently implemented for macOS, with Windows current-user UNC access planned next.");
    }

    private static async Task<bool> IsMountedAsync(string mountPoint, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync("/sbin/mount", string.Empty, cancellationToken);
        if (result.ExitCode != 0)
        {
            return false;
        }

        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(line => line.Contains($" on {mountPoint} ", StringComparison.Ordinal));
    }

    private static async Task MountMacShareAsync(DestinationSettings destination, string mountPoint, CancellationToken cancellationToken)
    {
        var (fileName, arguments) = destination.NetworkProtocol switch
        {
            NetworkShareProtocol.Afp => ("/sbin/mount_afp", $"{BuildAfpUrl(destination)} {EscapeArgument(mountPoint)}"),
            _ => ("/sbin/mount_smbfs", $"{BuildSmbSpecifier(destination)} {EscapeArgument(mountPoint)}")
        };

        var result = await RunProcessAsync(fileName, arguments, cancellationToken);
        if (result.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Mount command failed." : error.Trim());
        }
    }

    private static string BuildSmbSpecifier(DestinationSettings destination)
    {
        var authority = destination.UseCurrentUserCredentials
            ? string.Empty
            : $"{BuildDomainQualifiedUsername(destination)}:{EscapeSmbComponent(destination.Password)}@";
        return $"//{authority}{EscapeSmbComponent(destination.NetworkHost)}/{EscapeSmbComponent(destination.NetworkShareName)}";
    }

    private static string BuildAfpUrl(DestinationSettings destination)
    {
        var credentials = destination.UseCurrentUserCredentials
            ? string.Empty
            : $"{Uri.EscapeDataString(BuildDomainQualifiedUsername(destination))}:{Uri.EscapeDataString(destination.Password)}@";
        return $"afp://{credentials}{Uri.EscapeDataString(destination.NetworkHost)}/{Uri.EscapeDataString(destination.NetworkShareName)}";
    }

    private static string BuildDomainQualifiedUsername(DestinationSettings destination)
    {
        if (string.IsNullOrWhiteSpace(destination.NetworkDomain))
        {
            return destination.Username;
        }

        return $"{destination.NetworkDomain};{destination.Username}";
    }

    private static string EscapeSmbComponent(string value) =>
        value.Replace("/", "%2f", StringComparison.Ordinal)
            .Replace(":", "%3a", StringComparison.Ordinal)
            .Replace("@", "%40", StringComparison.Ordinal);

    private static string EscapeArgument(string value) =>
        $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static async Task<ProcessResult> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new ProcessResult(
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask);
    }

    private static string CombineLocalPath(string rootPath, string relativePath)
    {
        var segments = relativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Aggregate(rootPath, Path.Combine);
    }

    private static string CombineRemotePath(string basePath, string name)
    {
        var segments = new[] { basePath, name }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var combined = string.Join("/", segments);
        return string.IsNullOrWhiteSpace(combined) ? string.Empty : $"/{combined}";
    }

    private static string DescribeRemotePath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? "/" : path;

    private static string DescribeNetworkShare(DestinationSettings destination)
    {
        var protocol = destination.NetworkProtocol == NetworkShareProtocol.Afp ? "AFP" : "SMB";
        return $"{protocol} {destination.NetworkHost}/{destination.NetworkShareName}";
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
