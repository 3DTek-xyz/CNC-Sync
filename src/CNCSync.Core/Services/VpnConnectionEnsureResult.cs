namespace CNCSync.Core.Services;

public sealed class VpnConnectionEnsureResult
{
    public bool Success { get; init; }
    public bool ConnectedNow { get; init; }
    public bool ConnectionStateChanged { get; init; }
    public string Message { get; init; } = string.Empty;

    public static VpnConnectionEnsureResult NoRequirement() => new()
    {
        Success = true,
        ConnectedNow = true
    };
}
