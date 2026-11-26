using System.ComponentModel;
using System.Diagnostics;

namespace CNCSync.Infrastructure.DesktopPrinting;

public interface IPrintSpooler
{
    Task QueuePdfAsync(string queue, ReadOnlyMemory<byte> pdf, Guid jobId, CancellationToken cancellationToken);
}

public sealed class OsPrintSpooler(string? windowsPdfRendererPath = null) : IPrintSpooler
{
    private static readonly TimeSpan CupsTimeout = TimeSpan.FromSeconds(30);

    public Task QueuePdfAsync(string queue, ReadOnlyMemory<byte> pdf, Guid jobId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(queue) || queue.Length > 256 || queue[0] == '-' ||
            queue != queue.Trim() || queue.Any(char.IsControl))
        {
            throw new ArgumentException("Enter the exact OS printer queue name.", nameof(queue));
        }

        if (!pdf.Span.StartsWith("%PDF-"u8))
        {
            throw new ArgumentException("The print document must be a PDF.", nameof(pdf));
        }

        if (OperatingSystem.IsWindows())
        {
            return WindowsPdfSpooler.QueueAsync(windowsPdfRendererPath, queue, pdf, jobId, cancellationToken);
        }

        if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            return SubmitCupsAsync(CreateCupsStartInfo(queue, jobId), pdf, cancellationToken);
        }

        throw new PlatformNotSupportedException("Print sharing is unavailable on this operating system.");
    }

    internal static ProcessStartInfo CreateCupsStartInfo(string queue, Guid jobId) => new("/usr/bin/lp")
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        ArgumentList = { "-d", queue, "-t", $"PCS label {jobId:D}", "-o", "document-format=application/pdf", "-" }
    };

    internal static async Task SubmitCupsAsync(
        ProcessStartInfo startInfo, ReadOnlyMemory<byte> pdf, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CupsTimeout);
        var token = timeout.Token;

        using var process = StartCups(startInfo);
        using var termination = token.Register(() => StopSubmissionProcess(process));
        using var output = process.StandardOutput;
        using var error = process.StandardError;
        try
        {
            await Task.WhenAll(
                output.BaseStream.CopyToAsync(Stream.Null, token),
                error.BaseStream.CopyToAsync(Stream.Null, token),
                WriteInputAsync(process.StandardInput.BaseStream, pdf, token),
                process.WaitForExitAsync(token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException("CUPS did not confirm job acceptance within 30 seconds.");
        }
        catch (IOException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (IOException)
        {
            throw new IOException("CUPS could not confirm PDF submission. Check the configured OS printer queue.");
        }
        finally
        {
            StopSubmissionProcess(process);
        }

        if (process.ExitCode != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new IOException("CUPS rejected the PDF. Check the configured OS printer queue.");
        }
    }

    private static Process StartCups(ProcessStartInfo startInfo)
    {
        try
        {
            return Process.Start(startInfo) ?? throw new IOException("The CUPS submission command did not start.");
        }
        catch (Win32Exception)
        {
            throw new IOException("Could not start the CUPS submission command. Check that CUPS is installed.");
        }
    }

    private static async Task WriteInputAsync(Stream input, ReadOnlyMemory<byte> pdf, CancellationToken cancellationToken)
    {
        await using (input.ConfigureAwait(false))
        {
            await input.WriteAsync(pdf, cancellationToken).ConfigureAwait(false);
            await input.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    internal static void StopSubmissionProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch (InvalidOperationException)
        {
            // The command already exited.
        }
        catch (Win32Exception)
        {
            // Cancellation or submission failure remains the result.
        }
    }
}
