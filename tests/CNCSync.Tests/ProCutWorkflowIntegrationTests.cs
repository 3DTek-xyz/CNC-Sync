using System.Net;
using System.Net.Http.Headers;
using CNCSync.Core.Configuration;
using CNCSync.Core.Processing;
using CNCSync.Core.Services;
using CNCSync.Infrastructure.Networking;
using CNCSync.Infrastructure.Processing;

namespace CNCSync.Tests;

public sealed class ProCutWorkflowIntegrationTests
{
    [Fact]
    public async Task ProcessPathAsync_ProCutApiFileFlowStagesResponseAndUploadsToLocalDestination()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cncsync-procut-flow-{Guid.NewGuid():N}");
        var watchFolder = Path.Combine(root, "watch");
        var stagingFolder = Path.Combine(root, "staging");
        var destinationFolder = Path.Combine(root, "destination");
        Directory.CreateDirectory(watchFolder);
        Directory.CreateDirectory(destinationFolder);
        var sourceFile = Path.Combine(watchFolder, "program.nc");
        await File.WriteAllTextAsync(sourceFile, "G1 X0 Y0\n");

        try
        {
            var httpHandler = new CapturingHandler(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("G1 X10 Y20\n")
                };
                response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
                {
                    FileName = "program_processed.nc"
                };
                return response;
            });

            var coordinator = new SyncCoordinator(
                new StubFolderMonitor(),
                new StagingProjectProcessor(new HttpClient(httpHandler)),
                new DestinationService(new UnusedFtpService(), new UnusedSftpService(), new UnusedScpService(), new UnusedNetworkShareService(), new StubVpnService()),
                new AppSettingsValidator());
            var activityMessages = new List<string>();
            coordinator.ActivityLogged += entry => activityMessages.Add(entry.Message);

            var result = await coordinator.ProcessPathAsync(
                sourceFile,
                new WatchProfileSettings
                {
                    Name = "Watch 1",
                    WatchFolder = watchFolder,
                    StagingFolder = stagingFolder,
                    RemoteSubfolder = "machine-a"
                },
                new DestinationSettings
                {
                    Name = "Local destination",
                    Type = DestinationType.LocalFolder,
                    LocalRootPath = destinationFolder
                },
                new ProcessingSetupSettings
                {
                    Name = "ProCut process",
                    Mode = ProcessingMode.ProCutApi,
                    ProCutApiEndpoint = "/api/external/gcode/process",
                    ProCutArcFittingEnabled = true,
                    ProCutLineJoinerEnabled = true,
                    ProCutArcJoinerEnabled = true,
                    ProCutCornerSmoothEnabled = true
                },
                new ProCutApiSettings
                {
                    BaseUrl = "https://api.example.test",
                    ApiKey = TestSecrets.ProCutApiKey
                });

            Assert.True(result.Success);
            Assert.Equal(new Uri("https://api.example.test/api/external/gcode/process"), httpHandler.Request?.RequestUri);
            Assert.Contains("line_joiner", httpHandler.RequestContent);
            Assert.Contains("corner_smooth", httpHandler.RequestContent);
            Assert.DoesNotContain("arc_fitting", httpHandler.RequestContent);
            Assert.DoesNotContain("arc_joiner", httpHandler.RequestContent);

            var uploadedFile = Path.Combine(destinationFolder, "machine-a", "program_processed.nc");
            Assert.True(File.Exists(uploadedFile));
            Assert.Equal("G1 X10 Y20\n", await File.ReadAllTextAsync(uploadedFile));
            Assert.False(File.Exists(Path.Combine(stagingFolder, "program_processed.nc")));
            Assert.Contains(activityMessages, message => message.Contains("ProCut Suite API endpoint", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(activityMessages, message => message.Contains("ProCut Suite API tools: line_joiner, corner_smooth", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(activityMessages, message => message.Contains("ProCut Suite API upload starting: program.nc", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(activityMessages, message => message.Contains("ProCut Suite API response received: program.nc -> program_processed.nc", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(activityMessages, message => message.Contains("Local upload starting", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string RequestContent { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestContent = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responseFactory(request);
        }
    }

    private sealed class StubFolderMonitor : IFolderMonitor
    {
        public event Action<WorkItemReadyEvent>? WorkItemReady
        {
            add { }
            remove { }
        }
        public bool IsRunning => false;
        public Task StartAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubVpnService : IVpnService
    {
        public Task<IReadOnlyList<VpnConnectionInfo>> ListConnectionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VpnConnectionInfo>>([]);

        public Task<VpnConnectionEnsureResult> EnsureConnectedAsync(string connectionName, CancellationToken cancellationToken = default) =>
            Task.FromResult(VpnConnectionEnsureResult.NoRequirement());

        public Task<(bool Success, string Message)> DisconnectAsync(string connectionName, CancellationToken cancellationToken = default) =>
            Task.FromResult((true, "VPN not required."));
    }

    private sealed class UnusedFtpService : IFtpService
    {
        public Task<(bool Success, string Message)> TestConnectionAsync(DestinationSettings destination, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(bool Success, string Message)> UploadFileSystemItemAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, IProgress<string>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(bool Success, string Message)> UploadDirectoryAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, IProgress<string>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)> ListRootEntriesAsync(DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(bool Exists, long? SizeBytes, string Message)> TryGetFileSizeAsync(DestinationSettings destination, string remoteFilePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(bool Success, string Message)> DeleteRemoteItemAsync(DestinationSettings destination, string remotePath, bool isDirectory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnusedSftpService : ISftpService
    {
        public Task<(bool Success, string Message)> TestConnectionAsync(DestinationSettings destination, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(bool Success, string Message)> UploadFileSystemItemAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, IProgress<string>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(bool Success, string Message)> UploadDirectoryAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, IProgress<string>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)> ListRootEntriesAsync(DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(bool Exists, long? SizeBytes, string Message)> TryGetFileSizeAsync(DestinationSettings destination, string remoteFilePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(bool Success, string Message)> DeleteRemoteItemAsync(DestinationSettings destination, string remotePath, bool isDirectory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnusedScpService : IScpService
    {
        public Task<(bool Success, string Message)> TestConnectionAsync(DestinationSettings destination, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(bool Success, string Message)> UploadFileSystemItemAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, IProgress<string>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(bool Success, string Message)> UploadDirectoryAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, IProgress<string>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)> ListRootEntriesAsync(DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(bool Exists, long? SizeBytes, string Message)> TryGetFileSizeAsync(DestinationSettings destination, string remoteFilePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(bool Success, string Message)> DeleteRemoteItemAsync(DestinationSettings destination, string remotePath, bool isDirectory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnusedNetworkShareService : INetworkShareService
    {
        public Task<(bool Success, string Message)> TestConnectionAsync(DestinationSettings destination, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(bool Success, string Message)> UploadFileSystemItemAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, IProgress<string>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(bool Success, string Message)> UploadDirectoryAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, IProgress<string>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)> ListRootEntriesAsync(DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(bool Exists, long? SizeBytes, string Message)> TryGetFileSizeAsync(DestinationSettings destination, string remoteFilePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(bool Success, string Message)> DeleteRemoteItemAsync(DestinationSettings destination, string remotePath, bool isDirectory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
