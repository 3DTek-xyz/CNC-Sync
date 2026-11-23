using CNCSync.Core.Configuration;
using CNCSync.Core.Processing;

namespace CNCSync.Core.Services;

public sealed partial class SyncCoordinator
{
    public async Task<(bool Success, string Message)> CatchUpMissingItemsAsync(
        WatchProfileSettings profile, DestinationSettings destination, ProcessingSetupSettings processingSetup,
        ProCutApiSettings? proCutApi = null, CancellationToken cancellationToken = default)
    {
        SetStatus($"Checking destination for {profile.Name}");
        LogActivity("Catch-up started.", profile.Name);
        try
        {
            var uploaded = 0;
            var failed = 0;
            var skipped = 0;
            var candidates = CompletedStagedOutput.Enumerate(profile.StagingFolder, profile.Id, cancellationToken)
                .OrderByDescending(item => item.CompletedAtUtc);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = WorkItemKey.Create(profile, candidate.SourcePath);
                using var job = await _jobLocks.AcquireAsync(key, cancellationToken);
                var completed = ReadCompleted(candidate, profile, cancellationToken);
                if (completed is null || !seen.Add(key))
                {
                    skipped++;
                    continue;
                }

                if (IsSuperseded(completed, profile))
                {
                    TryCleanupCompleted(completed, profile);
                    continue;
                }

                var result = await DeliverCompletedAsync(completed, profile, destination, cancellationToken);
                ProcessingCompleted?.Invoke(result);
                if (result.Success)
                {
                    uploaded++;
                }
                else
                {
                    failed++;
                }
            }

            var message = $"Catch-up for {profile.Name}: uploaded {uploaded} verified item(s), failed {failed}, skipped {skipped}.";
            LogActivity(message, profile.Name);
            return (failed == 0, message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var message = $"Catch-up failed for {profile.Name}: {ex.Message}";
            LogActivity(message, profile.Name);
            return (false, message);
        }
        finally
        {
            RestoreStatus();
        }
    }

    private async Task<ProcessingResult> DeliverCompletedAsync(
        CompletedStagedOutput candidate, WatchProfileSettings profile, DestinationSettings destination, CancellationToken token)
    {
        await _deliverySlot.WaitAsync(token);
        try
        {
            var completed = ReadCompleted(candidate, profile, token);
            if (completed is null)
            {
                const string message = "Staged output could not be verified against the input.";
                LogActivity(message, profile.Name);
                return Failure(ToProcessingResult(candidate), message);
            }

            var result = ToProcessingResult(completed);
            try
            {
                result = await UploadPreparedOutputAsync(result, profile, destination, completed.RemoteFolderName is not null,
                    token, completed.RemoteRelativeParent);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogActivity($"Delivery failed: {ex.Message}", profile.Name);
                result = Failure(result, ex.Message);
            }

            if (!result.Success)
            {
                token.ThrowIfCancellationRequested();
            }

            if (completed.SourceFingerprint is { } fingerprint)
            {
                var now = DateTime.UtcNow;
                new DurableWorkState(fingerprint, result.Success ? WorkOutcome.Succeeded : WorkOutcome.AwaitingDelivery,
                    DeliveredThroughUtc: result.Success ? (now < completed.CompletedAtUtc ? completed.CompletedAtUtc : now) : null,
                    StagedJobDirectory: result.Success ? null : completed.JobDirectory)
                    .Write(profile, completed.SourcePath);
            }

            if (result.Success)
            {
                TryCleanupCompleted(completed, profile);
            }

            return result;
        }
        finally
        {
            _deliverySlot.Release();
        }
    }

    private static CompletedStagedOutput? ReadCompleted(
        CompletedStagedOutput candidate, WatchProfileSettings profile, CancellationToken token)
    {
        if (Path.GetDirectoryName(candidate.JobDirectory) is not { } stagingRoot || !SamePath(stagingRoot, profile.StagingFolder))
        {
            return null;
        }

        var output = CompletedStagedOutput.TryRead(candidate.JobDirectory, profile.Id, token);
        if (output is null || !SamePath(candidate.SourcePath, output.SourcePath) ||
            candidate.SourceFingerprint != output.SourceFingerprint || !IsCurrentCompletedOutput(output, profile, token))
        {
            return null;
        }

        return output;
    }

    private static bool IsCurrentCompletedOutput(CompletedStagedOutput output, WatchProfileSettings profile, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(profile.WatchFolder))
        {
            return false;
        }

        var relative = Path.GetRelativePath(profile.WatchFolder, output.SourcePath);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return false;
        }

        if (output.SourceFingerprint is not { } fingerprint)
        {
            return false;
        }

        try
        {
            return WorkItemFingerprint.Capture(output.SourcePath, includeContent: true, token) == fingerprint;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Once the source is gone, a missing or corrupt newer payload must not expose an older version.
            if (File.Exists(output.SourcePath) || Directory.Exists(output.SourcePath))
            {
                return false;
            }

            var latest = DurableWorkState.Read(profile, output.SourcePath);
            return latest is null ||
                (latest.Fingerprint == fingerprint &&
                 (latest.StagedJobDirectory is null || SamePath(latest.StagedJobDirectory, output.JobDirectory)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsSuperseded(CompletedStagedOutput output, WatchProfileSettings profile) =>
        DurableWorkState.Read(profile, output.SourcePath) is { } state &&
        (output.CompletedAtUtc <= state.DeliveredThroughUtc ||
         (state.Outcome == WorkOutcome.Succeeded && state.Fingerprint == output.SourceFingerprint));

    private void TryCleanupCompleted(CompletedStagedOutput completed, WatchProfileSettings profile)
    {
        try
        {
            completed.Delete();
            LogActivity($"Cleaned staged output: {completed.JobDirectory}", profile.Name);
        }
        catch (Exception ex)
        {
            LogActivity($"Staged output cleanup failed: {ex.Message}", profile.Name);
        }
    }

    private async Task<ProcessingResult> UploadPreparedOutputAsync(
        ProcessingResult result, WatchProfileSettings profile, DestinationSettings destination, bool appendFolder, CancellationToken token,
        string? sourceParent = null)
    {
        var effectiveRemotePath = BuildRemotePath(destination.RemoteBasePath, profile.RemoteSubfolder, sourceParent,
            appendFolder ? result.RemoteFolderName : null);
        if (destination.ReplaceRemoteFolderOnUpload && destination.Type != DestinationType.LocalFolder &&
            appendFolder && !string.IsNullOrWhiteSpace(effectiveRemotePath))
        {
            var deletion = await _destinationService.DeleteRemoteItemAsync(destination, effectiveRemotePath, isDirectory: true, token);
            LogActivity(deletion.Success
                ? $"Removed previous remote folder contents at {effectiveRemotePath}."
                : $"Remote folder cleanup skipped at {effectiveRemotePath}: {deletion.Message}", profile.Name);
        }

        var label = destination.Type switch
        {
            DestinationType.LocalFolder => "Local",
            DestinationType.Sftp => "SFTP",
            DestinationType.Scp => "SCP",
            DestinationType.NetworkShare => "Network share",
            _ => "FTP"
        };
        LogActivity($"{label} upload starting to {(string.IsNullOrWhiteSpace(effectiveRemotePath) ? "/" : effectiveRemotePath)} from {result.OutputPath}", profile.Name);
        var progressSource = string.IsNullOrWhiteSpace(destination.Name) ? profile.Name : $"{profile.Name} -> {destination.Name}";
        var progressKey = $"{profile.Id}:{destination.Id}";
        var progress = new Progress<string>(message => LogActivity(message, progressSource, isProgressUpdate: true, progressKey: progressKey));
        var replaceLocalFolder = destination.Type == DestinationType.LocalFolder && destination.ReplaceRemoteFolderOnUpload &&
            appendFolder && Directory.Exists(result.OutputPath);
        var upload = replaceLocalFolder
            ? await _destinationService.UploadDirectoryAsync(result.OutputPath, destination, effectiveRemotePath, progress, token)
            : await _destinationService.UploadFileSystemItemAsync(result.OutputPath, destination, effectiveRemotePath, progress, token);
        LogActivity(upload.Message, profile.Name);
        return upload.Success ? result : Failure(result, upload.Message);
    }

    private static string BuildRemotePath(params string?[] paths)
    {
        var segments = paths.Where(path => !string.IsNullOrWhiteSpace(path))
            .SelectMany(path => path!.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var combined = string.Join("/", segments);
        return combined.Length == 0 ? string.Empty : "/" + combined;
    }

    private static ProcessingResult ToProcessingResult(CompletedStagedOutput output) => new()
    {
        Success = true, Message = $"Verified staged output from {output.OutputPath}", SourcePath = output.SourcePath,
        OutputPath = output.OutputPath, RemoteFolderName = output.RemoteFolderName, StartedAtUtc = output.CompletedAtUtc,
        FinishedAtUtc = output.CompletedAtUtc, ProcessedFiles = output.ProcessedFiles, CompletedOutput = output
    };

    private static ProcessingResult Failure(ProcessingResult result, string message) => new()
    {
        Success = false, Message = message, SourcePath = result.SourcePath, OutputPath = result.OutputPath,
        RemoteFolderName = result.RemoteFolderName, StartedAtUtc = result.StartedAtUtc, FinishedAtUtc = DateTime.UtcNow,
        ProcessedFiles = result.ProcessedFiles, ActivityMessages = result.ActivityMessages,
        Errors = result.Errors.Concat([message]).ToList(), CompletedOutput = result.CompletedOutput
    };
}
