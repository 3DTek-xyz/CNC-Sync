using CBWSSSync.Core.Configuration;

namespace CBWSSSync.Core.Services;

public sealed class WorkItemReadyEvent
{
    public required string Path { get; init; }
    public required WatchProfileSettings Profile { get; init; }
}
