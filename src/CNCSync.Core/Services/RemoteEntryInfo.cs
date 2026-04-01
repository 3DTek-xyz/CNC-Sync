namespace CNCSync.Core.Services;

public sealed class RemoteEntryInfo
{
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public long? SizeBytes { get; init; }
}
