using CNCSync.Core.Configuration;
using CNCSync.Core.Processing;

namespace CNCSync.Core.Services;

public interface IProjectProcessor
{
    Task<ProcessingResult> ProcessAsync(
        string sourcePath,
        WatchProfileSettings profile,
        ProcessingSetupSettings processingSetup,
        ProCutApiSettings? proCutApi = null,
        CancellationToken cancellationToken = default);
}
