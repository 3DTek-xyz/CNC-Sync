using System.Net;
using System.Net.Sockets;
using System.Text;
using CNCSync.Core.Configuration;
using CNCSync.Core.Services;
using CNCSync.Infrastructure.Networking;

namespace CNCSync.Tests;

public sealed class SshTransferCancellationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Upload_CancelsWhenServerStallsDuringHandshake(bool useScp, bool verifyFingerprint)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var folder = new TempFolder();
        var file = Path.Combine(folder.Path, "test.nc");
        await File.WriteAllTextAsync(file, "G0 X0");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var scp = new ScpService();
        var destination = new DestinationSettings
        {
            Type = useScp ? DestinationType.Scp : DestinationType.Sftp,
            Host = "127.0.0.1",
            Port = ((IPEndPoint)listener.LocalEndpoint).Port,
            Username = "test",
            Password = "test",
            RequireSshHostKeyFingerprint = verifyFingerprint,
            SshHostKeyFingerprint = verifyFingerprint ? SshHostFingerprintTests.Fingerprint : string.Empty
        };
        Task<(bool Success, string Message)> upload = useScp
            ? scp.UploadFileSystemItemAsync(file, destination, "/jobs", cancellationToken: cancellation.Token)
            : new SftpService().UploadFileSystemItemAsync(file, destination, "/jobs", cancellationToken: cancellation.Token);

        using var server = await listener.AcceptTcpClientAsync(cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => upload.WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task ScpUpload_CancellationDisposesTheStalledAcknowledgementStream()
    {
        using var source = new MemoryStream("G0 X0"u8.ToArray());
        using var sink = new MemoryStream();
        using var acknowledgements = new StalledAcknowledgementStream();
        using var cancellation = new CancellationTokenSource();
        var upload = Task.Run(() => ScpUploadProtocol.Send(source, sink, acknowledgements, "test.nc",
            Encoding.UTF8, () => { }, cancellation.Token));

        await acknowledgements.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => upload.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(0, sink.Length);
    }

    [Fact]
    public void ScpUpload_UsesHeaderAndPayloadWithThreeAcknowledgements()
    {
        using var source = new MemoryStream("G0 X0"u8.ToArray());
        using var sink = new MemoryStream();
        using var acknowledgements = new MemoryStream([0, 0, 0]);

        ScpUploadProtocol.Send(source, sink, acknowledgements, "part name.nc", Encoding.UTF8, () => { }, CancellationToken.None);

        Assert.Equal("C0644 5 part name.nc\nG0 X0\0", Encoding.UTF8.GetString(sink.ToArray()));
        Assert.Equal(3, acknowledgements.Position);
    }

    private sealed class StalledAcknowledgementStream : MemoryStream
    {
        private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override int ReadByte()
        {
            ReadStarted.TrySetResult();
            _disposed.Task.GetAwaiter().GetResult();
            return -1;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _disposed.TrySetResult();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("cncsync-ssh-").FullName;
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
