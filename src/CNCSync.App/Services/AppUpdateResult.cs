namespace CNCSync.App.Services;

public sealed record AppUpdateResult(
    bool Success,
    string Message,
    bool UpdateAvailable = false,
    bool ReadyToApply = false);
