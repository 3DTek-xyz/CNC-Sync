using System.IO.Pipes;

namespace CNCSync.App.Services;

public static class SingleInstanceSignal
{
    private const string PipeName = "procut-suite-desktop-activate";

    public static async Task<bool> TrySignalExistingInstanceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var client = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.Out,
                PipeOptions.Asynchronous);

            await client.ConnectAsync(500, cancellationToken);
            await client.WriteAsync(new byte[] { 1 }, cancellationToken);
            await client.FlushAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static Task RunServerAsync(Func<Task> onSignal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onSignal);
        return Task.Run(() => RunServerLoopAsync(onSignal, cancellationToken), cancellationToken);
    }

    private static async Task RunServerLoopAsync(Func<Task> onSignal, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken);
                var buffer = new byte[1];
                _ = await server.ReadAsync(buffer, cancellationToken);
                await onSignal();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(250, cancellationToken);
            }
        }
    }
}
