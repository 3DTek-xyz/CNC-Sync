using CNCSync.Core.Services;

namespace CNCSync.App.ViewModels;

public sealed class RemoteBrowserItemViewModel
{
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public long? SizeBytes { get; init; }

    public string Kind => IsDirectory ? "Folder" : "File";

    public string SizeDisplay => IsDirectory
        ? "-"
        : SizeBytes is null
            ? string.Empty
            : $"{SizeBytes:N0} bytes";

    public static RemoteBrowserItemViewModel FromRemoteEntry(RemoteEntryInfo entry) =>
        new()
        {
            Name = entry.Name,
            FullPath = entry.FullPath,
            IsDirectory = entry.IsDirectory,
            SizeBytes = entry.SizeBytes
        };
}
