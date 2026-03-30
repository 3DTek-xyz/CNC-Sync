using System.Text.Json;
using CNCSync.Core.Configuration;
using CNCSync.Infrastructure.Configuration;

namespace CNCSync.Tests;

public sealed class JsonAppSettingsStoreTests
{
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

            var store = new JsonAppSettingsStore(settingsDirectory, legacyDirectory, bundledScriptsDirectory);
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

            var store = new JsonAppSettingsStore(settingsDirectory, legacyDirectory, bundledScriptsDirectory);
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
            var store = new JsonAppSettingsStore(settingsDirectory, legacyDirectory, bundledScriptsDirectory);
            var settings = AppSettings.CreateDefault();
            settings.Destinations[0].Name = "Saved";

            await store.SaveAsync(settings);

            Assert.True(File.Exists(Path.Combine(settingsDirectory, "settings.json")));
            Assert.False(File.Exists(Path.Combine(settingsDirectory, "settings.json.tmp")));

            var reloaded = await store.LoadAsync();
            Assert.Equal("Saved", reloaded.Destinations[0].Name);
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
