using System.Text.Json;
using CNCSync.Core.Configuration;

namespace CNCSync.Infrastructure.Configuration;

public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private static readonly string[] ObsoleteBundledScriptRelativePaths =
    [
        Path.Combine("shared", "cbwss_mozaik_example.py")
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsDirectory;
    private readonly string _legacySettingsDirectory;
    private readonly string _bundledScriptsDirectory;
    private readonly ISecretStore _secretStore;

    public JsonAppSettingsStore()
    {
        var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _settingsDirectory = Path.Combine(appDataRoot, "CNC Sync");
        _legacySettingsDirectory = Path.Combine(appDataRoot, "CNCSync");
        SettingsFilePath = Path.Combine(_settingsDirectory, "settings.json");
        ScriptsDirectoryPath = Path.Combine(_settingsDirectory, "Scripts");
        _bundledScriptsDirectory = Path.Combine(AppContext.BaseDirectory, "BundledScripts");
        _secretStore = CreateDefaultSecretStore();
    }

    public JsonAppSettingsStore(string settingsDirectory, string legacySettingsDirectory, string bundledScriptsDirectory, ISecretStore? secretStore = null)
    {
        _settingsDirectory = settingsDirectory;
        _legacySettingsDirectory = legacySettingsDirectory;
        SettingsFilePath = Path.Combine(_settingsDirectory, "settings.json");
        ScriptsDirectoryPath = Path.Combine(_settingsDirectory, "Scripts");
        _bundledScriptsDirectory = bundledScriptsDirectory;
        _secretStore = secretStore ?? CreateDefaultSecretStore();
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
            var defaultSettings = AppSettings.CreateDefault();
            RewriteSettingsFile(CreateSanitizedSettingsCopy(defaultSettings));
            return defaultSettings;
        }

        AppSettings? settings;
        using (var stream = File.OpenRead(SettingsFilePath))
        {
            settings = JsonSerializer.Deserialize<AppSettings>(stream, JsonOptions);
        }
        var normalized = (settings ?? AppSettings.CreateDefault()).Normalize();
        var migratedLegacyPasswords = RestoreDestinationPasswords(normalized);
        if (migratedLegacyPasswords)
        {
            RewriteSettingsFile(CreateSanitizedSettingsCopy(normalized));
        }

        return normalized;
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
        PersistDestinationPasswords(settings);

        var sanitizedSettings = CreateSanitizedSettingsCopy(settings);
        await RewriteSettingsFileAsync(sanitizedSettings, cancellationToken);
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

            if (!File.Exists(destinationPath))
            {
                File.Copy(sourcePath, destinationPath, overwrite: false);
            }
        }

        RemoveObsoleteBundledScripts();
    }

    private void RemoveObsoleteBundledScripts()
    {
        foreach (var relativePath in ObsoleteBundledScriptRelativePaths)
        {
            var path = Path.Combine(ScriptsDirectoryPath, relativePath);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) &&
                Directory.Exists(directory) &&
                !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
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

    private ISecretStore CreateDefaultSecretStore()
    {
        if (OperatingSystem.IsMacOS())
        {
            return new MacKeychainSecretStore();
        }

        if (OperatingSystem.IsWindows())
        {
            return new WindowsDpapiSecretStore(_settingsDirectory);
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxSecretStore(_settingsDirectory);
        }

        throw new PlatformNotSupportedException("Secure destination password storage is currently implemented for macOS Keychain, Windows DPAPI, and Linux per-user secret files.");
    }

    private bool RestoreDestinationPasswords(AppSettings settings)
    {
        var migratedLegacyPasswords = false;
        foreach (var destination in settings.Destinations)
        {
            var passwordSecretKey = BuildDestinationPasswordKey(destination);
            var legacyPassword = destination.Password;
            if (!string.IsNullOrWhiteSpace(legacyPassword))
            {
                _secretStore.SetSecret(passwordSecretKey, legacyPassword);
                destination.Password = legacyPassword;
                migratedLegacyPasswords = true;
            }
            else
            {
                var currentPassword = _secretStore.GetSecret(passwordSecretKey);
                var legacyStoredPassword = _secretStore.GetSecret(destination.Id);
                if (!string.IsNullOrWhiteSpace(legacyStoredPassword) && string.IsNullOrWhiteSpace(currentPassword))
                {
                    _secretStore.SetSecret(passwordSecretKey, legacyStoredPassword);
                    _secretStore.DeleteSecret(destination.Id);
                    currentPassword = legacyStoredPassword;
                    migratedLegacyPasswords = true;
                }

                destination.Password = currentPassword ?? string.Empty;
            }

            var passphraseSecretKey = BuildDestinationPrivateKeyPassphraseKey(destination);
            var legacyPassphrase = destination.PrivateKeyPassphrase;
            if (!string.IsNullOrWhiteSpace(legacyPassphrase))
            {
                _secretStore.SetSecret(passphraseSecretKey, legacyPassphrase);
                destination.PrivateKeyPassphrase = legacyPassphrase;
                migratedLegacyPasswords = true;
            }
            else
            {
                destination.PrivateKeyPassphrase = _secretStore.GetSecret(passphraseSecretKey) ?? string.Empty;
            }
        }

        return migratedLegacyPasswords;
    }

    private void PersistDestinationPasswords(AppSettings settings)
    {
        foreach (var destination in settings.Destinations)
        {
            var secretKey = BuildDestinationPasswordKey(destination);
            if (string.IsNullOrWhiteSpace(destination.Password))
            {
                _secretStore.DeleteSecret(secretKey);
                _secretStore.DeleteSecret(destination.Id);
            }
            else
            {
                _secretStore.SetSecret(secretKey, destination.Password);
            }

            var passphraseSecretKey = BuildDestinationPrivateKeyPassphraseKey(destination);
            if (string.IsNullOrWhiteSpace(destination.PrivateKeyPassphrase))
            {
                _secretStore.DeleteSecret(passphraseSecretKey);
            }
            else
            {
                _secretStore.SetSecret(passphraseSecretKey, destination.PrivateKeyPassphrase);
            }
        }
    }

    private static AppSettings CreateSanitizedSettingsCopy(AppSettings settings)
    {
        var copy = JsonSerializer.Deserialize<AppSettings>(
            JsonSerializer.Serialize(settings, JsonOptions),
            JsonOptions) ?? AppSettings.CreateDefault();

        foreach (var destination in copy.Destinations)
        {
            destination.Password = string.Empty;
            destination.PrivateKeyPassphrase = string.Empty;
        }

        return copy;
    }

    private static string BuildDestinationPasswordKey(DestinationSettings destination) => $"{destination.Id}:password";
    private static string BuildDestinationPrivateKeyPassphraseKey(DestinationSettings destination) => $"{destination.Id}:private-key-passphrase";

    private void RewriteSettingsFile(AppSettings settings)
    {
        Directory.CreateDirectory(_settingsDirectory);
        var tempPath = SettingsFilePath + ".tmp";
        using (var stream = File.Create(tempPath))
        {
            JsonSerializer.Serialize(stream, settings, JsonOptions);
        }

        MoveTempSettingsIntoPlace(tempPath);
    }

    private async Task RewriteSettingsFileAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_settingsDirectory);
        var tempPath = SettingsFilePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
        }

        MoveTempSettingsIntoPlace(tempPath);
    }

    private void MoveTempSettingsIntoPlace(string tempPath)
    {
        if (File.Exists(SettingsFilePath))
        {
            File.Move(tempPath, SettingsFilePath, overwrite: true);
        }
        else
        {
            File.Move(tempPath, SettingsFilePath);
        }
    }
}
