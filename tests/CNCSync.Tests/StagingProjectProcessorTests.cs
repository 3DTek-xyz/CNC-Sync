using CNCSync.Core.Configuration;
using CNCSync.Core.Processing;
using CNCSync.Infrastructure.Processing;
using System.Net;
using System.Net.Http.Headers;

namespace CNCSync.Tests;

public sealed class StagingProjectProcessorTests
{
    [Fact]
    public async Task ProcessAsync_SingleFileStagesAsSingleFile()
    {
        var watchFolder = Path.Combine(Path.GetTempPath(), $"cncsync-watch-{Guid.NewGuid():N}");
        var stagingFolder = Path.Combine(Path.GetTempPath(), $"cncsync-stage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(watchFolder);
        var sourceFile = Path.Combine(watchFolder, "program.nc");
        await File.WriteAllTextAsync(sourceFile, "G0 X0 Y0");

        try
        {
            var processor = new StagingProjectProcessor();
            var profile = new WatchProfileSettings
            {
                Name = "Watch 1",
                WatchFolder = watchFolder,
                StagingFolder = stagingFolder
            };

            var result = await processor.ProcessAsync(
                sourceFile,
                profile,
                ProcessingSetupSettings.CreateDefault("Default"));

            Assert.True(result.Success);
            Assert.True(File.Exists(result.OutputPath));
            Assert.False(Directory.Exists(result.OutputPath));
            Assert.Equal(Path.Combine(stagingFolder, "program.nc"), result.OutputPath);
            Assert.Equal(["program.nc"], result.ProcessedFiles);
        }
        finally
        {
            if (Directory.Exists(watchFolder))
            {
                Directory.Delete(watchFolder, recursive: true);
            }

            if (Directory.Exists(stagingFolder))
            {
                Directory.Delete(stagingFolder, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ProcessAsync_ProCutApiRequiresApiKey()
    {
        var watchFolder = Path.Combine(Path.GetTempPath(), $"cncsync-watch-{Guid.NewGuid():N}");
        var stagingFolder = Path.Combine(Path.GetTempPath(), $"cncsync-stage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(watchFolder);
        var sourceFile = Path.Combine(watchFolder, "program.nc");
        await File.WriteAllTextAsync(sourceFile, "G0 X0 Y0");

        try
        {
            var processor = new StagingProjectProcessor();
            var result = await processor.ProcessAsync(
                sourceFile,
                new WatchProfileSettings
                {
                    Name = "Watch 1",
                    WatchFolder = watchFolder,
                    StagingFolder = stagingFolder
                },
                new ProcessingSetupSettings
                {
                    Name = "Smooth",
                    Mode = ProcessingMode.ProCutApi
                },
                new ProCutApiSettings
                {
                    BaseUrl = "https://api.example.test",
                    ApiKey = string.Empty
                });

            Assert.False(result.Success);
            Assert.Contains("API key", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(watchFolder))
            {
                Directory.Delete(watchFolder, recursive: true);
            }

            if (Directory.Exists(stagingFolder))
            {
                Directory.Delete(stagingFolder, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ProcessAsync_ProCutApiStagesResponseFileAndSendsBearerToken()
    {
        var watchFolder = Path.Combine(Path.GetTempPath(), $"cncsync-watch-{Guid.NewGuid():N}");
        var stagingFolder = Path.Combine(Path.GetTempPath(), $"cncsync-stage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(watchFolder);
        var sourceFile = Path.Combine(watchFolder, "program.nc");
        await File.WriteAllTextAsync(sourceFile, "G0 X0 Y0");

        var handler = new CapturingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("G1 X10 Y20\n")
            };
            response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = "program_smoothed.nc"
            };
            return response;
        });

        try
        {
            var processor = new StagingProjectProcessor(new HttpClient(handler));
            var result = await processor.ProcessAsync(
                sourceFile,
                new WatchProfileSettings
                {
                    Name = "Watch 1",
                    WatchFolder = watchFolder,
                    StagingFolder = stagingFolder
                },
                new ProcessingSetupSettings
                {
                    Name = "Smooth",
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
            Assert.Equal(new Uri("https://api.example.test/api/external/gcode/process"), handler.Request?.RequestUri);
            Assert.Equal("Bearer", handler.Request?.Headers.Authorization?.Scheme);
            Assert.Equal(TestSecrets.ProCutApiKey, handler.Request?.Headers.Authorization?.Parameter);
            Assert.Contains("multipart/form-data", handler.Request?.Content?.Headers.ContentType?.MediaType);
            Assert.Contains("name=tools", handler.RequestContent);
            Assert.Contains("line_joiner", handler.RequestContent);
            Assert.Contains("corner_smooth", handler.RequestContent);
            Assert.DoesNotContain("arc_fitting", handler.RequestContent);
            Assert.DoesNotContain("arc_joiner", handler.RequestContent);
            Assert.Equal(Path.Combine(stagingFolder, "program_smoothed.nc"), result.OutputPath);
            Assert.Equal(["program_smoothed.nc"], result.ProcessedFiles);
            Assert.Contains(result.ActivityMessages, message => message.Contains("ProCut Suite API endpoint", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.ActivityMessages, message => message.Contains("line_joiner", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.ActivityMessages, message => message.Contains("corner_smooth", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.ActivityMessages, message => message.Contains("upload starting: program.nc", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.ActivityMessages, message => message.Contains("program.nc -> program_smoothed.nc", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("G1 X10 Y20\n", await File.ReadAllTextAsync(result.OutputPath));
        }
        finally
        {
            if (Directory.Exists(watchFolder))
            {
                Directory.Delete(watchFolder, recursive: true);
            }

            if (Directory.Exists(stagingFolder))
            {
                Directory.Delete(stagingFolder, recursive: true);
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
}
