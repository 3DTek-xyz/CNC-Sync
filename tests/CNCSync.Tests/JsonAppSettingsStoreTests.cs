using System.Text.Json;
using CNCSync.Core.Configuration;
using CNCSync.Infrastructure.Configuration;

namespace CNCSync.Tests;

public sealed class JsonAppSettingsStoreTests
{
    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("cncsync-store-").FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class InMemorySecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _secrets = new();

        public string? GetSecret(string key) => _secrets.TryGetValue(key, out var secret) ? secret : null;

        public void SetSecret(string key, string secret) => _secrets[key] = secret;

        public void DeleteSecret(string key) => _secrets.Remove(key);
    }

    [Fact]
    public async Task LoadAsync_UsesExistingUserSettingsWithoutReplacingThem()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cncsync-store-{Guid.NewGuid():N}");
        var settingsDirectory = Path.Combine(root, "current");
        var legacyDirectory = Path.Combine(root, "legacy");
        var bundledScriptsDirectory = Path.Combine(root, "bundled");
        Directory.CreateDirectory(settingsDirectory);

        try
        {
            var existingSettings = AppSettings.CreateDefault();
            existingSettings.Destinations[0].Name = "User Destination";
            existingSettings.WatchProfiles[0].StabilityDelaySeconds = 17;

            var settingsPath = Path.Combine(settingsDirectory, "settings.json");
            await File.WriteAllTextAsync(settingsPath, JsonSerializer.Serialize(existingSettings, new JsonSerializerOptions { WriteIndented = true }));

            var store = new JsonAppSettingsStore(settingsDirectory, legacyDirectory, bundledScriptsDirectory, new InMemorySecretStore());
            var loaded = await store.LoadAsync();

            Assert.Equal("User Destination", loaded.Destinations[0].Name);
            Assert.Equal(17, loaded.WatchProfiles[0].StabilityDelaySeconds);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SaveAndLoad_PreservesSshHostFingerprint()
    {
        using var root = new TemporaryDirectory();
        var store = new JsonAppSettingsStore(root.Path, Path.Combine(root.Path, "legacy"),
            Path.Combine(root.Path, "bundled"), new InMemorySecretStore());
        var settings = AppSettings.CreateDefault();
        settings.Destinations[0].Type = DestinationType.Sftp;
        settings.Destinations[0].RequireSshHostKeyFingerprint = true;
        settings.Destinations[0].SshHostKeyFingerprint = SshHostFingerprintTests.Fingerprint;

        await store.SaveAsync(settings);

        var loaded = await store.LoadAsync();
        Assert.True(loaded.Destinations[0].RequireSshHostKeyFingerprint);
        Assert.Equal(SshHostFingerprintTests.Fingerprint, loaded.Destinations[0].SshHostKeyFingerprint);
    }

    [Fact]
    public async Task LoadAsync_SeedsProCutApiImportTemplateBesideSettingsFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cncsync-store-{Guid.NewGuid():N}");
        var settingsDirectory = Path.Combine(root, "current");
        var legacyDirectory = Path.Combine(root, "legacy");
        var bundledScriptsDirectory = Path.Combine(root, "bundled");

        try
        {
            var store = new JsonAppSettingsStore(settingsDirectory, legacyDirectory, bundledScriptsDirectory, new InMemorySecretStore());
            await store.LoadAsync();

            var templatePath = Path.Combine(settingsDirectory, JsonAppSettingsStore.ProCutApiImportTemplateFileName);
            Assert.True(File.Exists(templatePath));

            var json = await File.ReadAllTextAsync(templatePath);
            var template = JsonSerializer.Deserialize<AppSettings>(json);
            Assert.NotNull(template);
            template.Normalize();

            Assert.Equal(ProcessingMode.ProCutApi, template.ProcessingSetups[0].Mode);
            Assert.Equal(DestinationType.LocalFolder, template.Destinations[0].Type);
            Assert.Equal(template.Destinations[0].Id, template.WatchProfiles[0].DestinationId);
            Assert.Equal(template.ProcessingSetups[0].Id, template.WatchProfiles[0].ProcessingSetupId);
            Assert.DoesNotContain(TestSecrets.ProCutApiSecret, json, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LoadAndSave_DoNotOverwriteAnEditedImportTemplate()
    {
        using var root = new TemporaryDirectory();
        var store = new JsonAppSettingsStore(root.Path, Path.Combine(root.Path, "legacy"),
            Path.Combine(root.Path, "bundled"), new InMemorySecretStore());
        var settings = await store.LoadAsync();
        var templatePath = Path.Combine(root.Path, JsonAppSettingsStore.ProCutApiImportTemplateFileName);
        const string editedTemplate = "{\"StartMinimized\": false}";

        await File.WriteAllTextAsync(templatePath, editedTemplate);
        await store.LoadAsync();
        Assert.Equal(editedTemplate, await File.ReadAllTextAsync(templatePath));

        await store.SaveAsync(settings);
        Assert.Equal(editedTemplate, await File.ReadAllTextAsync(templatePath));
    }

    [Fact]
    public async Task LoadAsync_DoesNotRestoreSavedKeyForModifiedSettingsOrigin()
    {
        using var root = new TemporaryDirectory();
        var store = new JsonAppSettingsStore(root.Path, Path.Combine(root.Path, "legacy"),
            Path.Combine(root.Path, "bundled"), new InMemorySecretStore());
        var settings = AppSettings.CreateDefault();
        settings.ProCutApi.BaseUrl = "https://api.example.test";
        settings.ProCutApi.ApiKey = TestSecrets.ProCutApiSecret;
        await store.SaveAsync(settings);

        settings.ProCutApi.BaseUrl = "https://api.example.test:444";
        settings.ProCutApi.ApiKey = string.Empty;
        await File.WriteAllTextAsync(store.SettingsFilePath, JsonSerializer.Serialize(settings));

        Assert.Empty((await store.LoadAsync()).ProCutApi.ApiKey);

        settings.ProCutApi.BaseUrl = "https://API.example.test:443/another/path";
        await File.WriteAllTextAsync(store.SettingsFilePath, JsonSerializer.Serialize(settings));
        Assert.Equal(TestSecrets.ProCutApiSecret, (await store.LoadAsync()).ProCutApi.ApiKey);
    }

    [Fact]
    public async Task LoadAsync_DoesNotLetLegacySettingsOverwriteExistingCurrentSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cncsync-store-{Guid.NewGuid():N}");
        var settingsDirectory = Path.Combine(root, "current");
        var legacyDirectory = Path.Combine(root, "legacy");
        var bundledScriptsDirectory = Path.Combine(root, "bundled");
        Directory.CreateDirectory(settingsDirectory);
        Directory.CreateDirectory(legacyDirectory);

        try
        {
            var currentSettings = AppSettings.CreateDefault();
            currentSettings.Destinations[0].Name = "Current";

            var legacySettings = AppSettings.CreateDefault();
            legacySettings.Destinations[0].Name = "Legacy";

            await File.WriteAllTextAsync(
                Path.Combine(settingsDirectory, "settings.json"),
                JsonSerializer.Serialize(currentSettings, new JsonSerializerOptions { WriteIndented = true }));
            await File.WriteAllTextAsync(
                Path.Combine(legacyDirectory, "settings.json"),
                JsonSerializer.Serialize(legacySettings, new JsonSerializerOptions { WriteIndented = true }));

            var store = new JsonAppSettingsStore(settingsDirectory, legacyDirectory, bundledScriptsDirectory, new InMemorySecretStore());
            var loaded = await store.LoadAsync();

            Assert.Equal("Current", loaded.Destinations[0].Name);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SaveAsync_WritesSettingsAtomicallyToSingleSettingsFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cncsync-store-{Guid.NewGuid():N}");
        var settingsDirectory = Path.Combine(root, "current");
        var legacyDirectory = Path.Combine(root, "legacy");
        var bundledScriptsDirectory = Path.Combine(root, "bundled");

        try
        {
            var secretStore = new InMemorySecretStore();
            var store = new JsonAppSettingsStore(settingsDirectory, legacyDirectory, bundledScriptsDirectory, secretStore);
            var settings = AppSettings.CreateDefault();
            settings.Destinations[0].Name = "Saved";
            settings.Destinations[0].Password = "example-secret";
            settings.Destinations[0].PrivateKeyPassphrase = "example-passphrase";

            await store.SaveAsync(settings);

            Assert.True(File.Exists(Path.Combine(settingsDirectory, "settings.json")));
            Assert.False(File.Exists(Path.Combine(settingsDirectory, "settings.json.tmp")));
            var savedJson = await File.ReadAllTextAsync(Path.Combine(settingsDirectory, "settings.json"));
            Assert.DoesNotContain("example-secret", savedJson, StringComparison.Ordinal);
            Assert.DoesNotContain("example-passphrase", savedJson, StringComparison.Ordinal);
            Assert.Null(secretStore.GetSecret(settings.Destinations[0].Id));
            Assert.Equal("example-secret", secretStore.GetSecret($"{settings.Destinations[0].Id}:password"));
            Assert.Equal("example-passphrase", secretStore.GetSecret($"{settings.Destinations[0].Id}:private-key-passphrase"));

            var reloaded = await store.LoadAsync();
            Assert.Equal("Saved", reloaded.Destinations[0].Name);
            Assert.Equal("example-secret", reloaded.Destinations[0].Password);
            Assert.Equal("example-passphrase", reloaded.Destinations[0].PrivateKeyPassphrase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SaveAsync_StoresProCutApiKeyInSecretStore()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cncsync-store-{Guid.NewGuid():N}");
        var settingsDirectory = Path.Combine(root, "current");
        var legacyDirectory = Path.Combine(root, "legacy");
        var bundledScriptsDirectory = Path.Combine(root, "bundled");

        try
        {
            var secretStore = new InMemorySecretStore();
            var store = new JsonAppSettingsStore(settingsDirectory, legacyDirectory, bundledScriptsDirectory, secretStore);
            var settings = AppSettings.CreateDefault();
            settings.ProCutApi.BaseUrl = "https://api.example.test";
            settings.ProCutApi.ApiKey = TestSecrets.ProCutApiSecret;

            await store.SaveAsync(settings);

            var savedJson = await File.ReadAllTextAsync(Path.Combine(settingsDirectory, "settings.json"));
            Assert.Contains("https://api.example.test", savedJson, StringComparison.Ordinal);
            Assert.DoesNotContain(TestSecrets.ProCutApiSecret, savedJson, StringComparison.Ordinal);
            Assert.Null(secretStore.GetSecret("procutsuite:api-key"));

            var reloaded = await store.LoadAsync();
            Assert.Equal("https://api.example.test", reloaded.ProCutApi.BaseUrl);
            Assert.Equal(TestSecrets.ProCutApiSecret, reloaded.ProCutApi.ApiKey);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LoadAsync_MigratesLegacyPlaintextProCutApiKeyIntoSecretStore()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cncsync-store-{Guid.NewGuid():N}");
        var settingsDirectory = Path.Combine(root, "current");
        var legacyDirectory = Path.Combine(root, "legacy");
        var bundledScriptsDirectory = Path.Combine(root, "bundled");
        Directory.CreateDirectory(settingsDirectory);

        try
        {
            var secretStore = new InMemorySecretStore();
            var settings = AppSettings.CreateDefault();
            settings.ProCutApi.ApiKey = TestSecrets.LegacyProCutApiSecret;

            await File.WriteAllTextAsync(
                Path.Combine(settingsDirectory, "settings.json"),
                JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));

            var store = new JsonAppSettingsStore(settingsDirectory, legacyDirectory, bundledScriptsDirectory, secretStore);
            var loaded = await store.LoadAsync();
            var rewrittenJson = await File.ReadAllTextAsync(Path.Combine(settingsDirectory, "settings.json"));

            Assert.Equal(TestSecrets.LegacyProCutApiSecret, loaded.ProCutApi.ApiKey);
            Assert.Equal(TestSecrets.LegacyProCutApiSecret, (await store.LoadAsync()).ProCutApi.ApiKey);
            Assert.DoesNotContain(TestSecrets.LegacyProCutApiSecret, rewrittenJson, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LoadAsync_BindsExistingSecretToTheCurrentSettingsOriginOnFirstLoad()
    {
        using var root = new TemporaryDirectory();
        var secrets = new InMemorySecretStore();
        secrets.SetSecret("procutsuite:api-key", TestSecrets.LegacyProCutApiSecret);
        var store = new JsonAppSettingsStore(root.Path, Path.Combine(root.Path, "legacy"),
            Path.Combine(root.Path, "bundled"), secrets);
        var settings = AppSettings.CreateDefault();
        settings.ProCutApi.BaseUrl = "https://api.example.test";
        await File.WriteAllTextAsync(store.SettingsFilePath, JsonSerializer.Serialize(settings));

        Assert.Equal(TestSecrets.LegacyProCutApiSecret, (await store.LoadAsync()).ProCutApi.ApiKey);
        Assert.Null(secrets.GetSecret("procutsuite:api-key"));

        settings.ProCutApi.BaseUrl = "https://other.example.test";
        await File.WriteAllTextAsync(store.SettingsFilePath, JsonSerializer.Serialize(settings));
        Assert.Empty((await store.LoadAsync()).ProCutApi.ApiKey);
    }

    [Fact]
    public async Task SaveAsync_DoesNotRebindAnUnchangedKeyToAnEditedOrigin()
    {
        using var root = new TemporaryDirectory();
        var store = new JsonAppSettingsStore(root.Path, Path.Combine(root.Path, "legacy"),
            Path.Combine(root.Path, "bundled"), new InMemorySecretStore());
        var settings = AppSettings.CreateDefault();
        settings.ProCutApi.BaseUrl = "https://api.example.test";
        settings.ProCutApi.ApiKey = TestSecrets.ProCutApiSecret;
        await store.SaveAsync(settings);

        settings.ProCutApi.BaseUrl = "https://other.example.test";
        await store.SaveAsync(settings);
        Assert.Empty((await store.LoadAsync()).ProCutApi.ApiKey);

        settings.ProCutApi.ApiKey = TestSecrets.LegacyProCutApiSecret;
        await store.SaveAsync(settings);
        Assert.Equal(TestSecrets.LegacyProCutApiSecret, (await store.LoadAsync()).ProCutApi.ApiKey);
    }

    [Fact]
    public async Task SaveAsync_AfterImportDoesNotTransferOrDeleteTheOriginalOriginsKey()
    {
        using var root = new TemporaryDirectory();
        var store = new JsonAppSettingsStore(root.Path, Path.Combine(root.Path, "legacy"),
            Path.Combine(root.Path, "bundled"), new InMemorySecretStore());
        var original = AppSettings.CreateDefault();
        original.ProCutApi.BaseUrl = "https://api.example.test";
        original.ProCutApi.ApiKey = TestSecrets.ProCutApiSecret;
        await store.SaveAsync(original);

        var imported = AppSettings.CreateDefault();
        imported.ProCutApi.BaseUrl = "https://other.example.test";
        await store.SaveAsync(AppSettings.PrepareImported(imported, original.ProCutApi.BaseUrl, original.ProCutApi.ApiKey));
        Assert.Empty((await store.LoadAsync()).ProCutApi.ApiKey);

        original.ProCutApi.ApiKey = string.Empty;
        await File.WriteAllTextAsync(store.SettingsFilePath, JsonSerializer.Serialize(original));
        Assert.Equal(TestSecrets.ProCutApiSecret, (await store.LoadAsync()).ProCutApi.ApiKey);
    }

    [Fact]
    public async Task LoadAsync_MigratesLegacyPlaintextPasswordsIntoSecretStore()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cncsync-store-{Guid.NewGuid():N}");
        var settingsDirectory = Path.Combine(root, "current");
        var legacyDirectory = Path.Combine(root, "legacy");
        var bundledScriptsDirectory = Path.Combine(root, "bundled");
        Directory.CreateDirectory(settingsDirectory);

        try
        {
            var secretStore = new InMemorySecretStore();
            var settings = AppSettings.CreateDefault();
            settings.Destinations[0].Password = "legacy-example-secret";
            settings.Destinations[0].PrivateKeyPassphrase = "legacy-example-passphrase";

            await File.WriteAllTextAsync(
                Path.Combine(settingsDirectory, "settings.json"),
                JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));

            var store = new JsonAppSettingsStore(settingsDirectory, legacyDirectory, bundledScriptsDirectory, secretStore);
            var loaded = await store.LoadAsync();

            Assert.Equal("legacy-example-secret", loaded.Destinations[0].Password);
            Assert.Equal("legacy-example-passphrase", loaded.Destinations[0].PrivateKeyPassphrase);
            Assert.Equal("legacy-example-secret", secretStore.GetSecret($"{loaded.Destinations[0].Id}:password"));
            Assert.Equal("legacy-example-passphrase", secretStore.GetSecret($"{loaded.Destinations[0].Id}:private-key-passphrase"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
