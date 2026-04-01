namespace CNCSync.Core.Configuration;

public interface ISecretStore
{
    string? GetSecret(string key);
    void SetSecret(string key, string secret);
    void DeleteSecret(string key);
}
