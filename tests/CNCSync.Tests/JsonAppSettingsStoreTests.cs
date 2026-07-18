using System.Text.Json;
using CNCSync.Core.Configuration;
using CNCSync.Infrastructure.Configuration;

namespace CNCSync.Tests;

public sealed class JsonAppSettingsStoreTests
{
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
            Assert.Equal(TestSecrets.ProCutApiSecret, secretStore.GetSecret("procutsuite:api-key"));

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
            Assert.Equal(TestSecrets.LegacyProCutApiSecret, secretStore.GetSecret("procutsuite:api-key"));
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
