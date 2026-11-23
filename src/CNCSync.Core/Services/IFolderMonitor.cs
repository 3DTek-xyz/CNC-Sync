using CNCSync.Core.Configuration;

namespace CNCSync.Core.Services;

public interface IFolderMonitor : IAsyncDisposable
{
    event Action<WorkItemReadyEvent>? WorkItemReady;
    event Action<string, string>? MonitoringError { add { } remove { } }
    void RequestRecheck(WorkItemReadyEvent item) { }
    bool IsRunning { get; }
    Task StartAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
