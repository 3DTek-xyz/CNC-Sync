using CNCSync.Core.Configuration;
using CNCSync.Infrastructure.Networking;

if (args.Length < 4)
{
    Console.WriteLine("Usage: SftpProbe <host> <port> <username> <password> [remotePath]");
    return 1;
}

var destination = new DestinationSettings
{
    Name = "SFTP Probe",
    Type = DestinationType.Sftp,
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

foreach (var path in new[] { "/", destination.RemoteBasePath, "/upload" }.Distinct(StringComparer.Ordinal))
{
    var result = await service.ListRootEntriesAsync(destination, path);
    Console.WriteLine();
    Console.WriteLine($"ListRootEntries('{path}') => success={result.Success} message={result.Message}");
    foreach (var entry in result.Entries)
    {
        Console.WriteLine($"- {entry.Name} | full={entry.FullPath} | dir={entry.IsDirectory} | size={entry.SizeBytes?.ToString() ?? "null"}");
    }
}

return 0;
