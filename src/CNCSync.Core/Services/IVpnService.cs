namespace CNCSync.Core.Services;

public interface IVpnService
{
    Task<IReadOnlyList<VpnConnectionInfo>> ListConnectionsAsync(CancellationToken cancellationToken = default);
    Task<VpnConnectionEnsureResult> EnsureConnectedAsync(string connectionName, CancellationToken cancellationToken = default);
}
