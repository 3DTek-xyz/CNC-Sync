using System.Net;
using System.Net.Http.Headers;
using CNCSync.Core.Configuration;
using CNCSync.Core.Processing;
using CNCSync.Core.Services;
using CNCSync.Infrastructure.Monitoring;
using CNCSync.Infrastructure.Networking;
using CNCSync.Infrastructure.Processing;

namespace CNCSync.Tests;

public sealed class SafeOutputTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pcs-safe-output-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("", "text/plain")]
    [InlineData(" \r\n\t", "text/plain")]
    [InlineData("\uFEFF \n<!DOCTYPE html><html></html>", "text/plain")]
    [InlineData("\r\n{\"error\":\"bad request\"}", "text/plain")]
    [InlineData("[\"error\"]", "text/plain")]
    [InlineData("null", "text/plain")]
    [InlineData("123", "text/plain")]
    [InlineData("G1 X1", "application/json")]
    [InlineData("G1 X1\0", "text/plain")]
    public async Task InvalidSuccessBodyIsNotStaged(string body, string mediaType)
    {
        var profile = CreateProfile();
        var result = await ProcessApiAsync(CreateSource("program.nc"), profile, () => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, mediaType)
        });

        Assert.False(result.Success);
        Assert.False(result.IsRetryable);
        Assert.Null(result.CompletedOutput);
        Assert.Empty(Directory.EnumerateFileSystemEntries(profile.StagingFolder));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedOrShortBodyLeavesNoStagedPayload(bool throwAfterWrite)
    {
        var profile = CreateProfile();
        var result = await ProcessApiAsync(CreateSource("program.nc"), profile, () => new(HttpStatusCode.OK)
        {
            Content = new ShortContent(throwAfterWrite)
        });

        Assert.False(result.Success);
        Assert.True(result.IsRetryable);
        Assert.Null(result.CompletedOutput);
        Assert.Empty(Directory.EnumerateFileSystemEntries(profile.StagingFolder));
    }

    [Fact]
    public async Task OneFailedResponsePreventsPublishingEntireDirectory()
    {
        var profile = CreateProfile();
        CreateSource("job/first.nc");
        CreateSource("job/second.nc");
        var responseCount = 0;
        var result = await ProcessApiAsync(Path.Combine(profile.WatchFolder, "job"), profile, () =>
            ++responseCount == 1 ? SuccessResponse("G1 X1\n") : new(HttpStatusCode.OK) { Content = new ShortContent(true) });

        Assert.False(result.Success);
        Assert.True(result.IsRetryable);
        Assert.Equal(2, responseCount);
        Assert.Empty(CompletedStagedOutput.Enumerate(profile.StagingFolder, profile.Id));
        Assert.Empty(Directory.EnumerateFileSystemEntries(profile.StagingFolder));
    }

    [Theory]
    [InlineData("../escaped.nc")]
    [InlineData("..\\escaped.nc")]
    [InlineData("NUL.nc")]
    public async Task UnsafeAttachmentFilenameIsRejected(string filename)
    {
        var profile = CreateProfile();
        var result = await ProcessApiAsync(CreateSource("program.nc"), profile, () =>
        {
            var response = SuccessResponse("G1 X1\n");
            response.Content.Headers.ContentDisposition!.FileNameStar = filename;
            return response;
        });

        Assert.False(result.Success);
        Assert.False(result.IsRetryable);
        Assert.Empty(CompletedStagedOutput.Enumerate(profile.StagingFolder, profile.Id));
    }

    [Fact]
    public async Task ExplicitPartialResponseIsNotPublishedEvenWhenLengthMatches()
    {
        var profile = CreateProfile();
        var result = await ProcessApiAsync(CreateSource("program.nc"), profile, () =>
        {
            var response = SuccessResponse("G1 X1\n");
            response.StatusCode = HttpStatusCode.PartialContent;
            return response;
        });

        Assert.False(result.Success);
        Assert.True(result.IsRetryable);
        Assert.Empty(CompletedStagedOutput.Enumerate(profile.StagingFolder, profile.Id));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.Redirect, false)]
    public async Task ApiFailureClassifiesRetryWithoutLoggingResponseBody(HttpStatusCode status, bool retryable)
    {
        var profile = CreateProfile();
        var result = await ProcessApiAsync(CreateSource("program.nc"), profile, () =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent("private server response") };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return response;
        });

        Assert.False(result.Success);
        Assert.Equal(retryable, result.IsRetryable);
        Assert.Equal(retryable ? TimeSpan.FromSeconds(7) : null, result.RetryAfter);
        Assert.DoesNotContain("private server response", result.Message);
    }

    [Fact]
    public async Task ChangedInputIsNotPublished()
    {
        var profile = CreateProfile();
        CreateSource("job/program.nc");
        var result = await ProcessApiAsync(Path.Combine(profile.WatchFolder, "job"), profile, () =>
        {
            CreateSource("job/added.nc");
            return SuccessResponse("G0 X1\n");
        });

        Assert.False(result.Success);
        Assert.True(result.IsRetryable);
        Assert.Empty(CompletedStagedOutput.Enumerate(profile.StagingFolder, profile.Id));
    }

    [Fact]
    public async Task SameBasenameApiJobsKeepSeparateCompletedPayloads()
    {
        var profile = CreateProfile();
        var firstSource = CreateSource("a/program.nc");
        var secondSource = CreateSource("b/program.nc");
        var results = await Task.WhenAll(
            ProcessApiAsync(firstSource, profile, () => SuccessResponse("%\n(First)\nG1 X1\n")),
            ProcessApiAsync(secondSource, profile, () => SuccessResponse("%\n(Second)\nG1 X2\n")));

        Assert.All(results, result => Assert.True(result.Success, result.Message));
        Assert.NotEqual(results[0].OutputPath, results[1].OutputPath);
        Assert.Equal("result.nc", Path.GetFileName(results[0].OutputPath));
        Assert.Equal("%\n(First)\nG1 X1\n", File.ReadAllText(results[0].OutputPath));
        Assert.Equal("%\n(Second)\nG1 X2\n", File.ReadAllText(results[1].OutputPath));

        var completed = CompletedStagedOutput.Enumerate(profile.StagingFolder, profile.Id).ToArray();
        Assert.Equal(2, completed.Length);
        Assert.Contains(completed, job => job.SourcePath == firstSource && job.OutputPath == results[0].OutputPath && job.RemoteRelativeParent == "a");
        Assert.Contains(completed, job => job.SourcePath == secondSource && job.OutputPath == results[1].OutputPath && job.RemoteRelativeParent == "b");

        var first = Assert.Single(CompletedStagedOutput.Enumerate(profile.StagingFolder, profile.Id,
            sourcePath: firstSource, sourceFingerprint: results[0].CompletedOutput!.SourceFingerprint));
        Assert.Equal(results[0].OutputPath, first.OutputPath);
        Assert.Empty(CompletedStagedOutput.Enumerate(profile.StagingFolder, profile.Id,
            sourcePath: firstSource, sourceFingerprint: "wrong fingerprint"));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/rooted")]
    [InlineData("first//second")]
    [InlineData("first\\second")]
    public void CompletedOutputRejectsUnsafeRemoteParent(string remoteParent)
    {
        var profile = CreateProfile();
        var source = CreateSource("program.nc");
        var job = CompletedStagedOutput.CreateJobDirectory(profile.StagingFolder);
        var output = Path.Combine(job, "result.nc");
        File.WriteAllText(output, "G1 X1\n");

        Assert.Throws<InvalidDataException>(() => CompletedStagedOutput.Publish(job, profile.Id, source, output, null,
            remoteRelativeParent: remoteParent));
        Assert.Empty(CompletedStagedOutput.Enumerate(profile.StagingFolder, profile.Id));
    }

    [Fact]
    public async Task DiscoveryIgnoresLegacyUnmarkedAndModifiedStaging()
    {
        var profile = CreateProfile();
        var result = await ProcessApiAsync(CreateSource("program.nc"), profile, () => SuccessResponse("G1 X1\n"));
        Assert.True(result.Success, result.Message);
        var completed = Assert.IsType<CompletedStagedOutput>(result.CompletedOutput);
        Assert.NotNull(CompletedStagedOutput.TryRead(completed.JobDirectory, profile.Id));
        Assert.Empty(CompletedStagedOutput.Enumerate(profile.StagingFolder, "another-profile"));

        File.WriteAllText(Path.Combine(profile.StagingFolder, "legacy.nc"), "G1 X1\n");
        var unmarked = CompletedStagedOutput.CreateJobDirectory(profile.StagingFolder);
        File.WriteAllText(Path.Combine(unmarked, "interrupted.nc"), "G1 X1\n");
        File.WriteAllText(result.OutputPath, "G1 X2\n");

        Assert.Empty(CompletedStagedOutput.Enumerate(profile.StagingFolder, profile.Id));
    }

    [Fact]
    public async Task DefaultCopyPublishesFolderAndPersistsRemoteFolderName()
    {
        var profile = CreateProfile();
        CreateSource("job/nested/program.nc");
        var source = Path.Combine(profile.WatchFolder, "job");
        var result = await new StagingProjectProcessor().ProcessAsync(source, profile, ProcessingSetupSettings.CreateDefault("Default"));

        Assert.True(result.Success, result.Message);
        var completed = Assert.Single(CompletedStagedOutput.Enumerate(profile.StagingFolder, profile.Id));
        Assert.Equal("job", completed.RemoteFolderName);
        Assert.Equal(source, completed.SourcePath);
        Assert.Equal([Path.Combine("nested", "program.nc")], completed.ProcessedFiles);
        Assert.NotNull(completed.SourceFingerprint);
        completed.Delete();
        Assert.False(Directory.Exists(completed.JobDirectory));
        Assert.True(Directory.Exists(source));
    }

    [Fact]
    public async Task ExternalScriptAlternateOutputIsSnapshottedForCatchUp()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var profile = CreateProfile();
        var source = CreateSource("program.nc");
        var alternateFolder = Path.Combine(_root, "alternate");
        Directory.CreateDirectory(alternateFolder);
        var scriptPath = Path.Combine(_root, "processor.sh");
        File.WriteAllText(scriptPath, "cp \"$1\" \"$3/generated.nc\"\nprintf 'OUTPUT_PATH=%s\\n' \"$3\"\n");
        var result = await new StagingProjectProcessor().ProcessAsync(source, profile, new ProcessingSetupSettings
        {
            Mode = ProcessingMode.ExternalScript, RunnerMode = ScriptRunnerMode.Bash, ScriptPath = scriptPath,
            ArgumentsTemplate = $"\"{{sourcePath}}\" \"{{outputPath}}\" \"{alternateFolder}\""
        });

        Assert.True(result.Success, result.Message);
        var completed = Assert.Single(CompletedStagedOutput.Enumerate(profile.StagingFolder, profile.Id));
        var alternateFile = Path.Combine(alternateFolder, "generated.nc");
        File.WriteAllText(alternateFile, "changed after processing");
        Assert.Equal("G0 X0\n", File.ReadAllText(Path.Combine(completed.OutputPath, "generated.nc")));
        completed.Delete();
        Assert.True(File.Exists(alternateFile));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledLocalCopyPreservesExistingFile(bool uploadDirectory)
    {
        var source = CreateSource("input/program.nc");
        var target = Path.Combine(_root, "destination");
        Directory.CreateDirectory(target);
        var targetFile = Path.Combine(target, "program.nc");
        File.WriteAllText(targetFile, "old contents");
        var destination = new DestinationSettings { Type = DestinationType.LocalFolder, LocalRootPath = target };
        var service = new DestinationService(null!, null!, null!, null!, null!);
        using var cancellation = new CancellationTokenSource();

        var result = await service.UploadFileSystemItemAsync(
            uploadDirectory ? Path.GetDirectoryName(source)! : source, destination, "",
            new CallbackProgress(cancellation.Cancel), cancellation.Token);

        Assert.False(result.Success);
        Assert.Equal("old contents", File.ReadAllText(targetFile));
        Assert.Equal([targetFile], Directory.GetFileSystemEntries(target));
    }

    [Fact]
    public async Task SuccessfulLocalCopyReplacesExistingFileWithoutLeavingTemporaryFile()
    {
        var source = CreateSource("program.nc");
        var target = Path.Combine(_root, "destination");
        Directory.CreateDirectory(target);
        var targetFile = Path.Combine(target, "program.nc");
        File.WriteAllText(targetFile, "old contents");
        var destination = new DestinationSettings { Type = DestinationType.LocalFolder, LocalRootPath = target, ReplaceRemoteFolderOnUpload = true };
        var service = new DestinationService(null!, null!, null!, null!, null!);

        var result = await service.UploadFileSystemItemAsync(source, destination, "");

        Assert.True(result.Success, result.Message);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(targetFile));
        Assert.Equal([targetFile], Directory.GetFileSystemEntries(target));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GroupedLocalReplacementIsRejectedBeforeTouchingDestination(bool existingDestination)
    {
        var source = CreateSource("job/current.nc");
        var root = Path.Combine(_root, "destination");
        var group = Path.Combine(root, "job");
        var previous = Path.Combine(group, "previous.nc");
        if (existingDestination)
        {
            Directory.CreateDirectory(group);
            File.WriteAllText(previous, "previous program");
        }

        var destination = new DestinationSettings { Type = DestinationType.LocalFolder, LocalRootPath = root, ReplaceRemoteFolderOnUpload = true };
        var service = new DestinationService(null!, null!, null!, null!, null!);

        var result = await service.UploadDirectoryAsync(Path.GetDirectoryName(source)!, destination, "/job");

        Assert.False(result.Success);
        Assert.Contains("Turn off 'Replace Remote Folder On Upload'", result.Message);
        if (existingDestination)
        {
            Assert.Equal([group], Directory.GetFileSystemEntries(root));
            Assert.Equal([previous], Directory.GetFileSystemEntries(group));
            Assert.Equal("previous program", File.ReadAllText(previous));
        }
        else
        {
            Assert.False(Directory.Exists(root));
        }
    }

    [Fact]
    public async Task CatchUpRetainsGroupedOutputWhenReplacementIsRejected()
    {
        var source = Path.GetDirectoryName(CreateSource("job/current.nc"))!;
        var profile = CreateProfile();
        var root = Path.Combine(_root, "destination");
        var group = Path.Combine(root, "job");
        Directory.CreateDirectory(group);
        File.WriteAllText(Path.Combine(group, "obsolete.nc"), "old program to remove");
        File.WriteAllText(Path.Combine(root, "neighbor.nc"), "unrelated job");
        var destination = new DestinationSettings { Type = DestinationType.LocalFolder, LocalRootPath = root, ReplaceRemoteFolderOnUpload = true };
        var setup = ProcessingSetupSettings.CreateDefault("Default");
        await using var coordinator = new SyncCoordinator(new FileSystemFolderMonitor(), new StagingProjectProcessor(),
            new DestinationService(null!, null!, null!, null!, null!), new AppSettingsValidator());
        var deliveryResults = new List<ProcessingResult>();
        coordinator.ProcessingCompleted += deliveryResults.Add;

        var staged = await coordinator.ProcessPathAsync(source, profile, null, setup);
        Assert.True(staged.Success, staged.Message);
        var delivery = await coordinator.CatchUpMissingItemsAsync(profile, destination, setup);

        Assert.False(delivery.Success, delivery.Message);
        Assert.Contains("Turn off 'Replace Remote Folder On Upload'", deliveryResults[^1].Message);
        Assert.Equal("old program to remove", File.ReadAllText(Path.Combine(group, "obsolete.nc")));
        Assert.False(File.Exists(Path.Combine(group, "current.nc")));
        Assert.Equal("unrelated job", File.ReadAllText(Path.Combine(root, "neighbor.nc")));
        Assert.Single(CompletedStagedOutput.Enumerate(profile.StagingFolder, profile.Id));

        var mergeDestination = new DestinationSettings { Type = DestinationType.LocalFolder, LocalRootPath = root };
        var retry = await coordinator.CatchUpMissingItemsAsync(profile, mergeDestination, setup);
        Assert.True(retry.Success, retry.Message);
        Assert.True(File.Exists(Path.Combine(group, "obsolete.nc")));
        Assert.Equal("G0 X0\n", File.ReadAllText(Path.Combine(group, "current.nc")));
        Assert.Empty(CompletedStagedOutput.Enumerate(profile.StagingFolder, profile.Id));
    }

    private WatchProfileSettings CreateProfile() => new()
    {
        Id = "profile",
        WatchFolder = Path.Combine(_root, "input"),
        StagingFolder = Path.Combine(_root, "staging")
    };

    private string CreateSource(string relativePath)
    {
        var path = Path.Combine(_root, "input", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "G0 X0\n");
        return path;
    }

    private static async Task<ProcessingResult> ProcessApiAsync(
        string source, WatchProfileSettings profile, Func<HttpResponseMessage> response)
    {
        using var client = new HttpClient(new ResponseHandler(response));
        return await new StagingProjectProcessor(client).ProcessAsync(source, profile,
            new ProcessingSetupSettings { Mode = ProcessingMode.ProCutApi, ProCutLineJoinerEnabled = true },
            new ProCutApiSettings { BaseUrl = "https://api.example.test", ApiKey = TestSecrets.ProCutApiKey });
    }

    private static HttpResponseMessage SuccessResponse(string body)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "result.nc" };
        return response;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class ResponseHandler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response());
    }

    private sealed class CallbackProgress(Action callback) : IProgress<string>
    {
        public void Report(string value) => callback();
    }

    private sealed class ShortContent : HttpContent
    {
        private readonly bool _throwAfterWrite;

        public ShortContent(bool throwAfterWrite)
        {
            _throwAfterWrite = throwAfterWrite;
            Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            Headers.ContentLength = 100;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 100;
            return true;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await stream.WriteAsync("G0 X1\n"u8.ToArray());
            if (_throwAfterWrite)
            {
                throw new IOException("Response stream interrupted");
            }
        }
    }
}
