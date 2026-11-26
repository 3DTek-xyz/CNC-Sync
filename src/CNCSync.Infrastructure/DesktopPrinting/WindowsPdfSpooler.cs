using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;

namespace CNCSync.Infrastructure.DesktopPrinting;

internal static class WindowsPdfSpooler
{
    private static readonly TimeSpan SubmissionTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(2);

    internal const string RendererRequiredMessage =
        "Select the installed or portable SumatraPDF app with a valid Windows publisher signature.";

    internal static bool IsConfiguredExecutable(string? path)
    {
        using var executable = OpenVerifiedExecutable(path);
        return executable is not null;
    }

    internal static async Task QueueAsync(
        string? executable, string queue, ReadOnlyMemory<byte> pdf, Guid jobId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Keep the verified file open without write or delete sharing through submission.
        using var renderer = OpenVerifiedExecutable(executable) ?? throw new IOException(RendererRequiredMessage);

        await SubmitAsync(path => CreateStartInfo(renderer.Name, queue, path), pdf, jobId, cancellationToken)
            .ConfigureAwait(false);
    }

    private static FileStream? OpenVerifiedExecutable(string? path)
    {
        if (!OperatingSystem.IsWindows() || !IsLocalExecutablePath(path))
        {
            return null;
        }

        FileStream? executable = null;
        var approved = false;
        try
        {
            executable = File.Open(path!, FileMode.Open, FileAccess.Read, FileShare.Read);
            approved = WindowsSumatraSignature.IsTrusted(executable) &&
                string.Equals(FileVersionInfo.GetVersionInfo(executable.Name).ProductName,
                    "SumatraPDF", StringComparison.Ordinal);
            return approved ? executable : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
            Win32Exception or CryptographicException)
        {
            return null;
        }
        finally
        {
            if (!approved)
            {
                executable?.Dispose();
            }
        }
    }

    private static bool IsLocalExecutablePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        Path.IsPathFullyQualified(path) &&
        !path.StartsWith(@"\\", StringComparison.Ordinal) &&
        !path.StartsWith("//", StringComparison.Ordinal) &&
        !Path.GetFileNameWithoutExtension(path).EndsWith("-install", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase);

    internal static ProcessStartInfo CreateStartInfo(string executable, string queue, string pdfPath) => new(executable)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        ArgumentList = { "-silent", "-print-to", queue, "-print-settings", "noscale", pdfPath }
    };

    internal static async Task SubmitAsync(
        Func<string, ProcessStartInfo> createStartInfo,
        ReadOnlyMemory<byte> pdf,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = CreateStagingDirectory();
        try
        {
            var path = Path.Combine(directory, $"PCS label {jobId:D}.pdf");
            await WritePdfAsync(path, pdf, cancellationToken).ConfigureAwait(false);
            await RunRendererAsync(createStartInfo(path), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            RemoveStagingDirectory(directory);
        }
    }

    private static string CreateStagingDirectory()
    {
        try
        {
            return Directory.CreateTempSubdirectory("pcs-label-").FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException("Could not prepare a temporary label print directory.");
        }
    }

    private static async Task WritePdfAsync(string path, ReadOnlyMemory<byte> pdf, CancellationToken cancellationToken)
    {
        try
        {
            await File.WriteAllBytesAsync(path, pdf, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException("Could not prepare the temporary label PDF.");
        }
    }

    private static async Task RunRendererAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SubmissionTimeout);
        var token = timeout.Token;

        using var process = StartRenderer(startInfo);
        using var termination = token.Register(() => OsPrintSpooler.StopSubmissionProcess(process));
        using var output = process.StandardOutput;
        using var error = process.StandardError;
        try
        {
            await Task.WhenAll(
                output.BaseStream.CopyToAsync(Stream.Null, token),
                error.BaseStream.CopyToAsync(Stream.Null, token),
                process.WaitForExitAsync(token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException("SumatraPDF did not confirm print submission within 30 seconds.");
        }
        catch (IOException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new IOException("Could not read the SumatraPDF print result.");
        }
        finally
        {
            await StopRendererAsync(process).ConfigureAwait(false);
        }

        if (process.ExitCode != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new IOException($"SumatraPDF could not submit the label print (exit code {process.ExitCode}). Check the configured Windows printer queue.");
        }
    }

    private static Process StartRenderer(ProcessStartInfo startInfo)
    {
        try
        {
            return Process.Start(startInfo) ?? throw new IOException("SumatraPDF did not start.");
        }
        catch (Win32Exception)
        {
            throw new IOException("Could not start SumatraPDF. Check the configured executable.");
        }
    }

    private static async Task StopRendererAsync(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        OsPrintSpooler.StopSubmissionProcess(process);
        using var cleanup = new CancellationTokenSource(CleanupTimeout);
        try
        {
            await process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Preserve the result of the original submission if the process cannot be reaped.
        }
    }

    private static void RemoveStagingDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cleanup cannot change the printer's submission result.
        }
    }
}
