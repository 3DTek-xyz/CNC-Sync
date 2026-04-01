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
    public FtpDataMode FtpDataMode { get; set; } = FtpDataMode.AutoPassive;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public SshAuthenticationMode SshAuthenticationMode { get; set; } = SshAuthenticationMode.Password;
    public string PrivateKeyPath { get; set; } = string.Empty;
    public string PrivateKeyPassphrase { get; set; } = string.Empty;
    public string LocalRootPath { get; set; } = string.Empty;
    public string NetworkHost { get; set; } = string.Empty;
    public string NetworkShareName { get; set; } = string.Empty;
    public string NetworkDomain { get; set; } = string.Empty;
    public bool UseCurrentUserCredentials { get; set; } = true;
    public string RequiredVpnConnectionName { get; set; } = string.Empty;
    public bool DisconnectVpnWhenFinished { get; set; }
    public bool AutoUpload { get; set; } = true;
    public bool ReplaceRemoteFolderOnUpload { get; set; }
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
            FtpDataMode = FtpDataMode.AutoPassive,
            Username = string.Empty,
            Password = string.Empty,
            SshAuthenticationMode = SshAuthenticationMode.Password,
            PrivateKeyPath = string.Empty,
            PrivateKeyPassphrase = string.Empty,
            LocalRootPath = string.Empty,
            NetworkHost = string.Empty,
            NetworkShareName = string.Empty,
            NetworkDomain = string.Empty,
            UseCurrentUserCredentials = true,
            RequiredVpnConnectionName = string.Empty,
            DisconnectVpnWhenFinished = false,
            AutoUpload = true,
            ReplaceRemoteFolderOnUpload = false
        };
}
