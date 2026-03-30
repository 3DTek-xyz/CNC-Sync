namespace CNCSync.App.Services;

public interface ILoginStartupService
{
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default);
    Task ApplyAsync(bool enabled, CancellationToken cancellationToken = default);
}
