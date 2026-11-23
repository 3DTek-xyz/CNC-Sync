using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace CNCSync.Core.Services;

public static class WorkItemFingerprint
{
    public static string Capture(string path, bool includeContent, CancellationToken cancellationToken = default)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = includeContent ? ArrayPool<byte>.Shared.Rent(64 * 1024) : null;
        try
        {
            AppendEntry(new FileInfo(path), ".", hash, buffer, cancellationToken);
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally
        {
            if (buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    private static void AppendEntry(FileSystemInfo entry, string relativePath, IncrementalHash hash, byte[]? buffer, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var attributes = File.GetAttributes(entry.FullName);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"Cannot fingerprint a symbolic link: {entry.FullName}");
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            Append(hash, "directory");
            Append(hash, relativePath);
            foreach (var child in new DirectoryInfo(entry.FullName).EnumerateFileSystemInfos()
                         .Where(child => !FileSystemItemFilter.ShouldIgnoreFileSystemItem(child.Name))
                         .OrderBy(child => child.Name, StringComparer.Ordinal))
            {
                AppendEntry(child, relativePath + "/" + child.Name, hash, buffer, token);
            }

            return;
        }

        var file = new FileInfo(entry.FullName);
        var length = file.Length;
        var modified = file.LastWriteTimeUtc;
        Append(hash, "file");
        Append(hash, relativePath);
        Append(hash, FormattableString.Invariant($"{length}:{modified.Ticks}"));
        if (buffer is null)
        {
            return;
        }

        using var content = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
        int bytesRead;
        while ((bytesRead = stream.Read(buffer)) != 0)
        {
            token.ThrowIfCancellationRequested();
            content.AppendData(buffer.AsSpan(0, bytesRead));
        }

        file.Refresh();
        if (stream.Position != length || file.Length != length || file.LastWriteTimeUtc != modified)
        {
            throw new IOException($"Input changed while fingerprinting: {file.FullName}");
        }

        hash.AppendData(content.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
