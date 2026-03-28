using System.Collections.Concurrent;
using CBWSSSync.Core.Configuration;
using CBWSSSync.Core.Processing;

namespace CBWSSSync.Core.Services;

public sealed class SyncCoordinator : ISyncCoordinator
{
    private readonly IFolderMonitor _folderMonitor;
    private readonly IProjectProcessor _projectProcessor;
    private readonly IFtpService _ftpService;
    private readonly AppSettingsValidator _validator;
    private readonly ConcurrentDictionary<string, byte> _inFlightPaths = new(StringComparer.OrdinalIgnoreCase);
    private AppSettings? _currentSettings;

    public SyncCoordinator(
        IFolderMonitor folderMonitor,
        IProjectProcessor projectProcessor,
        IFtpService ftpService,
        AppSettingsValidator validator)
    {
        _folderMonitor = folderMonitor;
        _projectProcessor = projectProcessor;
        _ftpService = ftpService;
        _validator = validator;
        _folderMonitor.WorkItemReady += OnWorkItemReady;
    }

    public event Action<ActivityLogEntry>? ActivityLogged;
    public event Action<string>? StatusChanged;
    public event Action<ProcessingResult>? ProcessingCompleted;

    public bool IsRunning => _folderMonitor.IsRunning;

    public async Task StartAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var validation = _validator.Validate(settings);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Errors));
        }

        _currentSettings = settings;
        await _folderMonitor.StartAsync(settings, cancellationToken);
        LogActivity($"Monitoring started for {settings.WatchProfiles.Count(profile => profile.Enabled)} profile(s).");
        SetStatus("Running");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _folderMonitor.StopAsync(cancellationToken);
        LogActivity("Monitoring stopped.");
        SetStatus("Stopped");
    }

    public async Task<ProcessingResult> ProcessPathAsync(
        string path,
        WatchProfileSettings profile,
        FtpDestinationSettings? destination,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteProcessAsync(
            path,
            profile,
            destination,
            destination?.AutoUpload == true,
            cancellationToken);
    }

    public async Task<(bool Success, string Message)> CatchUpMissingItemsAsync(
        WatchProfileSettings profile,
        FtpDestinationSettings destination,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profile.WatchFolder))
        {
            return (false, $"Catch-up skipped for {profile.Name} because no watch folder is configured.");
        }

        if (!Directory.Exists(profile.WatchFolder))
        {
            return (false, $"Catch-up skipped for {profile.Name} because the watch folder does not exist: {profile.WatchFolder}");
        }

        SetStatus($"Checking FTP for {profile.Name}");
        LogActivity("Manual catch-up started.", profile.Name);

        try
        {
            var remoteDirectoryPath = BuildRemoteDirectoryPath(destination, profile);
            var remoteEntriesResult = await _ftpService.ListRootEntriesAsync(destination, remoteDirectoryPath, cancellationToken);
            if (!remoteEntriesResult.Success)
            {
                LogActivity(remoteEntriesResult.Message, profile.Name);
                SetStatus(IsRunning ? "Running" : "Stopped");
                return (false, remoteEntriesResult.Message);
            }

            var remoteEntries = remoteEntriesResult.Entries;
            var localItems = Directory
                .EnumerateFileSystemEntries(profile.WatchFolder, "*", SearchOption.TopDirectoryOnly)
                .Where(path => !ShouldIgnoreFileSystemItem(Path.GetFileName(path)))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var missingItems = new List<string>();
            foreach (var localItem in localItems)
            {
                if (!await RemoteContainsItemAsync(remoteEntries, localItem, destination, remoteDirectoryPath, cancellationToken))
                {
                    missingItems.Add(localItem);
                }
            }

            if (missingItems.Count == 0)
            {
                var upToDateMessage = $"Manual catch-up found nothing missing on the FTP server for {profile.Name}.";
                LogActivity(upToDateMessage, profile.Name);
                SetStatus(IsRunning ? "Running" : "Stopped");
                return (true, upToDateMessage);
            }

            LogActivity(
                $"Manual catch-up found {missingItems.Count} missing item(s) out of {localItems.Count} local item(s).",
                profile.Name);

            var uploadedCount = 0;
            foreach (var missingItem in missingItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await ExecuteProcessAsync(missingItem, profile, destination, shouldUpload: true, cancellationToken);
                if (result.Success)
                {
                    uploadedCount++;
                }
            }

            var summaryMessage =
                $"Manual catch-up finished for {profile.Name}: uploaded {uploadedCount} missing item(s), skipped {localItems.Count - missingItems.Count} already present item(s).";
            LogActivity(summaryMessage, profile.Name);
            SetStatus(IsRunning ? "Running" : "Stopped");
            return (uploadedCount == missingItems.Count, summaryMessage);
        }
        catch (Exception ex)
        {
            var errorMessage = $"Manual catch-up failed for {profile.Name}: {ex.Message}";
            LogActivity(errorMessage, profile.Name);
            SetStatus("Error");
            return (false, errorMessage);
        }
    }

    public Task<(bool Success, string Message)> TestFtpAsync(FtpDestinationSettings destination, CancellationToken cancellationToken = default)
    {
        return _ftpService.TestConnectionAsync(destination, cancellationToken);
    }

    private async Task<ProcessingResult> ExecuteProcessAsync(
        string path,
        WatchProfileSettings profile,
        FtpDestinationSettings? destination,
        bool shouldUpload,
        CancellationToken cancellationToken)
    {
        var inFlightKey = $"{profile.Id}:{path}";
        if (!_inFlightPaths.TryAdd(inFlightKey, 0))
        {
            return new ProcessingResult
            {
                Success = false,
                Message = "That path is already being processed for this profile.",
                SourcePath = path,
                StartedAtUtc = DateTime.UtcNow,
                FinishedAtUtc = DateTime.UtcNow
            };
        }

        try
        {
            SetStatus($"Processing {profile.Name}");
            LogActivity($"Processing started: {path}", profile.Name);

            var result = await _projectProcessor.ProcessAsync(path, profile, cancellationToken);
            LogActivity(result.Message, profile.Name);

            if (result.Success && shouldUpload && destination is not null)
            {
                var remoteDirectoryPath = BuildRemoteDirectoryPath(destination, profile);
                var uploadResult = await _ftpService.UploadDirectoryAsync(result.OutputPath, destination, remoteDirectoryPath, cancellationToken);
                LogActivity(uploadResult.Message, profile.Name);
            }

            ProcessingCompleted?.Invoke(result);
            SetStatus(IsRunning ? "Running" : "Stopped");
            return result;
        }
        finally
        {
            _inFlightPaths.TryRemove(inFlightKey, out _);
        }
    }

    private async Task<bool> RemoteContainsItemAsync(
        IReadOnlyList<RemoteEntryInfo> remoteEntries,
        string localPath,
        FtpDestinationSettings destination,
        string remoteDirectoryPath,
        CancellationToken cancellationToken)
    {
        var itemName = Path.GetFileName(localPath);
        if (File.Exists(localPath))
        {
            var remoteFilePath = CombineRemoteDirectoryPath(remoteDirectoryPath, itemName);
            var remoteSizeResult = await _ftpService.TryGetFileSizeAsync(destination, remoteFilePath, cancellationToken);
            var localSizeBytes = new FileInfo(localPath).Length;
            return remoteSizeResult.Exists && remoteSizeResult.SizeBytes == localSizeBytes;
        }

        foreach (var remoteEntry in remoteEntries)
        {
            if (ShouldIgnoreFileSystemItem(remoteEntry.Name))
            {
                continue;
            }

            if (!string.Equals(remoteEntry.Name, itemName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool ShouldIgnoreFileSystemItem(string? itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return true;
        }

        return itemName.StartsWith(".", StringComparison.Ordinal) ||
               itemName.StartsWith("._", StringComparison.Ordinal) ||
               string.Equals(itemName, "Thumbs.db", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(itemName, "desktop.ini", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildRemoteDirectoryPath(FtpDestinationSettings destination, WatchProfileSettings profile)
    {
        var segments = new[] { destination.RemoteBasePath, profile.RemoteSubfolder }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var combined = string.Join("/", segments);
        return string.IsNullOrWhiteSpace(combined) ? string.Empty : $"/{combined}";
    }

    private static string CombineRemoteDirectoryPath(string? basePath, string? itemName)
    {
        var segments = new[] { basePath, itemName }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var combined = string.Join("/", segments);
        return string.IsNullOrWhiteSpace(combined) ? string.Empty : $"/{combined}";
    }

    private void OnWorkItemReady(WorkItemReadyEvent workItem)
    {
        var settings = _currentSettings;
        if (settings is null)
        {
            return;
        }

        var destination = settings.FtpDestinations.FirstOrDefault(
            item => string.Equals(item.Id, workItem.Profile.FtpDestinationId, StringComparison.OrdinalIgnoreCase));

        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessPathAsync(workItem.Path, workItem.Profile, destination);
            }
            catch (Exception ex)
            {
                LogActivity($"Processing failed for {workItem.Path}: {ex.Message}", workItem.Profile.Name);
                SetStatus("Error");
            }
        });
    }

    private void LogActivity(string message, string? source = null) =>
        ActivityLogged?.Invoke(new ActivityLogEntry
        {
            TimestampLocal = DateTime.Now,
            Source = source ?? string.Empty,
            Message = message
        });

    private void SetStatus(string status) => StatusChanged?.Invoke(status);

    public async ValueTask DisposeAsync()
    {
        _folderMonitor.WorkItemReady -= OnWorkItemReady;
        await _folderMonitor.DisposeAsync();
    }
}
