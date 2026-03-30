namespace CNCSync.Core.Configuration;

public sealed class DestinationSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public DestinationType Type { get; set; } = DestinationType.Ftp;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 21;
    public string RemoteBasePath { get; set; } = string.Empty;
    public bool UseAnonymousFtp { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string LocalRootPath { get; set; } = string.Empty;
    public NetworkShareProtocol NetworkProtocol { get; set; } = NetworkShareProtocol.Smb;
    public string NetworkHost { get; set; } = string.Empty;
    public string NetworkShareName { get; set; } = string.Empty;
    public string NetworkDomain { get; set; } = string.Empty;
    public bool UseCurrentUserCredentials { get; set; } = true;
    public bool AutoUpload { get; set; } = true;
    public bool Enabled
    {
        get => AutoUpload;
        set => AutoUpload = value;
    }

    public static DestinationSettings CreateDefault(string name) =>
        new()
        {
            Name = name,
            Type = DestinationType.Ftp,
            Host = string.Empty,
            Port = 21,
            RemoteBasePath = string.Empty,
            UseAnonymousFtp = true,
            Username = string.Empty,
            Password = string.Empty,
            LocalRootPath = string.Empty,
            NetworkProtocol = NetworkShareProtocol.Smb,
            NetworkHost = string.Empty,
            NetworkShareName = string.Empty,
            NetworkDomain = string.Empty,
            UseCurrentUserCredentials = true,
            AutoUpload = true
        };
}
