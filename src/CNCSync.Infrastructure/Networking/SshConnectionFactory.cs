using CNCSync.Core.Configuration;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace CNCSync.Infrastructure.Networking;

internal static class SshConnectionFactory
{
    internal static readonly TimeSpan IoTimeout = TimeSpan.FromSeconds(15);

    internal static Task<SftpClient> ConnectSftpAsync(DestinationSettings destination, CancellationToken cancellationToken) =>
        ConnectAsync(destination, connection => new SftpClient(connection) { OperationTimeout = IoTimeout }, cancellationToken);

    internal static Task<SshClient> ConnectSshAsync(DestinationSettings destination, CancellationToken cancellationToken) =>
        ConnectAsync(destination, connection => new SshClient(connection), cancellationToken);

    internal static async Task<(bool Success, string Message)> TestConnectionAsync<T>(
        DestinationSettings destination,
        string protocol,
        Func<DestinationSettings, CancellationToken, Task<T>> connectAsync,
        CancellationToken cancellationToken) where T : BaseClient
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(destination.Host))
        {
            return (false, $"{protocol} host is not configured.");
        }

        try
        {
            if (destination.RequireSshHostKeyFingerprint && string.IsNullOrWhiteSpace(destination.SshHostKeyFingerprint))
            {
                using var probe = new SshClient(new ConnectionInfo(
                    destination.Host, GetPort(destination), "probe", new NoneAuthenticationMethod("probe"))
                    { Timeout = IoTimeout });
                await ConnectWithHostCheckAsync(probe, null, cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException("SSH server fingerprint could not be determined.");
            }

            GetRequiredFingerprint(destination);
            var validation = ValidateCredentials(destination, protocol);
            if (!validation.Success)
            {
                return validation;
            }

            using var client = await connectAsync(destination, cancellationToken).ConfigureAwait(false);
            return (true, $"{protocol} connection successful: {destination.Host}:{GetPort(destination)}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return (false, $"{protocol} connection failed: {ex.Message}");
        }
    }

    internal static string? GetRequiredFingerprint(DestinationSettings destination)
    {
        if (!destination.RequireSshHostKeyFingerprint)
        {
            return null;
        }

        return SshHostFingerprint.Normalize(destination.SshHostKeyFingerprint) ??
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(destination.SshHostKeyFingerprint)
                ? "SSH server fingerprint is required. Use Test This Destination to see the server's fingerprint, then confirm it with your server administrator."
                : "SSH server fingerprint must be a valid SHA256 fingerprint.");
    }

    internal static (bool Success, string Message) ValidateCredentials(DestinationSettings destination, string protocol)
    {
        if (string.IsNullOrWhiteSpace(destination.Username))
        {
            return (false, $"{protocol} username is not configured.");
        }

        if (destination.SshAuthenticationMode == SshAuthenticationMode.Password &&
            string.IsNullOrWhiteSpace(destination.Password))
        {
            return (false, $"{protocol} password is not configured.");
        }

        if (destination.SshAuthenticationMode == SshAuthenticationMode.PrivateKey &&
            string.IsNullOrWhiteSpace(destination.PrivateKeyPath))
        {
            return (false, $"{protocol} private key path is not configured.");
        }

        return (true, string.Empty);
    }

    private static async Task<T> ConnectAsync<T>(
        DestinationSettings destination,
        Func<ConnectionInfo, T> createClient,
        CancellationToken cancellationToken) where T : BaseClient
    {
        var fingerprint = GetRequiredFingerprint(destination);
        var client = createClient(CreateConnectionInfo(destination));
        try
        {
            if (fingerprint is null)
            {
                await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await ConnectWithHostCheckAsync(client, fingerprint, cancellationToken).ConfigureAwait(false);
            }
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task ConnectWithHostCheckAsync(BaseClient client, string? expected, CancellationToken cancellationToken)
    {
        string? rejectedFingerprint = null;
        var verified = false;
        client.HostKeyReceived += (_, args) =>
        {
            var actual = "SHA256:" + args.FingerPrintSHA256;
            args.CanTrust = string.Equals(actual, expected, StringComparison.Ordinal);
            if (args.CanTrust)
            {
                verified = true;
            }
            else
            {
                rejectedFingerprint = actual;
            }
        };

        try
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SshException ex) when (rejectedFingerprint is not null)
        {
            var message = expected is null
                ? $"Server presented {rejectedFingerprint}. Confirm it with your server administrator, then enter it in SSH server fingerprint."
                : $"SSH server fingerprint mismatch. Expected {expected}, but the server presented {rejectedFingerprint}. Confirm any change with your server administrator.";
            throw new InvalidOperationException(message, ex);
        }

        if (!verified)
        {
            throw new InvalidOperationException("SSH server fingerprint could not be verified.");
        }
    }

    private static ConnectionInfo CreateConnectionInfo(DestinationSettings destination)
    {
        var authenticationMethod = destination.SshAuthenticationMode == SshAuthenticationMode.PrivateKey
            ? CreatePrivateKeyAuthenticationMethod(destination)
            : new PasswordAuthenticationMethod(destination.Username, destination.Password);

        return new ConnectionInfo(destination.Host, GetPort(destination), destination.Username, authenticationMethod)
            { Timeout = IoTimeout };
    }

    private static int GetPort(DestinationSettings destination) => destination.Port > 0 ? destination.Port : 22;

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
