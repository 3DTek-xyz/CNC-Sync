using System.Text.Json;
using CBWSSSync.Core.Configuration;

namespace CBWSSSync.Infrastructure.Configuration;

public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsDirectory;
    private readonly string _bundledScriptsDirectory;

    public JsonAppSettingsStore()
    {
        var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _settingsDirectory = Path.Combine(appDataRoot, "CBWSSSync");
        SettingsFilePath = Path.Combine(_settingsDirectory, "settings.json");
        ScriptsDirectoryPath = Path.Combine(_settingsDirectory, "Scripts");
        _bundledScriptsDirectory = Path.Combine(AppContext.BaseDirectory, "BundledScripts");
    }

    public string SettingsFilePath { get; }
    public string ScriptsDirectoryPath { get; }

    public AppSettings Load()
    {
        Directory.CreateDirectory(_settingsDirectory);
        Directory.CreateDirectory(ScriptsDirectoryPath);
        SeedBundledScripts();

        if (!File.Exists(SettingsFilePath))
        {
            return AppSettings.CreateDefault();
        }

        using var stream = File.OpenRead(SettingsFilePath);
        var settings = JsonSerializer.Deserialize<AppSettings>(stream, JsonOptions);
        return (settings ?? AppSettings.CreateDefault()).Normalize();
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(Load, cancellationToken);
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_settingsDirectory);
        Directory.CreateDirectory(ScriptsDirectoryPath);
        SeedBundledScripts();

        await using var stream = File.Create(SettingsFilePath);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
    }

    private void SeedBundledScripts()
    {
        if (!Directory.Exists(_bundledScriptsDirectory))
        {
            return;
        }

        foreach (var sourcePath in Directory.EnumerateFiles(_bundledScriptsDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(_bundledScriptsDirectory, sourcePath);
            var destinationPath = Path.Combine(ScriptsDirectoryPath, relativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            // Only seed missing files so user-modified examples are not overwritten.
            if (!File.Exists(destinationPath))
            {
                File.Copy(sourcePath, destinationPath);
            }
        }
    }
}
