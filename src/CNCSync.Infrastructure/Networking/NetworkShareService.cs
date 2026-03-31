using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
            var mountPoint = Path.Combine(Path.GetTempPath(), "cncsync-network-shares", BuildMountPointKey(destination));
            Directory.CreateDirectory(mountPoint);

            if (!await IsMountedAsync(mountPoint, cancellationToken))
            {
                await MountMacShareAsync(destination, mountPoint, cancellationToken);
            }

            return mountPoint;
        }

        if (OperatingSystem.IsWindows())
        {
            var uncPath = $@"\\{destination.NetworkHost}\{destination.NetworkShareName}";
            if (!destination.UseCurrentUserCredentials)
            {
                await EnsureWindowsShareConnectedAsync(destination, uncPath, cancellationToken);
            }

            return uncPath;
        }

        if (OperatingSystem.IsLinux())
        {
            return await EnsureLinuxShareAccessibleAsync(destination, cancellationToken);
        }

        throw new PlatformNotSupportedException("Network share destinations are currently implemented for macOS, Windows SMB, and Linux desktop SMB mounts.");
    }

    private static async Task EnsureWindowsShareConnectedAsync(DestinationSettings destination, string uncPath, CancellationToken cancellationToken)
    {
        var qualifiedUsername = string.IsNullOrWhiteSpace(destination.NetworkDomain)
            ? destination.Username
            : $@"{destination.NetworkDomain}\{destination.Username}";

        var result = await RunProcessAsync(
            "net",
            $"use {EscapeWindowsArgument(uncPath)} {EscapeWindowsArgument(destination.Password)} /user:{EscapeWindowsArgument(qualifiedUsername)} /persistent:no",
            cancellationToken);

        if (result.ExitCode == 0)
        {
            return;
        }

        var output = string.Concat(result.StandardOutput, "\n", result.StandardError);
        if (output.Contains("The command completed successfully.", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new InvalidOperationException($"Windows SMB sign-in failed: {output.Trim()}");
    }

    private static async Task<string> EnsureLinuxShareAccessibleAsync(DestinationSettings destination, CancellationToken cancellationToken)
    {
        var existingMountPath = FindLinuxMountedSharePath(destination);
        if (!string.IsNullOrWhiteSpace(existingMountPath))
        {
            return existingMountPath;
        }

        if (destination.UseCurrentUserCredentials)
        {
            var gioMountUri = $"smb://{EscapeGioUriSegment(destination.NetworkHost)}/{EscapeGioUriSegment(destination.NetworkShareName)}";
            var mountResult = await RunProcessAsync("gio", $"mount {EscapeArgument(gioMountUri)}", cancellationToken);
            if (mountResult.ExitCode == 0)
            {
                existingMountPath = FindLinuxMountedSharePath(destination);
                if (!string.IsNullOrWhiteSpace(existingMountPath))
                {
                    return existingMountPath;
                }
            }
        }

        throw new InvalidOperationException(
            destination.UseCurrentUserCredentials
                ? $"Linux SMB share is not mounted yet: {destination.NetworkHost}/{destination.NetworkShareName}. Mount it in the desktop file manager or make sure gio can access it."
                : $"Linux explicit SMB credentials are not yet automated. Mount {destination.NetworkHost}/{destination.NetworkShareName} in the desktop file manager with those credentials first.");
    }

    private static async Task<bool> IsMountedAsync(string mountPoint, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync("/sbin/mount", string.Empty, cancellationToken);
        if (result.ExitCode != 0)
        {
            return false;
        }

        var comparableMountPoints = GetComparableMountPoints(mountPoint);

        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(line => comparableMountPoints.Any(candidate => line.Contains($" on {candidate} ", StringComparison.Ordinal)));
    }

    private static async Task MountMacShareAsync(DestinationSettings destination, string mountPoint, CancellationToken cancellationToken)
    {
        await PrepareMountPointAsync(mountPoint, cancellationToken);

        var specifier = BuildSmbSpecifier(destination);
        var result = await RunProcessAsync("/sbin/mount_smbfs", $"{EscapeArgument(specifier)} {EscapeArgument(mountPoint)}", cancellationToken);
        if (result.ExitCode != 0 && ContainsFileExistsError(result))
        {
            await TryUnmountAsync(mountPoint, cancellationToken);
            await PrepareMountPointAsync(mountPoint, cancellationToken);
            var retryArguments = $"-s {EscapeArgument(specifier)} {EscapeArgument(mountPoint)}";
            result = await RunProcessAsync("/sbin/mount_smbfs", retryArguments, cancellationToken);
        }

        if (result.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            var details = string.IsNullOrWhiteSpace(error) ? "Mount command failed." : error.Trim();
            throw new InvalidOperationException($"{details} (SMB {destination.NetworkHost}/{destination.NetworkShareName})");
        }
    }

    private static string BuildSmbSpecifier(DestinationSettings destination)
    {
        var authority = destination.UseCurrentUserCredentials
            ? string.Empty
            : $"{BuildDomainQualifiedUsername(destination)}:{EscapeSmbComponent(destination.Password)}@";
        return $"//{authority}{EscapeSmbComponent(destination.NetworkHost)}/{EscapeSmbComponent(destination.NetworkShareName)}";
    }

    private static string BuildMountPointKey(DestinationSettings destination)
    {
        var identity = destination.UseCurrentUserCredentials
            ? "current-user"
            : $"{destination.NetworkDomain}|{destination.Username}";
        var raw = $"{destination.Id}|{destination.NetworkHost}|{destination.NetworkShareName}|{identity}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant()[..12];
        return $"{destination.Id}-{hash}";
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
        Uri.EscapeDataString(value);

    private static string EscapeArgument(string value) =>
        $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static string EscapeWindowsArgument(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

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

    private static string? FindLinuxMountedSharePath(DestinationSettings destination)
    {
        var runtimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrWhiteSpace(runtimeDirectory))
        {
            var userId = Environment.GetEnvironmentVariable("UID");
            if (!string.IsNullOrWhiteSpace(userId))
            {
                runtimeDirectory = $"/run/user/{userId}";
            }
        }

        if (string.IsNullOrWhiteSpace(runtimeDirectory))
        {
            return null;
        }

        var gvfsRoot = Path.Combine(runtimeDirectory, "gvfs");
        if (!Directory.Exists(gvfsRoot))
        {
            return null;
        }

        return Directory.EnumerateDirectories(gvfsRoot)
            .FirstOrDefault(path => LinuxMountMatches(path, destination));
    }

    private static bool LinuxMountMatches(string path, DestinationSettings destination)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var serverMatch = $"server={destination.NetworkHost}";
        var shareMatch = $"share={destination.NetworkShareName}";
        return name.Contains("smb-share:", StringComparison.OrdinalIgnoreCase) &&
               name.Contains(serverMatch, StringComparison.OrdinalIgnoreCase) &&
               name.Contains(shareMatch, StringComparison.OrdinalIgnoreCase);
    }

    private static string EscapeGioUriSegment(string value) =>
        Uri.EscapeDataString(value);

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
        => $"SMB {destination.NetworkHost}/{destination.NetworkShareName}";

    private static IReadOnlyList<string> GetComparableMountPoints(string mountPoint)
    {
        var fullPath = Path.GetFullPath(mountPoint);
        var values = new HashSet<string>(StringComparer.Ordinal)
        {
            mountPoint,
            fullPath
        };

        if (fullPath.StartsWith("/var/", StringComparison.Ordinal))
        {
            values.Add($"/private{fullPath}");
        }
        else if (fullPath.StartsWith("/private/var/", StringComparison.Ordinal))
        {
            values.Add(fullPath["/private".Length..]);
        }

        return values.ToList();
    }

    private static bool ContainsFileExistsError(ProcessResult result)
    {
        var output = string.Concat(result.StandardOutput, "\n", result.StandardError);
        return output.Contains("File exists", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task PrepareMountPointAsync(string mountPoint, CancellationToken cancellationToken)
    {
        if (await IsMountedAsync(mountPoint, cancellationToken))
        {
            return;
        }

        if (Directory.Exists(mountPoint))
        {
            foreach (var directory in Directory.EnumerateDirectories(mountPoint))
            {
                Directory.Delete(directory, recursive: true);
            }

            foreach (var file in Directory.EnumerateFiles(mountPoint))
            {
                File.Delete(file);
            }
        }
        else
        {
            Directory.CreateDirectory(mountPoint);
        }
    }

    private static async Task TryUnmountAsync(string mountPoint, CancellationToken cancellationToken)
    {
        if (!await IsMountedAsync(mountPoint, cancellationToken))
        {
            return;
        }

        await RunProcessAsync("/sbin/umount", EscapeArgument(mountPoint), cancellationToken);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
