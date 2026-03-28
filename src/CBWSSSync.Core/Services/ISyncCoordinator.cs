using CBWSSSync.Core.Configuration;
using CBWSSSync.Core.Processing;

namespace CBWSSSync.Core.Services;

public interface ISyncCoordinator : IAsyncDisposable
{
    event Action<ActivityLogEntry>? ActivityLogged;
    event Action<string>? StatusChanged;
    event Action<ProcessingResult>? ProcessingCompleted;
    bool IsRunning { get; }
    Task StartAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task<ProcessingResult> ProcessPathAsync(string path, WatchProfileSettings profile, FtpDestinationSettings? destination, CancellationToken cancellationToken = default);
    Task<(bool Success, string Message)> TestFtpAsync(FtpDestinationSettings destination, CancellationToken cancellationToken = default);
    Task<(bool Success, string Message)> CatchUpMissingItemsAsync(WatchProfileSettings profile, FtpDestinationSettings destination, CancellationToken cancellationToken = default);
}
