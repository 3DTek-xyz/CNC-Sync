using System.Text.Json;
using CBWSSSync.Core.Configuration;
using CBWSSSync.Infrastructure.Networking;

var settingsPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "CNC Sync",
    "settings.json");

Console.WriteLine($"Settings file: {settingsPath}");
var settingsJson = await File.ReadAllTextAsync(settingsPath);
var settings = JsonSerializer.Deserialize<AppSettings>(settingsJson, new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
})?.Normalize();

if (settings is null)
{
    Console.WriteLine("Could not load settings.");
    return 1;
}

var destination = settings.FtpDestinations.First();
Console.WriteLine($"Destination: {destination.Name} {destination.Host}:{destination.Port} Base={destination.RemoteBasePath}");

var ftp = new FtpService();
var profile = settings.WatchProfiles.First();

foreach (var path in new[] { "/", destination.RemoteBasePath, "/uploads", "/uploads/watch1" })
{
    Console.WriteLine();
    Console.WriteLine($"Listing path: {path}");
    var result = await ftp.ListRootEntriesAsync(destination, path);
    Console.WriteLine($"Success: {result.Success}");
    Console.WriteLine($"Message: {result.Message}");
    Console.WriteLine($"Count: {result.Entries.Count}");
    foreach (var entry in result.Entries)
    {
        Console.WriteLine($"- {entry.Name} | Full={entry.FullPath} | Dir={entry.IsDirectory} | Size={entry.SizeBytes?.ToString() ?? "null"}");
    }
}

Console.WriteLine();
Console.WriteLine("Catch-up simulation:");
var localItems = Directory
    .EnumerateFileSystemEntries(profile.WatchFolder, "*", SearchOption.TopDirectoryOnly)
    .Where(path => !string.IsNullOrWhiteSpace(Path.GetFileName(path)) &&
                   !Path.GetFileName(path)!.StartsWith(".", StringComparison.Ordinal) &&
                   !Path.GetFileName(path)!.StartsWith("._", StringComparison.Ordinal) &&
                   !string.Equals(Path.GetFileName(path), "Thumbs.db", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(Path.GetFileName(path), "desktop.ini", StringComparison.OrdinalIgnoreCase))
    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
    .ToList();

Console.WriteLine($"Local items count: {localItems.Count}");
foreach (var localItem in localItems)
{
    var itemName = Path.GetFileName(localItem);
    var remoteFilePath = $"/uploads/watch1/{itemName}";
    var remoteSizeResult = await ftp.TryGetFileSizeAsync(destination, remoteFilePath, CancellationToken.None);
    var localSizeBytes = new FileInfo(localItem).Length;
    Console.WriteLine($"{itemName} => exists={remoteSizeResult.Exists} remoteSize={remoteSizeResult.SizeBytes?.ToString() ?? "null"} localSize={localSizeBytes} msg={remoteSizeResult.Message}");
}

return 0;
