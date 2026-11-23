using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace CNCSync.Infrastructure.Networking;

internal static class ScpUploadProtocol
{
    public static string BuildCommand(string directory) =>
        $"scp -t -d {RemotePathTransformation.ShellQuote.Transform(directory)}";

    public static void Send(Stream source, Stream sink, Stream acknowledgements, string fileName, Encoding encoding,
        Action resetIdleTimeout, CancellationToken cancellationToken)
    {
        // SSH.NET's command output pipe is a blocking stream; disposing it wakes an ACK read.
        using var abortRead = cancellationToken.UnsafeRegister(static stream => ((Stream)stream!).Dispose(), acknowledgements);
        try
        {
            ReadAcknowledgement(acknowledgements, encoding, cancellationToken);
            resetIdleTimeout();
            sink.Write(encoding.GetBytes(FormattableString.Invariant($"C0644 {source.Length} {fileName}\n")));
            ReadAcknowledgement(acknowledgements, encoding, cancellationToken);

            var buffer = new byte[16 * 1024];
            long remaining = source.Length;
            while (remaining > 0)
            {
                resetIdleTimeout();
                var count = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (count == 0)
                {
                    throw new EndOfStreamException("The local file changed during SCP upload.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                sink.Write(buffer, 0, count);
                remaining -= count;
            }

            resetIdleTimeout();
            sink.WriteByte(0);
            ReadAcknowledgement(acknowledgements, encoding, cancellationToken);
            resetIdleTimeout();
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private static void ReadAcknowledgement(Stream stream, Encoding encoding, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var code = stream.ReadByte();
        cancellationToken.ThrowIfCancellationRequested();
        if (code == 0)
        {
            return;
        }

        if (code is not (1 or 2))
        {
            throw new ScpException("The server did not acknowledge the SCP upload.");
        }

        var buffer = new byte[4096];
        var length = 0;
        while (length < buffer.Length && stream.ReadByte() is >= 0 and not '\n' and var value)
        {
            buffer[length++] = (byte)value;
        }

        throw new ScpException($"The server rejected the SCP upload: {encoding.GetString(buffer, 0, length)}");
    }
}
