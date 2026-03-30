using System.Text.Json;
using CNCSync.Core.Configuration;

namespace CNCSync.Infrastructure.Configuration;

public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsDirectory;
    private readonly string _legacySettingsDirectory;
    private readonly string _bundledScriptsDirectory;

    public JsonAppSettingsStore()
    {
        var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _settingsDirectory = Path.Combine(appDataRoot, "CNC Sync");
        _legacySettingsDirectory = Path.Combine(appDataRoot, "CNCSync");
        SettingsFilePath = Path.Combine(_settingsDirectory, "settings.json");
        ScriptsDirectoryPath = Path.Combine(_settingsDirectory, "Scripts");
        _bundledScriptsDirectory = Path.Combine(AppContext.BaseDirectory, "BundledScripts");
    }

    public JsonAppSettingsStore(string settingsDirectory, string legacySettingsDirectory, string bundledScriptsDirectory)
    {
        _settingsDirectory = settingsDirectory;
        _legacySettingsDirectory = legacySettingsDirectory;
        SettingsFilePath = Path.Combine(_settingsDirectory, "settings.json");
        ScriptsDirectoryPath = Path.Combine(_settingsDirectory, "Scripts");
        _bundledScriptsDirectory = bundledScriptsDirectory;
    }

    public string SettingsFilePath { get; }
    public string ScriptsDirectoryPath { get; }

    public AppSettings Load()
    {
        MigrateLegacyDataIfNeeded();
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
        MigrateLegacyDataIfNeeded();
        Directory.CreateDirectory(_settingsDirectory);
        Directory.CreateDirectory(ScriptsDirectoryPath);
        SeedBundledScripts();

        var tempPath = SettingsFilePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
        }

        if (File.Exists(SettingsFilePath))
        {
            File.Move(tempPath, SettingsFilePath, overwrite: true);
        }
        else
        {
            File.Move(tempPath, SettingsFilePath);
        }
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

            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }

    private void MigrateLegacyDataIfNeeded()
    {
        if (!Directory.Exists(_legacySettingsDirectory))
        {
            return;
        }

        Directory.CreateDirectory(_settingsDirectory);

        foreach (var sourcePath in Directory.EnumerateFiles(_legacySettingsDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(_legacySettingsDirectory, sourcePath);
            var destinationPath = Path.Combine(_settingsDirectory, relativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            if (!File.Exists(destinationPath))
            {
                File.Copy(sourcePath, destinationPath);
            }
        }
    }
}
