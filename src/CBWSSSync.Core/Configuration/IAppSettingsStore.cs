namespace CBWSSSync.Core.Configuration;

public interface IAppSettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
    string SettingsFilePath { get; }
    string ScriptsDirectoryPath { get; }
}
