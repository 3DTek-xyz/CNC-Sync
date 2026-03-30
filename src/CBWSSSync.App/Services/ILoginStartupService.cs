namespace CBWSSSync.App.Services;

public interface ILoginStartupService
{
    Task ApplyAsync(bool enabled, CancellationToken cancellationToken = default);
}
