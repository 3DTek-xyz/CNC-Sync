namespace CNCSync.Core.Services;

public sealed class VpnConnectionInfo
{
    public string Name { get; init; } = string.Empty;
    public string Identifier { get; init; } = string.Empty;
    public bool IsConnected { get; init; }
}
