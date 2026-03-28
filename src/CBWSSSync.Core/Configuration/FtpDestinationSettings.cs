namespace CBWSSSync.Core.Configuration;

public sealed class FtpDestinationSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 21;
    public string RemoteBasePath { get; set; } = string.Empty;
    public bool UseAnonymousFtp { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool AutoUpload { get; set; } = true;

    public static FtpDestinationSettings CreateDefault(string name) =>
        new()
        {
            Name = name,
            Host = string.Empty,
            Port = 21,
            RemoteBasePath = string.Empty,
            UseAnonymousFtp = true,
            Username = string.Empty,
            Password = string.Empty,
            AutoUpload = true
        };
}
