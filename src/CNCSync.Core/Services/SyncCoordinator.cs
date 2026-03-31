using System.Collections.Concurrent;
using CNCSync.Core.Configuration;
using CNCSync.Core.Processing;

namespace CNCSync.Core.Services;

public sealed class SyncCoordinator : ISyncCoordinator
{
    private readonly IFolderMonitor _folderMonitor;
    private readonly IProjectProcessor _projectProcessor;
    private readonly IDestinationService _destinationService;
    private readonly AppSettingsValidator _validator;
    private readonly ConcurrentDictionary<string, byte> _inFlightPaths = new(StringComparer.OrdinalIgnoreCase);
    private AppSettings? _currentSettings;

    public SyncCoordinator(
        IFolderMonitor folderMonitor,
        IProjectProcessor projectProcessor,
        IDestinationService destinationService,
        AppSettingsValidator validator)
    {
        _folderMonitor = folderMonitor;
        _projectProcessor = projectProcessor;
        _destinationService = destinationService;
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
        DestinationSettings? destination,
        ProcessingSetupSettings processingSetup,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteProcessAsync(
            path,
            profile,
            destination,
            processingSetup,
            destination is not null,
            cancellationToken);
    }

    public async Task<(bool Success, string Message)> CatchUpMissingItemsAsync(
        WatchProfileSettings profile,
        DestinationSettings destination,
        ProcessingSetupSettings processingSetup,
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

        SetStatus($"Checking destination for {profile.Name}");
        LogActivity("Manual catch-up started.", profile.Name);

        try
        {
            var pendingItems = EnumeratePendingStagedItems(profile)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (pendingItems.Count == 0)
            {
                var upToDateMessage = $"Manual catch-up found no pending staged items for {profile.Name}.";
                LogActivity(upToDateMessage, profile.Name);
                SetStatus(IsRunning ? "Running" : "Stopped");
                return (true, upToDateMessage);
            }

            LogActivity(
                BuildPendingItemsMessage(pendingItems),
                profile.Name);

            var uploadedCount = 0;
            var failedCount = 0;
            foreach (var pendingItem in pendingItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await UploadPendingItemAsync(pendingItem, profile, destination, processingSetup, cancellationToken);
                
                if (result.Success)
                {
                    uploadedCount++;
                }
                else
                {
                    failedCount++;
                }
            }

            var summaryMessage =
                failedCount == 0
                    ? $"Manual catch-up finished for {profile.Name}: uploaded {uploadedCount} pending staged item(s)."
                    : $"Manual catch-up finished for {profile.Name}: uploaded {uploadedCount} pending staged item(s), failed {failedCount} item(s).";
            LogActivity(summaryMessage, profile.Name);
            SetStatus(IsRunning ? "Running" : "Stopped");
            return (failedCount == 0 && uploadedCount == pendingItems.Count, summaryMessage);
        }
        catch (Exception ex)
        {
            var errorMessage = $"Manual catch-up failed for {profile.Name}: {ex.Message}";
            LogActivity(errorMessage, profile.Name);
            SetStatus("Error");
            return (false, errorMessage);
        }
    }

    private async Task<ProcessingResult> UploadPendingItemAsync(
        string stagedPath,
        WatchProfileSettings profile,
        DestinationSettings destination,
        ProcessingSetupSettings processingSetup,
        CancellationToken cancellationToken)
    {
        var sourceDisplayName = Path.GetFileName(stagedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!Directory.Exists(stagedPath) && !File.Exists(stagedPath))
        {
            var missingOutputMessage =
                $"Pending staged output for {sourceDisplayName} is no longer on local disk: {stagedPath}";
            LogActivity(missingOutputMessage, profile.Name);
            return new ProcessingResult
            {
                Success = false,
                Message = missingOutputMessage,
                SourcePath = stagedPath,
                OutputPath = stagedPath,
                StartedAtUtc = DateTime.UtcNow,
                FinishedAtUtc = DateTime.UtcNow,
                Errors = [missingOutputMessage]
            };
        }

        var appendFolderName = ShouldAppendStagedFolderName(stagedPath, profile);
        var sourcePath = ResolveCurrentSourcePath(profile, stagedPath);
        LogActivity($"Manual catch-up retrying staged output for {sourceDisplayName}.", profile.Name);
        return await UploadPreparedOutputAsync(
            new ProcessingResult
            {
                Success = true,
                Message = $"Reused staged output from {stagedPath}",
                SourcePath = sourcePath,
                OutputPath = stagedPath,
                RemoteFolderName = appendFolderName ? sourceDisplayName : null,
                StartedAtUtc = DateTime.UtcNow,
                FinishedAtUtc = DateTime.UtcNow,
                ProcessedFiles = FileSystemItemFilter.EnumerateIncludedFiles(stagedPath)
                    .Select(path => Path.GetRelativePath(stagedPath, path))
                    .ToList()
            },
            profile,
            destination,
            processingSetup,
            cancellationToken);
    }

    public Task<(bool Success, string Message)> TestDestinationAsync(DestinationSettings destination, CancellationToken cancellationToken = default)
    {
        return _destinationService.TestConnectionAsync(destination, cancellationToken);
    }

    private async Task<ProcessingResult> ExecuteProcessAsync(
        string path,
        WatchProfileSettings profile,
        DestinationSettings? destination,
        ProcessingSetupSettings? processingSetup,
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
                RemoteFolderName = Directory.Exists(path)
                    ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                    : null,
                StartedAtUtc = DateTime.UtcNow,
                FinishedAtUtc = DateTime.UtcNow
            };
        }

        try
        {
            SetStatus($"Processing {profile.Name}");
            LogActivity($"Processing started: {path}", profile.Name);

            var effectiveProcessingSetup = processingSetup ?? new ProcessingSetupSettings
            {
                Name = "Default Processing",
                Mode = ProcessingMode.DefaultUpload
            };

            var result = await _projectProcessor.ProcessAsync(path, profile, effectiveProcessingSetup, cancellationToken);
            LogActivity(result.Message, profile.Name);

            if (result.Success && shouldUpload && destination is not null)
            {
                result = await UploadPreparedOutputAsync(result, profile, destination, effectiveProcessingSetup, cancellationToken);
                if (!result.Success)
                {
                    return result;
                }
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

    private async Task<ProcessingResult> UploadPreparedOutputAsync(
        ProcessingResult result,
        WatchProfileSettings profile,
        DestinationSettings destination,
        ProcessingSetupSettings processingSetup,
        CancellationToken cancellationToken)
    {
        var remoteDirectoryPath = BuildRemoteDirectoryPath(destination, profile);
        var effectiveRemotePath = ShouldAppendProcessedFolderName(result, profile)
            ? CombineRemoteDirectoryPath(remoteDirectoryPath, result.RemoteFolderName)
            : remoteDirectoryPath;

        if (processingSetup.ReplaceRemoteFolderOnUpload &&
            !string.IsNullOrWhiteSpace(effectiveRemotePath))
        {
            var deleteResult = await _destinationService.DeleteRemoteItemAsync(destination, effectiveRemotePath, isDirectory: true, cancellationToken);
            if (deleteResult.Success)
            {
                LogActivity($"Replaced previous remote folder contents at {effectiveRemotePath}.", profile.Name);
            }
            else
            {
                LogActivity($"Remote folder cleanup skipped at {effectiveRemotePath}: {deleteResult.Message}", profile.Name);
            }
        }

        var destinationLabel = destination.Type switch
        {
            DestinationType.LocalFolder => "Local upload",
            DestinationType.Sftp => "SFTP upload",
            DestinationType.Scp => "SCP upload",
            _ => "FTP upload"
        };
        LogActivity($"{destinationLabel} starting to {(string.IsNullOrWhiteSpace(effectiveRemotePath) ? "/" : effectiveRemotePath)} from {result.OutputPath}", profile.Name);
        var uploadResult = await _destinationService.UploadFileSystemItemAsync(result.OutputPath, destination, effectiveRemotePath, cancellationToken);
        LogActivity(uploadResult.Message, profile.Name);

        if (!uploadResult.Success)
        {
            var failedUploadResult = new ProcessingResult
            {
                Success = false,
                Message = uploadResult.Message,
                SourcePath = result.SourcePath,
                OutputPath = result.OutputPath,
                RemoteFolderName = result.RemoteFolderName,
                StartedAtUtc = result.StartedAtUtc,
                FinishedAtUtc = DateTime.UtcNow,
                ProcessedFiles = result.ProcessedFiles,
                Errors = result.Errors.Concat([uploadResult.Message]).ToList()
            };

            ProcessingCompleted?.Invoke(failedUploadResult);
            SetStatus("Error");
            return failedUploadResult;
        }

        TryCleanupStagedOutput(result.OutputPath, profile.StagingFolder, profile.Name);
        return result;
    }

    private static string BuildRemoteDirectoryPath(DestinationSettings destination, WatchProfileSettings profile)
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

    private static bool ShouldAppendProcessedFolderName(ProcessingResult result, WatchProfileSettings profile)
    {
        if (string.IsNullOrWhiteSpace(result.RemoteFolderName) || !Directory.Exists(result.SourcePath))
        {
            return false;
        }

        var sourcePath = Path.GetFullPath(result.SourcePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var watchFolder = Path.GetFullPath(profile.WatchFolder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return !string.Equals(sourcePath, watchFolder, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetStableStagedOutputPath(WatchProfileSettings profile, string sourcePath)
    {
        var sourceName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return Path.Combine(profile.StagingFolder, string.IsNullOrWhiteSpace(sourceName) ? "work-item" : sourceName);
    }

    private static string BuildPendingItemsMessage(IReadOnlyList<string> pendingItems)
    {
        var pendingNames = pendingItems
            .Select(path => Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        if (pendingNames.Count == 1)
        {
            return $"Manual catch-up found 1 pending staged item: {pendingNames[0]}.";
        }

        if (pendingNames.Count is > 1 and <= 3)
        {
            return $"Manual catch-up found {pendingNames.Count} pending staged item(s): {string.Join(", ", pendingNames)}.";
        }

        return $"Manual catch-up found {pendingItems.Count} pending staged item(s).";
    }

    private void TryCleanupStagedOutput(string outputPath, string stagingRoot, string profileName)
    {
        if (string.IsNullOrWhiteSpace(outputPath) ||
            string.IsNullOrWhiteSpace(stagingRoot) ||
            (!Directory.Exists(outputPath) && !File.Exists(outputPath)) ||
            !Directory.Exists(stagingRoot))
        {
            return;
        }

        try
        {
            var normalizedOutputPath = Path.GetFullPath(outputPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedStagingRoot = Path.GetFullPath(stagingRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var stagingRootPrefix = normalizedStagingRoot + Path.DirectorySeparatorChar;

            if (!normalizedOutputPath.StartsWith(stagingRootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (Directory.Exists(normalizedOutputPath))
            {
                Directory.Delete(normalizedOutputPath, recursive: true);
            }
            else if (File.Exists(normalizedOutputPath))
            {
                File.Delete(normalizedOutputPath);
            }

            LogActivity($"Cleaned staged output: {normalizedOutputPath}", profileName);
        }
        catch (Exception ex)
        {
            LogActivity($"Staged output cleanup skipped: {ex.Message}", profileName);
        }
    }

    private static IEnumerable<string> EnumeratePendingStagedItems(WatchProfileSettings profile)
    {
        if (string.IsNullOrWhiteSpace(profile.StagingFolder) || !Directory.Exists(profile.StagingFolder))
        {
            yield break;
        }

        foreach (var stagedPath in Directory.EnumerateFileSystemEntries(profile.StagingFolder, "*", SearchOption.TopDirectoryOnly)
                     .Where(path => !FileSystemItemFilter.ShouldIgnoreFileSystemItem(Path.GetFileName(path)))
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            yield return stagedPath;
        }
    }

    private static bool ShouldAppendStagedFolderName(string stagedPath, WatchProfileSettings profile)
    {
        var sourceName = Path.GetFileName(stagedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return false;
        }

        var candidate = Path.Combine(profile.WatchFolder ?? string.Empty, sourceName);
        return Directory.Exists(candidate);
    }

    private static string ResolveCurrentSourcePath(WatchProfileSettings profile, string stagedPath)
    {
        var sourceName = Path.GetFileName(stagedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(sourceName) || string.IsNullOrWhiteSpace(profile.WatchFolder))
        {
            return stagedPath;
        }

        return Path.Combine(profile.WatchFolder, sourceName);
    }

    private void OnWorkItemReady(WorkItemReadyEvent workItem)
    {
        var settings = _currentSettings;
        if (settings is null)
        {
            return;
        }

        var destination = settings.Destinations.FirstOrDefault(
            item => string.Equals(item.Id, workItem.Profile.DestinationId, StringComparison.OrdinalIgnoreCase));
        var processingSetup = settings.ProcessingSetups.FirstOrDefault(
            item => string.Equals(item.Id, workItem.Profile.ProcessingSetupId, StringComparison.OrdinalIgnoreCase));

        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessPathAsync(workItem.Path, workItem.Profile, destination, processingSetup ?? ProcessingSetupSettings.CreateDefault("Default Processing"));
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
