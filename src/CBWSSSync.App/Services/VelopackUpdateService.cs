using Velopack;
using Velopack.Sources;

namespace CBWSSSync.App.Services;

public sealed class VelopackUpdateService : IAppUpdateService
{
    private const string RepositoryUrl = "https://github.com/3DTek-xyz/CNC-FTPSync";
    private UpdateInfo? _pendingUpdate;

    public bool IsSupported => OperatingSystem.IsWindows();

    public bool CanApplyUpdate => _pendingUpdate is not null;

    public async Task<AppUpdateResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
        {
            return new AppUpdateResult(false, "Automatic updates are currently enabled for installed Windows builds only.");
        }

        var manager = CreateManager();
        if (!manager.IsInstalled)
        {
            return new AppUpdateResult(false, "Update checks are available in installed Windows builds, not dotnet-run or unpackaged publishes.");
        }

        if (manager.UpdatePendingRestart is not null)
        {
            _pendingUpdate = new UpdateInfo(manager.UpdatePendingRestart, false, manager.UpdatePendingRestart, []);
            return new AppUpdateResult(true, "An update is already downloaded and ready to apply.", true, true);
        }

        var update = await manager.CheckForUpdatesAsync();
        _pendingUpdate = update;

        if (update is null)
        {
            return new AppUpdateResult(true, "CNC Sync is up to date.");
        }

        var version = update.TargetFullRelease.Version.ToString();
        return new AppUpdateResult(true, $"Update {version} is available and ready to download.", true, false);
    }

    public async Task<AppUpdateResult> DownloadAndApplyUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
        {
            return new AppUpdateResult(false, "Automatic updates are currently enabled for installed Windows builds only.");
        }

        var manager = CreateManager();
        if (!manager.IsInstalled)
        {
            return new AppUpdateResult(false, "Update apply is available in installed Windows builds, not dotnet-run or unpackaged publishes.");
        }

        if (_pendingUpdate is null)
        {
            return new AppUpdateResult(false, "Check for updates first.");
        }

        await manager.DownloadUpdatesAsync(_pendingUpdate, progress: _ => { }, cancelToken: cancellationToken);
        var target = _pendingUpdate.TargetFullRelease;
        manager.ApplyUpdatesAndRestart(target);
        return new AppUpdateResult(true, $"Downloaded update {target.Version} and requested restart to apply it.", true, true);
    }

    private static UpdateManager CreateManager()
    {
        var source = new GithubSource(RepositoryUrl, string.Empty, prerelease: false);
        return new UpdateManager(source);
    }
}
