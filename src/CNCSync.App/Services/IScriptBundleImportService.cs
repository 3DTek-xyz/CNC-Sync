namespace CNCSync.App.Services;

public interface IScriptBundleImportService
{
    Task<ScriptBundleImportResult> ImportAsync(
        string sourceUrl,
        string scriptsDirectoryPath,
        CancellationToken cancellationToken = default);
}
