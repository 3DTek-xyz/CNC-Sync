using CNCSync.Core.Configuration;
using CNCSync.Infrastructure.Networking;

if (args.Length < 4)
{
    Console.WriteLine("Usage: ScpProbe <host> <port> <username> <password> [remotePath]");
    return 1;
}

var destination = new DestinationSettings
{
    Name = "SCP Probe",
    Type = DestinationType.Scp,
    Host = args[0],
    Port = int.Parse(args[1]),
    Username = args[2],
    Password = args[3],
    RemoteBasePath = args.Length > 4 ? args[4] : string.Empty
};

var service = new DestinationService(new FtpService(), new SftpService(), new ScpService(), new NetworkShareService());

var testResult = await service.TestConnectionAsync(destination);
Console.WriteLine($"TestConnection: success={testResult.Success} message={testResult.Message}");
if (!testResult.Success)
{
    return 1;
}

var tempRoot = Path.Combine(Path.GetTempPath(), $"cncsync-scp-probe-{Guid.NewGuid():N}");
Directory.CreateDirectory(tempRoot);
var tempFilePath = Path.Combine(tempRoot, "probe.txt");
await File.WriteAllTextAsync(tempFilePath, "scp probe");

var uploadTarget = string.IsNullOrWhiteSpace(destination.RemoteBasePath) ? "/upload/scp-probe" : $"{destination.RemoteBasePath.TrimEnd('/')}/scp-probe";
var uploadResult = await service.UploadDirectoryAsync(tempRoot, destination, uploadTarget);
Console.WriteLine($"UploadDirectory: success={uploadResult.Success} message={uploadResult.Message}");

foreach (var path in new[] { "/", destination.RemoteBasePath, "/upload", uploadTarget }.Distinct(StringComparer.Ordinal))
{
    var result = await service.ListRootEntriesAsync(destination, path);
    Console.WriteLine();
    Console.WriteLine($"ListRootEntries('{path}') => success={result.Success} message={result.Message}");
    foreach (var entry in result.Entries)
    {
        Console.WriteLine($"- {entry.Name} | full={entry.FullPath} | dir={entry.IsDirectory} | size={entry.SizeBytes?.ToString() ?? "null"}");
    }
}

var sizeResult = await service.TryGetFileSizeAsync(destination, $"{uploadTarget}/probe.txt");
Console.WriteLine();
Console.WriteLine($"TryGetFileSize('{uploadTarget}/probe.txt') => exists={sizeResult.Exists} size={sizeResult.SizeBytes?.ToString() ?? "null"} message={sizeResult.Message}");

var deleteResult = await service.DeleteRemoteItemAsync(destination, uploadTarget, isDirectory: true);
Console.WriteLine($"DeleteRemoteItem('{uploadTarget}') => success={deleteResult.Success} message={deleteResult.Message}");

Directory.Delete(tempRoot, recursive: true);
return 0;
