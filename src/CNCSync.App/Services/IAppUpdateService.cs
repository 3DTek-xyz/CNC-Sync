namespace CNCSync.App.Services;

public interface IAppUpdateService
{
    bool IsSupported { get; }
    bool CanApplyUpdate { get; }
    Task<AppUpdateResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
    Task<AppUpdateResult> DownloadAndApplyUpdateAsync(CancellationToken cancellationToken = default);
}
