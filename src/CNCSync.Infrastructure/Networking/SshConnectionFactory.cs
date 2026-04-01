using CNCSync.Core.Configuration;
using Renci.SshNet;

namespace CNCSync.Infrastructure.Networking;

internal static class SshConnectionFactory
{
    public static ConnectionInfo CreateConnectionInfo(DestinationSettings destination)
    {
        var port = destination.Port > 0 ? destination.Port : 22;
        var authenticationMethod = destination.SshAuthenticationMode == SshAuthenticationMode.PrivateKey
            ? CreatePrivateKeyAuthenticationMethod(destination)
            : new PasswordAuthenticationMethod(destination.Username, destination.Password);

        return new ConnectionInfo(destination.Host, port, destination.Username, authenticationMethod);
    }

    private static AuthenticationMethod CreatePrivateKeyAuthenticationMethod(DestinationSettings destination)
    {
        var expandedKeyPath = ExpandHomeDirectory(destination.PrivateKeyPath);
        var privateKeyFile = string.IsNullOrWhiteSpace(destination.PrivateKeyPassphrase)
            ? new PrivateKeyFile(expandedKeyPath)
            : new PrivateKeyFile(expandedKeyPath, destination.PrivateKeyPassphrase);

        return new PrivateKeyAuthenticationMethod(destination.Username, privateKeyFile);
    }

    private static string ExpandHomeDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (!path.StartsWith("~/", StringComparison.Ordinal) &&
            !path.StartsWith("~\\", StringComparison.Ordinal))
        {
            return path;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, path[2..]);
    }
}
