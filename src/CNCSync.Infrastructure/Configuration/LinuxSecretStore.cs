using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;
using CNCSync.Core.Configuration;

namespace CNCSync.Infrastructure.Configuration;

[SupportedOSPlatform("linux")]
public sealed class LinuxSecretStore : ISecretStore
{
    private readonly string _secretsDirectory;

    public LinuxSecretStore(string settingsDirectory)
    {
        _secretsDirectory = Path.Combine(settingsDirectory, "Secrets");
    }

    public string? GetSecret(string key)
    {
        var path = GetSecretPath(key);
        return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
    }

    public void SetSecret(string key, string secret)
    {
        Directory.CreateDirectory(_secretsDirectory);
        TrySetDirectoryPermissions(_secretsDirectory);

        var path = GetSecretPath(key);
        File.WriteAllText(path, secret, Encoding.UTF8);
        TrySetFilePermissions(path);
    }

    public void DeleteSecret(string key)
    {
        var path = GetSecretPath(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string GetSecretPath(string key)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        return Path.Combine(_secretsDirectory, $"{hash}.secret");
    }

    private static void TrySetDirectoryPermissions(string path)
    {
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch
        {
        }
    }

    private static void TrySetFilePermissions(string path)
    {
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
        }
    }
}
