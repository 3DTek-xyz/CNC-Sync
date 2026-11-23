using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CNCSync.Core.Configuration;

namespace CNCSync.Core.Services;

internal enum WorkOutcome
{
    Retrying,
    Failed,
    AwaitingDelivery,
    Succeeded
}

internal sealed record DurableWorkState(
    string Fingerprint,
    WorkOutcome Outcome,
    int Attempts = 0,
    DateTimeOffset? RetryAt = null,
    DateTime? DeliveredThroughUtc = null,
    string? StagedJobDirectory = null,
    string? ProcessingConfiguration = null)
{
    public static DurableWorkState? Read(WatchProfileSettings profile, string path)
    {
        var location = GetLocation(profile, path);
        try
        {
            using var stream = File.OpenRead(location);
            var state = JsonSerializer.Deserialize<DurableWorkState>(stream);
            if (state is null || string.IsNullOrWhiteSpace(state.Fingerprint) || !Enum.IsDefined(state.Outcome) ||
                state.Attempts < 0 || (state.Outcome == WorkOutcome.Retrying && state.RetryAt is null))
            {
                throw new IOException($"Invalid queue receipt: {location}");
            }

            return state;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public void Write(WatchProfileSettings profile, string path)
    {
        var location = GetLocation(profile, path);
        var previous = Read(profile, path)?.DeliveredThroughUtc;
        var state = previous is not null && (DeliveredThroughUtc is null || previous > DeliveredThroughUtc)
            ? this with { DeliveredThroughUtc = previous }
            : this;
        Directory.CreateDirectory(Path.GetDirectoryName(location)!);
        var temporary = $"{location}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = File.Open(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, state);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, location, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static string GetLocation(WatchProfileSettings profile, string path)
    {
        var folder = Path.Combine(profile.StagingFolder, ".cncsync-queue");
        if (Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"Queue state directory must not be a symbolic link: {folder}");
        }

        var key = WorkItemKey.Create(profile, path);
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(folder, name + ".json");
    }
}

internal static class WorkItemKey
{
    public static string Create(WatchProfileSettings profile, string path) =>
        profile.Id.ToUpperInvariant() + "\0" + NormalizePath(path);

    public static string NormalizePath(string path)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return OperatingSystem.IsWindows() ? fullPath.ToUpperInvariant() : fullPath;
    }
}
