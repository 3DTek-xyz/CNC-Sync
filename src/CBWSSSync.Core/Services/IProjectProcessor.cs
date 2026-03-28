using CBWSSSync.Core.Configuration;
using CBWSSSync.Core.Processing;

namespace CBWSSSync.Core.Services;

public interface IProjectProcessor
{
    Task<ProcessingResult> ProcessAsync(string sourcePath, WatchProfileSettings profile, ProcessingSetupSettings processingSetup, CancellationToken cancellationToken = default);
}
