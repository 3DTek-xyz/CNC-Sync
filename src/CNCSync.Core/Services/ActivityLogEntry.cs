namespace CNCSync.Core.Services;

public sealed class ActivityLogEntry
{
    public required DateTime TimestampLocal { get; init; }
    public string Source { get; init; } = string.Empty;
    public required string Message { get; init; }
    public bool IsProgressUpdate { get; init; }
    public string ProgressKey { get; init; } = string.Empty;

    public string TimeDisplay => TimestampLocal.ToString("HH:mm:ss.fff");

    public string SourceDisplay => string.IsNullOrWhiteSpace(Source) ? "-" : Source;
}
