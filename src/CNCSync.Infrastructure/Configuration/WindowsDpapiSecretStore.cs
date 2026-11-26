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
        byte[] protectedBytes;
        try
        {
            using var file = new FileStream(GetSecretPath(key), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            protectedBytes = new byte[checked((int)file.Length)];
            file.ReadExactly(protectedBytes);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        var plaintextBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        try
        {
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    public void SetSecret(string key, string secret)
    {
        Directory.CreateDirectory(_secretsDirectory);
        var plaintextBytes = Encoding.UTF8.GetBytes(secret);
        var temporaryPath = Path.Combine(_secretsDirectory, $"{Guid.NewGuid():N}.tmp");
        try
        {
            var protectedBytes = ProtectedData.Protect(plaintextBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            using (var file = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(protectedBytes);
                file.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, GetSecretPath(key), overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
            File.Delete(temporaryPath);
        }
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
