using CBWSSSync.Core.Configuration;

namespace CBWSSSync.Core.Services;

public interface IFolderMonitor : IAsyncDisposable
{
    event Action<WorkItemReadyEvent>? WorkItemReady;
    bool IsRunning { get; }
    Task StartAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
