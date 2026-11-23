namespace CNCSync.Core.Processing;

public sealed class ProcessingResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public string? RemoteFolderName { get; init; }
    public CompletedStagedOutput? CompletedOutput { get; init; }
    public bool IsRetryable { get; init; }
    public TimeSpan? RetryAfter { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public DateTime FinishedAtUtc { get; init; }
    public IReadOnlyList<string> ProcessedFiles { get; init; } = [];
    public IReadOnlyList<string> ActivityMessages { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
}
