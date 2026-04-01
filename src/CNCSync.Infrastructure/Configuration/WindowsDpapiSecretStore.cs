using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;
using CNCSync.Core.Configuration;

namespace CNCSync.Infrastructure.Configuration;

[SupportedOSPlatform("windows")]
public sealed class WindowsDpapiSecretStore : ISecretStore
{
    private readonly string _secretsDirectory;

    public WindowsDpapiSecretStore(string settingsDirectory)
    {
        _secretsDirectory = Path.Combine(settingsDirectory, "Secrets");
    }

    public string? GetSecret(string key)
    {
        var path = GetSecretPath(key);
        if (!File.Exists(path))
        {
            return null;
        }

        var protectedBytes = File.ReadAllBytes(path);
        var plaintextBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plaintextBytes);
    }

    public void SetSecret(string key, string secret)
    {
        Directory.CreateDirectory(_secretsDirectory);
        var plaintextBytes = Encoding.UTF8.GetBytes(secret);
        var protectedBytes = ProtectedData.Protect(plaintextBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(GetSecretPath(key), protectedBytes);
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
        return Path.Combine(_secretsDirectory, $"{hash}.bin");
    }
}
