using CNCSync.Core.Configuration;
using CNCSync.Core.Processing;
using CNCSync.Infrastructure.Processing;

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
}
