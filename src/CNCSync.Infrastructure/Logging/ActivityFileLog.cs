using System.Text;

namespace CNCSync.Infrastructure.Logging;

public sealed class ActivityFileLog(string path, int maxBytes = 2 * 1024 * 1024)
{
    private readonly Lock _gate = new();
    private readonly string _path = Path.GetFullPath(path);
    private readonly int _maxBytes = maxBytes >= 8 ? maxBytes : throw new ArgumentOutOfRangeException(nameof(maxBytes));

    public bool TryAppend(string line)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var entryBytes = Encoding.UTF8.GetByteCount(line);
                if (File.Exists(_path) && new FileInfo(_path).Length + Math.Min(entryBytes, _maxBytes) > _maxBytes)
                {
                    File.Move(_path, _path + ".1", overwrite: true);
                }

                if (entryBytes <= _maxBytes)
                {
                    File.AppendAllText(_path, line);
                }
                else
                {
                    AppendTruncated(line);
                }

                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    private void AppendTruncated(string line)
    {
        var newline = Encoding.UTF8.GetBytes(Environment.NewLine);
        var bytes = new byte[_maxBytes];
        Encoding.UTF8.GetEncoder().Convert(line.AsSpan(), bytes.AsSpan(0, _maxBytes - newline.Length),
            flush: false, out _, out var written, out _);
        newline.CopyTo(bytes, written);

        using var stream = File.Open(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
        stream.Write(bytes.AsSpan(0, written + newline.Length));
    }
}
