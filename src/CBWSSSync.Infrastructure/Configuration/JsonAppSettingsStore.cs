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

    public JsonAppSettingsStore()
    {
        var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _settingsDirectory = Path.Combine(appDataRoot, "CBWSSSync");
        SettingsFilePath = Path.Combine(_settingsDirectory, "settings.json");
    }

    public string SettingsFilePath { get; }

    public AppSettings Load()
    {
        Directory.CreateDirectory(_settingsDirectory);

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

        await using var stream = File.Create(SettingsFilePath);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
    }
}
