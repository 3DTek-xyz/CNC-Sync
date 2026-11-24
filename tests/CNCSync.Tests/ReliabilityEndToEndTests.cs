using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using CNCSync.Core.Configuration;
using CNCSync.Core.Processing;
using CNCSync.Core.Services;
using CNCSync.Infrastructure.Monitoring;
using CNCSync.Infrastructure.Networking;
using CNCSync.Infrastructure.Processing;

namespace CNCSync.Tests;

public sealed class ReliabilityEndToEndTests
{
    private const string Input = "G1 X0 Y0\n";
    private const string Processed = "G1 X10 Y20\n";
    private const string Program = "program.nc";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task WatcherDrop_SubmitsSelectedToolsAndDeliversProcessedFile()
    {
        using var workspace = new Workspace();
        using var handler = new ScriptedHandler((_, request) => Ok(request.FileName));
        using var client = new HttpClient(handler);
        await using var coordinator = workspace.CreateCoordinator(client);
        using var deadline = new CancellationTokenSource(TestTimeout);
        var completed = ObserveCompletions(coordinator);

        await coordinator.StartAsync(workspace.Settings, deadline.Token);
        await workspace.DropAsync(Program, deadline.Token);

        var result = await completed.ReadAsync(deadline.Token);
        Assert.True(result.Success, result.Message);
        await coordinator.StopAsync(deadline.Token);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("https://api.example.test/api/external/gcode/process"), request.Uri);
        Assert.Equal(Program, request.FileName);
        Assert.Equal(Input, request.Source);
        Assert.Equal(new[] { "line_joiner", "corner_smooth" }, request.Tools);
        Assert.Equal(Processed, await File.ReadAllTextAsync(workspace.DeliveredPath(Program), deadline.Token));
        Assert.Equal(Input, await File.ReadAllTextAsync(workspace.InputPath(Program), deadline.Token));
    }

    [Fact]
    public async Task WatcherDropsWithSameName_DeliverToDistinctJobFolders()
    {
        const string otherInput = "G1 X30 Y40\n";
        const string otherProcessed = "G1 X50 Y60\n";
        var first = Path.Combine("job-a", Program);
        var second = Path.Combine("job-b", Program);
        using var workspace = new Workspace();
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.InputPath(first))!);
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.InputPath(second))!);
        using var handler = new ScriptedHandler((_, request) =>
            Ok(request.FileName, request.Source == Input ? Processed : otherProcessed));
        using var client = new HttpClient(handler);
        await using var coordinator = workspace.CreateCoordinator(client);
        using var deadline = new CancellationTokenSource(TestTimeout);
        var completed = ObserveCompletions(coordinator);

        await coordinator.StartAsync(workspace.Settings, deadline.Token);
        await workspace.DropAsync(first, deadline.Token);
        await workspace.DropAsync(second, deadline.Token, otherInput);

        for (var i = 0; i < 2; i++)
        {
            var result = await completed.ReadAsync(deadline.Token);
            Assert.True(result.Success, result.Message);
        }

        await coordinator.StopAsync(deadline.Token);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(Processed, await File.ReadAllTextAsync(workspace.DeliveredPath(first), deadline.Token));
        Assert.Equal(otherProcessed, await File.ReadAllTextAsync(workspace.DeliveredPath(second), deadline.Token));
        Assert.False(File.Exists(workspace.DeliveredPath(Program)));
    }

    [Fact]
    public async Task TruncatedResponse_IsNeverDeliveredByCatchUp()
    {
        using var workspace = new Workspace();
        var prefixWritten = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new ScriptedHandler((_, request) => Response(request.FileName, new InterruptedContent(prefixWritten)));
        using var client = new HttpClient(handler);
        await using var coordinator = workspace.CreateCoordinator(client);
        using var deadline = new CancellationTokenSource(TestTimeout);
        var completed = ObserveCompletions(coordinator);

        await coordinator.StartAsync(workspace.Settings, deadline.Token);
        await workspace.DropAsync(Program, deadline.Token);

        var failed = await completed.ReadAsync(deadline.Token);
        Assert.False(failed.Success, failed.Message);
        Assert.True(prefixWritten.Task.IsCompletedSuccessfully, "The simulated connection must drop after writing a prefix.");
        await coordinator.StopAsync(deadline.Token);
        var attempts = handler.Requests.Count;
        Assert.Empty(Directory.EnumerateFiles(workspace.Destination.LocalRootPath, "*", SearchOption.AllDirectories));

        var catchUp = await coordinator.CatchUpMissingItemsAsync(
            workspace.Profile, workspace.Destination, workspace.Setup, workspace.Settings.ProCutApi, deadline.Token);

        Assert.True(catchUp.Success, catchUp.Message);
        Assert.Empty(Directory.EnumerateFiles(workspace.Destination.LocalRootPath, "*", SearchOption.AllDirectories));
        Assert.Equal(attempts, handler.Requests.Count);
    }

    [Fact]
    public async Task RateLimitedWatcherDrop_RetriesAndDeliversWithoutAnotherFileChange()
    {
        using var workspace = new Workspace();
        var retryAfter = TimeSpan.FromSeconds(6);
        using var handler = new ScriptedHandler((attempt, request) =>
        {
            if (attempt != 1)
            {
                return Ok(request.FileName);
            }

            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter);
            return response;
        });
        using var client = new HttpClient(handler);
        await using var coordinator = workspace.CreateCoordinator(client);
        using var deadline = new CancellationTokenSource(TestTimeout);
        var completed = ObserveCompletions(coordinator);

        await coordinator.StartAsync(workspace.Settings, deadline.Token);
        await workspace.DropAsync(Program, deadline.Token);

        ProcessingResult result;
        do
        {
            result = await completed.ReadAsync(deadline.Token);
        }
        while (!result.Success);

        await coordinator.StopAsync(deadline.Token);
        var attempts = handler.Requests.ToArray();
        Assert.Collection(attempts,
            first => Assert.Equal((Program, Input), (first.FileName, first.Source)),
            retry => Assert.Equal((Program, Input), (retry.FileName, retry.Source)));
        Assert.True(Stopwatch.GetElapsedTime(attempts[0].Timestamp, attempts[1].Timestamp) >= retryAfter,
            "The second API request must not start before Retry-After has elapsed.");
        Assert.Equal(Processed, await File.ReadAllTextAsync(workspace.DeliveredPath(Program), deadline.Token));
    }

    [Fact]
    public async Task WatcherDrop_SubmitsNewJobFolderWithoutSubmittingExistingJobs()
    {
        var existing = Path.Combine("existing-job", "historical.nc");
        var newInput = Path.Combine("new-job", Program);
        using var workspace = new Workspace();
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.InputPath(existing))!);
        using var deadline = new CancellationTokenSource(TestTimeout);
        await workspace.DropAsync(existing, deadline.Token);
        using var handler = new ScriptedHandler((_, request) => Ok(request.FileName));
        using var client = new HttpClient(handler);
        await using var coordinator = workspace.CreateCoordinator(client);
        var completed = ObserveCompletions(coordinator);

        await coordinator.StartAsync(workspace.Settings, deadline.Token);
        await Task.Delay(TimeSpan.FromSeconds(3), deadline.Token);
        Assert.Empty(handler.Requests);

        await workspace.DropFolderAsync("new-job", Program, deadline.Token);
        var result = await completed.ReadAsync(deadline.Token);
        Assert.True(result.Success, result.Message);
        await coordinator.StopAsync(deadline.Token);

        Assert.Equal(Program, Assert.Single(handler.Requests).FileName);
        Assert.Equal(Processed, await File.ReadAllTextAsync(workspace.DeliveredPath(newInput), deadline.Token));
        Assert.False(File.Exists(workspace.DeliveredPath(existing)));
    }

    [Fact]
    public async Task Restart_DeduplicatesLiveEventForPreviouslyDeliveredFile()
    {
        const string newFile = "second.nc";
        using var workspace = new Workspace();
        using var handler = new ScriptedHandler((_, request) => Ok(request.FileName));
        using var client = new HttpClient(handler);
        using var deadline = new CancellationTokenSource(TestTimeout);

        await using (var coordinator = workspace.CreateCoordinator(client))
        {
            var completed = ObserveCompletions(coordinator);
            await coordinator.StartAsync(workspace.Settings, deadline.Token);
            await workspace.DropAsync(Program, deadline.Token);
            var result = await completed.ReadAsync(deadline.Token);
            Assert.True(result.Success, result.Message);
            await coordinator.StopAsync(deadline.Token);
        }

        var originalPath = workspace.InputPath(Program);
        var originalFingerprint = WorkItemFingerprint.Capture(originalPath, includeContent: true);
        var parkedPath = Path.Combine(Path.GetDirectoryName(workspace.Profile.WatchFolder)!, Program);
        File.Move(originalPath, parkedPath);

        var restartedMonitor = new FileSystemFolderMonitor();
        await using (var coordinator = workspace.CreateCoordinator(client, restartedMonitor, maxConcurrentJobs: 1))
        {
            var completed = ObserveCompletions(coordinator);
            var originalReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            restartedMonitor.WorkItemReady += item =>
            {
                if (Path.GetFileName(item.Path) == Program)
                {
                    originalReady.TrySetResult();
                }
            };

            await coordinator.StartAsync(workspace.Settings, deadline.Token);
            File.Move(parkedPath, originalPath);
            Assert.Equal(originalFingerprint, WorkItemFingerprint.Capture(originalPath, includeContent: true));
            // The coordinator subscribes first. With one worker, the second drop
            // can't finish until the live event for the original file has been considered.
            await originalReady.Task.WaitAsync(deadline.Token);
            await workspace.DropAsync(newFile, deadline.Token);
            var result = await completed.ReadAsync(deadline.Token);
            Assert.True(result.Success, result.Message);
            Assert.Equal(newFile, Path.GetFileName(result.SourcePath));
            await coordinator.StopAsync(deadline.Token);
        }

        Assert.Equal(new[] { Program, newFile }, handler.Requests.Select(request => request.FileName));
        Assert.Equal(Processed, await File.ReadAllTextAsync(workspace.DeliveredPath(Program), deadline.Token));
        Assert.Equal(Processed, await File.ReadAllTextAsync(workspace.DeliveredPath(newFile), deadline.Token));
    }

    private static ChannelReader<ProcessingResult> ObserveCompletions(SyncCoordinator coordinator)
    {
        var completed = Channel.CreateUnbounded<ProcessingResult>();
        coordinator.ProcessingCompleted += result => completed.Writer.TryWrite(result);
        return completed.Reader;
    }

    private static HttpResponseMessage Ok(string inputName, string content = Processed) =>
        Response(inputName, new StringContent(content, Encoding.UTF8, "text/plain"));

    private static HttpResponseMessage Response(string inputName, HttpContent content)
    {
        content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
        {
            FileName = OutputName(inputName)
        };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static string OutputName(string inputName) => $"{Path.GetFileNameWithoutExtension(inputName)}-processed.nc";

    private sealed class InterruptedContent : HttpContent
    {
        private readonly TaskCompletionSource _prefixWritten;

        public InterruptedContent(TaskCompletionSource prefixWritten)
        {
            _prefixWritten = prefixWritten;
            Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await stream.WriteAsync("G1 X10"u8.ToArray());
            _prefixWritten.TrySetResult();
            throw new IOException("Simulated connection drop after a partial G-code response.");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = Encoding.UTF8.GetByteCount(Processed);
            return true;
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri? Uri, string FileName, string Source, string[] Tools, long Timestamp);

    private sealed class ScriptedHandler(Func<int, CapturedRequest, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private int _attempts;
        public ConcurrentQueue<CapturedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var timestamp = Stopwatch.GetTimestamp();
            var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
            var source = Assert.Single(multipart, part => part.Headers.ContentDisposition?.Name?.Trim('"') == "file");
            var tools = Assert.Single(multipart, part => part.Headers.ContentDisposition?.Name?.Trim('"') == "tools");
            using var json = JsonDocument.Parse(await tools.ReadAsStringAsync(cancellationToken));
            var captured = new CapturedRequest(
                request.Method,
                request.RequestUri,
                source.Headers.ContentDisposition?.FileName?.Trim('"') ?? "",
                await source.ReadAsStringAsync(cancellationToken),
                json.RootElement.EnumerateArray().Select(tool => tool.GetProperty("type").GetString()!).ToArray(),
                timestamp);
            Requests.Enqueue(captured);
            return respond(Interlocked.Increment(ref _attempts), captured);
        }
    }

    private sealed class Workspace : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"cncsync-reliability-{Guid.NewGuid():N}");
        private readonly ScpService _scp = new();

        public Workspace()
        {
            Destination = new DestinationSettings
            {
                Name = "Local test destination",
                Type = DestinationType.LocalFolder,
                LocalRootPath = Path.Combine(_root, "output")
            };
            Setup = new ProcessingSetupSettings
            {
                Name = "API test processing",
                Mode = ProcessingMode.ProCutApi,
                ProCutApiEndpoint = "/api/external/gcode/process",
                ProCutLineJoinerEnabled = true,
                ProCutCornerSmoothEnabled = true
            };
            Profile = new WatchProfileSettings
            {
                Name = "Reliability test watch",
                WatchFolder = Path.Combine(_root, "watch"),
                StagingFolder = Path.Combine(_root, "staging"),
                RemoteSubfolder = "machine-a",
                DestinationId = Destination.Id,
                ProcessingSetupId = Setup.Id,
                StabilityDelaySeconds = 1,
                StabilityPollingSeconds = 1
            };
            Directory.CreateDirectory(Profile.WatchFolder);
            Directory.CreateDirectory(Destination.LocalRootPath);
            Settings = new AppSettings
            {
                WatchProfiles = [Profile],
                Destinations = [Destination],
                ProcessingSetups = [Setup],
                ProCutApi = new ProCutApiSettings { BaseUrl = "https://api.example.test", ApiKey = TestSecrets.ProCutApiKey }
            };
        }

        public AppSettings Settings { get; }
        public WatchProfileSettings Profile { get; }
        public DestinationSettings Destination { get; }
        public ProcessingSetupSettings Setup { get; }
        public string InputPath(string name) => Path.Combine(Profile.WatchFolder, name);
        public string DeliveredPath(string inputName) =>
            Path.Combine(Destination.LocalRootPath, "machine-a", Path.GetDirectoryName(inputName) ?? "", OutputName(inputName));

        public async Task DropAsync(string name, CancellationToken cancellationToken, string content = Input)
        {
            var incoming = Path.Combine(_root, Guid.NewGuid().ToString("N"));
            await File.WriteAllTextAsync(incoming, content, cancellationToken);
            File.Move(incoming, InputPath(name));
        }

        public async Task DropFolderAsync(string name, string fileName, CancellationToken cancellationToken)
        {
            var incoming = Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N")));
            await File.WriteAllTextAsync(Path.Combine(incoming.FullName, fileName), Input, cancellationToken);
            Directory.Move(incoming.FullName, InputPath(name));
        }

        public SyncCoordinator CreateCoordinator(HttpClient client, FileSystemFolderMonitor? monitor = null, int maxConcurrentJobs = 2) =>
            new(monitor ?? new FileSystemFolderMonitor(), new StagingProjectProcessor(client),
                new DestinationService(new FtpService(), new SftpService(), _scp,
                    new NetworkShareService(), new SystemVpnService()),
                new AppSettingsValidator(), maxConcurrentJobs);

        public void Dispose()
        {
            _scp.Dispose();
            Directory.Delete(_root, recursive: true);
        }
    }
}
