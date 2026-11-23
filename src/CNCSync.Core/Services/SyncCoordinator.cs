using CNCSync.Core.Configuration;
using CNCSync.Core.Processing;

namespace CNCSync.Core.Services;

public sealed partial class SyncCoordinator : ISyncCoordinator
{
    private const int MaxAttempts = 4;
    private readonly IFolderMonitor _folderMonitor;
    private readonly IProjectProcessor _projectProcessor;
    private readonly IDestinationService _destinationService;
    private readonly AppSettingsValidator _validator;
    private readonly int _maxConcurrentJobs;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _lifecycle = new(1);
    private readonly SemaphoreSlim _processingSlots;
    private readonly SemaphoreSlim _deliverySlot = new(1);
    private readonly WorkItemLocks _jobLocks = new();
    private WatchWorkQueue? _queue;
    private AppSettings? _currentSettings;

    public SyncCoordinator(
        IFolderMonitor folderMonitor, IProjectProcessor projectProcessor, IDestinationService destinationService,
        AppSettingsValidator validator, int maxConcurrentJobs = 2, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrentJobs);
        _folderMonitor = folderMonitor;
        _projectProcessor = projectProcessor;
        _destinationService = destinationService;
        _validator = validator;
        _maxConcurrentJobs = maxConcurrentJobs;
        _processingSlots = new SemaphoreSlim(maxConcurrentJobs);
        _clock = timeProvider ?? TimeProvider.System;
        _folderMonitor.WorkItemReady += OnWorkItemReady;
        _folderMonitor.MonitoringError += LogMonitorError;
    }

    public event Action<ActivityLogEntry>? ActivityLogged;
    public event Action<string>? StatusChanged;
    public event Action<ProcessingResult>? ProcessingCompleted;
    public bool IsRunning => _folderMonitor.IsRunning;

    public async Task StartAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (_queue is not null)
            {
                return;
            }

            var validation = _validator.Validate(settings);
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Errors));
            }

            _currentSettings = settings;
            Volatile.Write(ref _queue, new WatchWorkQueue(_maxConcurrentJobs, _clock,
                (item, token) => RunWatchedAsync(item, settings, token)));
            try
            {
                await _folderMonitor.StartAsync(settings, cancellationToken);
            }
            catch
            {
                await StopMonitoringAsync();
                throw;
            }

            LogActivity($"Monitoring started for {settings.WatchProfiles.Count(profile => profile.Enabled)} profile(s).");
            RestoreStatus();
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (_queue is null)
            {
                return;
            }

            await StopMonitoringAsync();
            LogActivity("Monitoring stopped.");
            SetStatus("Stopped");
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task StopMonitoringAsync()
    {
        var queue = Interlocked.Exchange(ref _queue, null);
        await Task.WhenAll(queue?.StopAsync() ?? Task.CompletedTask, _folderMonitor.StopAsync());
    }

    public async Task<ProcessingResult> ProcessPathAsync(
        string path, WatchProfileSettings profile, DestinationSettings? destination, ProcessingSetupSettings processingSetup,
        ProCutApiSettings? proCutApi = null, CancellationToken cancellationToken = default)
    {
        await _processingSlots.WaitAsync(cancellationToken);
        try
        {
            using var job = await _jobLocks.AcquireAsync(WorkItemKey.Create(profile, path), cancellationToken);
            var result = await ProcessAsync(path, profile, processingSetup, proCutApi ?? _currentSettings?.ProCutApi,
                fingerprint: null, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Success && destination is not null)
            {
                if (result.CompletedOutput is { } completed)
                {
                    result = await DeliverCompletedAsync(completed, profile, destination, cancellationToken);
                }
                else if (!string.IsNullOrWhiteSpace(result.OutputPath))
                {
                    var append = result.RemoteFolderName is not null && Directory.Exists(result.SourcePath) &&
                        (string.IsNullOrWhiteSpace(profile.WatchFolder) || !SamePath(result.SourcePath, profile.WatchFolder));
                    result = await UploadPreparedOutputAsync(result, profile, destination, append, cancellationToken);
                }
            }
            else if (result.Success && result.CompletedOutput is { SourceFingerprint: { } fingerprint } staged)
            {
                new DurableWorkState(fingerprint, WorkOutcome.AwaitingDelivery, StagedJobDirectory: staged.JobDirectory)
                    .Write(profile, path);
            }

            ProcessingCompleted?.Invoke(result);
            return result;
        }
        finally
        {
            _processingSlots.Release();
            RestoreStatus();
        }
    }

    public Task<(bool Success, string Message)> TestDestinationAsync(DestinationSettings destination, CancellationToken cancellationToken = default) =>
        _destinationService.TestConnectionAsync(destination, cancellationToken);

    private async Task<DateTimeOffset?> RunWatchedAsync(WorkItemReadyEvent item, AppSettings settings, CancellationToken token)
    {
        await _processingSlots.WaitAsync(token);
        try
        {
            using var job = await _jobLocks.AcquireAsync(WorkItemKey.Create(item.Profile, item.Path), token);
            var fingerprint = CaptureStableFingerprint(item, token);
            if (fingerprint is null)
            {
                return null;
            }

            var state = DurableWorkState.Read(item.Profile, item.Path);
            var setup = settings.ProcessingSetups.FirstOrDefault(entry =>
                string.Equals(entry.Id, item.Profile.ProcessingSetupId, StringComparison.OrdinalIgnoreCase))
                ?? ProcessingSetupSettings.CreateDefault("Default Processing");
            var configuration = ProcessingConfigurationFingerprint.Capture(setup, settings.ProCutApi);
            if (state?.Fingerprint != fingerprint)
            {
                state = null;
            }
            else if (state.Outcome == WorkOutcome.AwaitingDelivery)
            {
                if (FindRecordedStaging(state, item, fingerprint, token) is not null)
                {
                    return null;
                }

                LogActivity($"Pending staged output is missing or invalid; rebuilding {item.Path}.", item.Profile.Name);
                state = null;
            }
            else if (state.Outcome is WorkOutcome.Failed or WorkOutcome.Retrying &&
                     state.ProcessingConfiguration != configuration)
            {
                LogActivity($"Processing settings changed; starting a fresh attempt for {item.Path}.", item.Profile.Name);
                state = null;
            }
            else if (state.Outcome != WorkOutcome.Retrying)
            {
                return null;
            }
            else if (state.RetryAt > _clock.GetUtcNow())
            {
                LogActivity($"Retry pending for {item.Path} until {state.RetryAt:O}.", item.Profile.Name);
                return state.RetryAt;
            }

            return await ProcessWatchedVersionAsync(item, settings, setup, fingerprint, configuration, state?.Attempts ?? 0, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogActivity($"Queue could not process {item.Path}: {ex.Message}", item.Profile.Name);
            return null;
        }
        finally
        {
            _processingSlots.Release();
            RestoreStatus();
        }
    }

    private async Task<DateTimeOffset?> ProcessWatchedVersionAsync(
        WorkItemReadyEvent item, AppSettings settings, ProcessingSetupSettings setup, string fingerprint,
        string configuration, int previousAttempts, CancellationToken token)
    {
        var profile = item.Profile;
        var destination = settings.Destinations.FirstOrDefault(entry =>
            string.Equals(entry.Id, profile.DestinationId, StringComparison.OrdinalIgnoreCase));
        var completed = previousAttempts == 0 ? null : CompletedStagedOutput.Enumerate(profile.StagingFolder, profile.Id, token,
                sourcePath: item.Path, sourceFingerprint: fingerprint)
            .Where(output => output.SourceFingerprint == fingerprint && SamePath(output.SourcePath, item.Path) && !IsSuperseded(output, profile))
            .MaxBy(output => output.CompletedAtUtc);

        var attempts = previousAttempts + 1;
        if (completed is null && attempts > MaxAttempts)
        {
            RecordProcessingFailure(item, fingerprint, configuration, previousAttempts,
                new ProcessingResult { IsRetryable = true, Message = "A previous attempt was interrupted." });
            return null;
        }

        var result = completed is null
            ? await AttemptProcessingAsync(item, setup, settings.ProCutApi, fingerprint, configuration, attempts, token)
            : ToProcessingResult(completed);
        token.ThrowIfCancellationRequested();
        if (CaptureStableFingerprint(item, token) != fingerprint)
        {
            LogActivity($"Input changed during processing; waiting for the new version: {item.Path}", profile.Name);
            _folderMonitor.RequestRecheck(item);
            return null;
        }

        if (!result.Success)
        {
            var retry = RecordProcessingFailure(item, fingerprint, configuration, attempts, result);
            ProcessingCompleted?.Invoke(result);
            return retry;
        }

        if (result.CompletedOutput is { } output && destination is not null)
        {
            result = output.SourceFingerprint == fingerprint && SamePath(output.SourcePath, item.Path)
                ? await DeliverCompletedAsync(output, profile, destination, token)
                : Failure(result, "Staged output does not match the queued input.");
        }
        else if (result.CompletedOutput is { } staged)
        {
            new DurableWorkState(fingerprint, WorkOutcome.AwaitingDelivery, StagedJobDirectory: staged.JobDirectory).Write(profile, item.Path);
        }
        else if (!string.IsNullOrWhiteSpace(result.OutputPath))
        {
            result = Failure(result, "Processing did not publish verified staged output.");
            RecordProcessingFailure(item, fingerprint, configuration, attempts, result);
        }
        else
        {
            new DurableWorkState(fingerprint, WorkOutcome.Succeeded).Write(profile, item.Path);
        }

        ProcessingCompleted?.Invoke(result);
        return null;
    }

    private static CompletedStagedOutput? FindRecordedStaging(
        DurableWorkState state, WorkItemReadyEvent item, string fingerprint, CancellationToken token)
    {
        if (state.StagedJobDirectory is not { } path ||
            Path.GetDirectoryName(path) is not { } stagingRoot || !SamePath(stagingRoot, item.Profile.StagingFolder))
        {
            return null;
        }

        var output = CompletedStagedOutput.TryRead(path, item.Profile.Id, token);
        return output is not null && output.SourceFingerprint == fingerprint && SamePath(output.SourcePath, item.Path)
            ? output
            : null;
    }

    private async Task<ProcessingResult> AttemptProcessingAsync(
        WorkItemReadyEvent item, ProcessingSetupSettings setup, ProCutApiSettings api, string fingerprint,
        string configuration, int attempts, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Reserve before the request so a crash cannot reset the retry budget.
        var retryAt = _clock.GetUtcNow().Add(GetBackoff(attempts));
        new DurableWorkState(fingerprint, WorkOutcome.Retrying, attempts, retryAt, ProcessingConfiguration: configuration)
            .Write(item.Profile, item.Path);
        try
        {
            return await ProcessAsync(item.Path, item.Profile, setup, api, fingerprint, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            new DurableWorkState(fingerprint, WorkOutcome.Retrying, attempts - 1, _clock.GetUtcNow(), ProcessingConfiguration: configuration)
                .Write(item.Profile, item.Path);
            throw;
        }
        catch (Exception ex)
        {
            LogActivity($"Processing failed for {item.Path}: {ex.Message}", item.Profile.Name);
            return new ProcessingResult { SourcePath = item.Path, Message = ex.Message };
        }
    }

    private string? CaptureStableFingerprint(WorkItemReadyEvent item, CancellationToken token)
    {
        try
        {
            var metadata = WorkItemFingerprint.Capture(item.Path, includeContent: false, token);
            if (item.StableMetadataFingerprint is null || metadata == item.StableMetadataFingerprint)
            {
                var fingerprint = WorkItemFingerprint.Capture(item.Path, includeContent: true, token);
                if (WorkItemFingerprint.Capture(item.Path, includeContent: false, token) == metadata)
                {
                    return fingerprint;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogActivity($"Waiting for readable input {item.Path}: {ex.Message}", item.Profile.Name);
        }

        _folderMonitor.RequestRecheck(item);
        return null;
    }

    private DateTimeOffset? RecordProcessingFailure(
        WorkItemReadyEvent item, string fingerprint, string configuration, int attempts, ProcessingResult result)
    {
        if (result.IsRetryable && attempts < MaxAttempts)
        {
            var delay = GetBackoff(attempts);
            var now = _clock.GetUtcNow();
            if (result.RetryAfter is { } retryAfter && retryAfter > delay)
            {
                delay = retryAfter;
            }

            var retryAt = now + (delay <= DateTimeOffset.MaxValue - now ? delay : DateTimeOffset.MaxValue - now);
            new DurableWorkState(fingerprint, WorkOutcome.Retrying, attempts, retryAt, ProcessingConfiguration: configuration)
                .Write(item.Profile, item.Path);
            LogActivity($"Retry pending for {item.Path}: attempt {attempts + 1}/{MaxAttempts} at {retryAt:O}.", item.Profile.Name);
            return retryAt;
        }

        new DurableWorkState(fingerprint, WorkOutcome.Failed, attempts, ProcessingConfiguration: configuration)
            .Write(item.Profile, item.Path);
        var reason = result.IsRetryable ? "Retry limit reached" : "Permanent processing failure";
        LogActivity($"{reason} for {item.Path}. Input retained for manual processing or a changed version.", item.Profile.Name);
        return null;
    }

    private static TimeSpan GetBackoff(int attempts) =>
        TimeSpan.FromSeconds(5 * (1 << (attempts - 1)));

    private async Task<ProcessingResult> ProcessAsync(
        string path, WatchProfileSettings profile, ProcessingSetupSettings setup, ProCutApiSettings? api,
        string? fingerprint, CancellationToken token)
    {
        SetStatus($"Processing {profile.Name}");
        LogActivity($"Processing started: {path}", profile.Name);
        var result = fingerprint is null
            ? await _projectProcessor.ProcessAsync(path, profile, setup, api, token)
            : await _projectProcessor.ProcessQueuedAsync(path, profile, setup, api, fingerprint, token);
        foreach (var message in result.ActivityMessages)
        {
            LogActivity(message, profile.Name);
        }

        LogActivity(result.Message, profile.Name);
        return result;
    }

    private static bool SamePath(string first, string second) =>
        string.Equals(WorkItemKey.NormalizePath(first), WorkItemKey.NormalizePath(second), StringComparison.Ordinal);

    private void OnWorkItemReady(WorkItemReadyEvent item) => Volatile.Read(ref _queue)?.Submit(item);
    private void LogMonitorError(string profileName, string message) => LogActivity(message, profileName);

    private void LogActivity(string message, string? source = null, bool isProgressUpdate = false, string? progressKey = null) =>
        ActivityLogged?.Invoke(new ActivityLogEntry
        {
            TimestampLocal = DateTime.Now, Source = source ?? string.Empty, Message = message,
            IsProgressUpdate = isProgressUpdate, ProgressKey = progressKey ?? string.Empty
        });

    private void SetStatus(string status) => StatusChanged?.Invoke(status);
    private void RestoreStatus() => SetStatus(IsRunning ? "Running" : "Stopped");

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _folderMonitor.WorkItemReady -= OnWorkItemReady;
        _folderMonitor.MonitoringError -= LogMonitorError;
        await _folderMonitor.DisposeAsync();
    }
}
