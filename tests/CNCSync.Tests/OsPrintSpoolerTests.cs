using System.Diagnostics;
using CNCSync.Infrastructure.DesktopPrinting;

namespace CNCSync.Tests;

public sealed class OsPrintSpoolerTests
{
    private static readonly byte[] Pdf = "%PDF-1.7\n%%EOF"u8.ToArray();
    private static readonly Guid JobId = Guid.Parse("240c50d7-5bb1-4e66-b3c4-c4bb249b4f5a");

    [Theory]
    [InlineData("")]
    [InlineData(" padded")]
    [InlineData("queue\0other")]
    [InlineData("queue\nother")]
    [InlineData("-option")]
    public async Task QueueRejectsInvalidNamesBeforeStartingAnOsJob(string queue)
    {
        var spooler = new OsPrintSpooler();
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await spooler.QueuePdfAsync(queue, Pdf, JobId, CancellationToken.None));
    }

    [Fact]
    public async Task QueueRejectsNonPdfBeforeStartingAnOsJob()
    {
        var spooler = new OsPrintSpooler();
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await spooler.QueuePdfAsync("configured-queue", "plain text"u8.ToArray(), JobId, CancellationToken.None));
    }

    [Fact]
    public async Task AlreadyCancelledSubmissionDoesNotOpenTheQueue()
    {
        var spooler = new OsPrintSpooler();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await spooler.QueuePdfAsync("configured-queue", Pdf, JobId, new CancellationToken(true)));
    }

    [Fact]
    public void CupsUsesTheExactQueueAndReadsPdfFromStdin()
    {
        var args = OsPrintSpooler.CreateCupsStartInfo("queue;literal", JobId);
        Assert.Equal("/usr/bin/lp", args.FileName);
        Assert.False(args.UseShellExecute);
        Assert.True(args.RedirectStandardInput);
        Assert.Equal(["-d", "queue;literal", "-t", $"PCS label {JobId:D}", "-o", "document-format=application/pdf", "-"],
            args.ArgumentList);
    }

    [Fact]
    public async Task CupsConfirmsAcceptanceAndKeepsProcessOutputOutOfErrors()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            return;
        }

        var capturePath = Path.GetTempFileName();
        try
        {
            var capture = TestProcess("cat > \"$CUPS_TEST_CAPTURE\"");
            capture.Environment["CUPS_TEST_CAPTURE"] = capturePath;
            await OsPrintSpooler.SubmitCupsAsync(capture, Pdf, CancellationToken.None);
            Assert.Equal(Pdf, await File.ReadAllBytesAsync(capturePath));
        }
        finally
        {
            File.Delete(capturePath);
        }

        var failure = await Assert.ThrowsAsync<IOException>(async () =>
            await OsPrintSpooler.SubmitCupsAsync(TestProcess("cat >/dev/null; printf 'private text' >&2; exit 1"),
                Pdf, CancellationToken.None));
        Assert.DoesNotContain("private text", failure.Message);
    }

    [Fact]
    public async Task CupsTerminatesTheProcessWhenCancelled()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            return;
        }

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var submission = OsPrintSpooler.SubmitCupsAsync(TestProcess("exec /bin/sleep 30"), Pdf, cancellation.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await submission.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void WindowsUsesExactQueueAndSilentActualSizePrinting()
    {
        const string renderer = @"C:\Program Files\SumatraPDF\SumatraPDF.exe";
        const string pdfPath = @"C:\Users\Tester\AppData\Local\Temp\PCS label.pdf";
        const string queue = "Workshop \"Labels\";West";
        var startInfo = WindowsPdfSpooler.CreateStartInfo(renderer, queue, pdfPath);

        Assert.Equal(renderer, startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(["-silent", "-print-to", queue, "-print-settings", "noscale", pdfPath], startInfo.ArgumentList);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SumatraPDF.exe")]
    [InlineData("/does-not-exist/SumatraPDF.exe")]
    public void WindowsRejectsMissingOrRelativeRendererPaths(string? executable)
    {
        Assert.False(WindowsPdfSpooler.IsConfiguredExecutable(executable));
    }

    [Fact]
    public async Task WindowsRejectsAnUnverifiedExecutableEvenWithTheSumatraName()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var executable = Path.Combine(directory.FullName, "SumatraPDF.exe");
            await File.WriteAllTextAsync(executable, "not a verified Windows executable");

            Assert.False(WindowsPdfSpooler.IsConfiguredExecutable(executable));
            await Assert.ThrowsAsync<IOException>(() => WindowsPdfSpooler.QueueAsync(
                executable, "test-queue", Pdf, JobId, CancellationToken.None));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task WindowsStagesPdfForTheRendererAndDeletesItOnSuccess()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string? stagedPath = null;
        var capturePath = Path.GetTempFileName();
        try
        {
            await WindowsPdfSpooler.SubmitAsync(path =>
            {
                stagedPath = path;
                var capture = TestProcess("cat \"$SUMATRA_TEST_INPUT\" > \"$SUMATRA_TEST_CAPTURE\"");
                capture.Environment["SUMATRA_TEST_INPUT"] = path;
                capture.Environment["SUMATRA_TEST_CAPTURE"] = capturePath;
                return capture;
            }, Pdf, JobId, CancellationToken.None);

            Assert.Equal(Pdf, await File.ReadAllBytesAsync(capturePath));
            Assert.Equal($"PCS label {JobId:D}.pdf", Path.GetFileName(stagedPath));
            Assert.False(Directory.Exists(Path.GetDirectoryName(stagedPath!)));
        }
        finally
        {
            File.Delete(capturePath);
        }
    }

    [Fact]
    public async Task WindowsRejectsNonzeroExitAndDeletesTheStagedPdf()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string? stagedPath = null;
        var failure = await Assert.ThrowsAsync<IOException>(() => WindowsPdfSpooler.SubmitAsync(path =>
        {
            stagedPath = path;
            return TestProcess("printf 'private text' >&2; exit 5");
        }, Pdf, JobId, CancellationToken.None));

        Assert.Contains("exit code 5", failure.Message);
        Assert.DoesNotContain("private text", failure.Message);
        Assert.False(Directory.Exists(Path.GetDirectoryName(stagedPath!)));
    }

    [Fact]
    public async Task WindowsCancellationTerminatesTheRendererAndDeletesTheStagedPdf()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        string? stagedPath = null;
        var submission = WindowsPdfSpooler.SubmitAsync(path =>
        {
            stagedPath = path;
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(200));
            return TestProcess("exec /bin/sleep 30");
        }, Pdf, JobId, cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => submission.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(Directory.Exists(Path.GetDirectoryName(stagedPath!)));
    }

    private static ProcessStartInfo TestProcess(string command) => new("/bin/sh")
    {
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        ArgumentList = { "-c", command }
    };
}
