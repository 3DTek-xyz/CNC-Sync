namespace CNCSync.Core.Services;

public interface IVpnService
{
    Task<IReadOnlyList<VpnConnectionInfo>> ListConnectionsAsync(CancellationToken cancellationToken = default);
    Task<VpnConnectionEnsureResult> EnsureConnectedAsync(string connectionName, CancellationToken cancellationToken = default);
    Task<(bool Success, string Message)> DisconnectAsync(string connectionName, CancellationToken cancellationToken = default);
}
