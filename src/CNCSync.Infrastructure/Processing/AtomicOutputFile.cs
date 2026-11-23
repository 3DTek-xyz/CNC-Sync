using System.Diagnostics;

namespace CNCSync.Infrastructure.Processing;

internal static class AtomicOutputFile
{
    public static async Task WriteAsync(
        string outputPath,
        bool overwrite,
        Func<FileStream, CancellationToken, Task> write,
        CancellationToken cancellationToken,
        Func<string, CancellationToken, Task>? validate = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var temporaryPath = Path.Combine(Path.GetDirectoryName(outputPath)!, $".pcssync-{Guid.NewGuid():N}.tmp");
        var created = false;
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                created = true;
                await write(stream, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (validate is not null)
            {
                await validate(temporaryPath, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, outputPath, overwrite);
            created = false;
        }
        finally
        {
            if (created)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Trace.TraceWarning($"Could not remove temporary output {temporaryPath}: {ex.Message}");
                }
            }
        }
    }
}
