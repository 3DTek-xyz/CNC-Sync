namespace CNCSync.App.Services;

public sealed class DesignAppUpdateService : IAppUpdateService
{
    public bool IsSupported => true;
    public bool CanApplyUpdate => false;

    public Task<AppUpdateResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new AppUpdateResult(true, "Design-time update check complete. No updates are available."));

    public Task<AppUpdateResult> DownloadAndApplyUpdateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new AppUpdateResult(false, "Design-time updater has nothing ready to apply."));
}
