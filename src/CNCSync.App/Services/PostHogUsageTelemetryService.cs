using CNCSync.Core.Configuration;
using CNCSync.Core.Processing;
using CNCSync.Infrastructure.Logging;
using PostHog;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CNCSync.App.Services;

public sealed class PostHogUsageTelemetryService : IUsageTelemetryService
{
    private const string ProjectApiKey = "REMOVED_POSTHOG_PROJECT_API_KEY";
    private const string HostUrl = "https://us.i.posthog.com";
    private const int MaxSettingsCharsPerEvent = 6000;
    private const int MaxLogCharsPerEvent = 6000;
    private const int MaxLogCharsTotal = 18000;

    private readonly string _appVersion;
    private readonly PostHogClient _client;
    private static readonly Regex IpAddressPattern = new(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.Compiled);
    private AppSettings _settings;

    public PostHogUsageTelemetryService(string appVersion, AppSettings initialSettings)
    {
        _appVersion = appVersion;
        _settings = initialSettings.Normalize();
        _client = new PostHogClient(new PostHogOptions
        {
            ProjectApiKey = ProjectApiKey,
            HostUrl = new Uri(HostUrl)
        });
    }

    public string NoticeText =>
        "This app collects anonymised usage telemetry for product improvements and support planning.";

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings.Normalize();
    }

    public bool CaptureStartupState(AppSettings settings, bool launchedAtLogin)
    {
        settings.Normalize();
        ApplySettings(settings);

        var nowUtc = DateTime.UtcNow;
        var stateChanged = false;

        if (!_settings.TelemetryInstallReportedAtUtc.HasValue)
        {
            Capture("install registered", new Dictionary<string, object?>
            {
                ["launched_at_login"] = launchedAtLogin,
                ["start_minimized"] = _settings.StartMinimized,
                ["enabled_watch_profiles"] = _settings.WatchProfiles.Count(profile => profile.Enabled)
            });
            settings.TelemetryInstallReportedAtUtc = nowUtc;
            stateChanged = true;
        }

        if (!string.Equals(_settings.TelemetryLastSeenVersion, _appVersion, StringComparison.Ordinal))
        {
            Capture("app version seen", new Dictionary<string, object?>
            {
                ["launched_at_login"] = launchedAtLogin,
                ["start_minimized"] = _settings.StartMinimized,
                ["previous_version"] = _settings.TelemetryLastSeenVersion,
                ["enabled_watch_profiles"] = _settings.WatchProfiles.Count(profile => profile.Enabled)
            });
            settings.TelemetryLastSeenVersion = _appVersion;
            settings.TelemetryLastSeenAtUtc = nowUtc;
            stateChanged = true;
        }

        var lastHeartbeatDate = _settings.TelemetryLastHeartbeatAtUtc?.Date;
        if (lastHeartbeatDate != nowUtc.Date)
        {
            Capture("daily active", new Dictionary<string, object?>
            {
                ["launched_at_login"] = launchedAtLogin,
                ["start_minimized"] = _settings.StartMinimized,
                ["enabled_watch_profiles"] = _settings.WatchProfiles.Count(profile => profile.Enabled)
            });
            settings.TelemetryLastHeartbeatAtUtc = nowUtc;
            stateChanged = true;
        }

        if (stateChanged)
        {
            ApplySettings(settings);
        }

        return stateChanged;
    }

    public void CaptureMonitoringStarted(AppSettings settings)
    {
        ApplySettings(settings);
        Capture("monitoring started", new Dictionary<string, object?>
        {
            ["enabled_watch_profiles"] = _settings.WatchProfiles.Count(profile => profile.Enabled),
            ["scheduled_catch_up_enabled"] = _settings.ScheduledCatchUpEnabled
        });
    }

    public void CaptureMonitoringStopped()
    {
        Capture("monitoring stopped");
    }

    public void CaptureProcessingCompleted(ProcessingResult result)
    {
        var properties = new Dictionary<string, object?>
        {
            ["processed_file_count"] = result.ProcessedFiles.Count,
            ["duration_seconds"] = Math.Max(0, (result.FinishedAtUtc - result.StartedAtUtc).TotalSeconds),
            ["error_count"] = result.Errors.Count
        };

        if (result.Success)
        {
            Capture("processing completed", properties);
            return;
        }

        properties["failure_kind"] = ClassifyFailure(result.Message);
        Capture(
            result.Message.Contains("upload", StringComparison.OrdinalIgnoreCase) ? "upload failed" : "processing failed",
            properties);
    }

    public void CaptureManualCatchUpCompleted(bool success, string message)
    {
        Capture("catch up completed", new Dictionary<string, object?>
        {
            ["success"] = success,
            ["failure_kind"] = success ? string.Empty : ClassifyFailure(message)
        });
    }

    public void CaptureUpdateAvailable()
    {
        Capture("update available");
    }

    public void CaptureUpdateApplyRequested()
    {
        Capture("update apply requested");
    }

    public bool SendSupportDiagnostics(AppSettings settings, string activityLogTail, string diagnosticsLogTail)
    {
        try
        {
            ApplySettings(settings);
            var supportRequestId = Guid.NewGuid().ToString("N");

            var sanitizedSettings = BuildSanitizedSettingsJson(settings);
            var sanitizedActivity = SanitizeLogText(activityLogTail, maxLength: MaxLogCharsTotal);
            var sanitizedDiagnostics = SanitizeLogText(diagnosticsLogTail, maxLength: MaxLogCharsTotal);

            Capture("support diagnostics submitted", new Dictionary<string, object?>
            {
                ["support_request_id"] = supportRequestId,
                ["settings_chunk_count"] = SplitIntoChunks(sanitizedSettings, MaxSettingsCharsPerEvent).Count,
                ["activity_line_count"] = CountLines(sanitizedActivity),
                ["diagnostics_line_count"] = CountLines(sanitizedDiagnostics)
            });

            var settingsChunks = SplitIntoChunks(sanitizedSettings, MaxSettingsCharsPerEvent);
            for (var index = 0; index < settingsChunks.Count; index++)
            {
                Capture("support diagnostics settings", new Dictionary<string, object?>
                {
                    ["support_request_id"] = supportRequestId,
                    ["chunk_index"] = index + 1,
                    ["chunk_count"] = settingsChunks.Count,
                    ["sanitized_settings_json_chunk"] = settingsChunks[index]
                });
            }

            SendChunkedLogPayload(
                supportRequestId,
                "support diagnostics activity log",
                "activity_log_tail_chunk",
                sanitizedActivity,
                MaxLogCharsPerEvent);

            SendChunkedLogPayload(
                supportRequestId,
                "support diagnostics diagnostics log",
                "diagnostics_log_tail_chunk",
                sanitizedDiagnostics,
                MaxLogCharsPerEvent);

            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLog.WriteException("PostHog support diagnostics submission failed.", ex);
            return false;
        }
    }

    private void Capture(string eventName, Dictionary<string, object?>? properties = null)
    {
        if (string.IsNullOrWhiteSpace(_settings.TelemetryInstallId))
        {
            return;
        }

        try
        {
            var payload = new Dictionary<string, object>
            {
                ["$process_person_profile"] = false,
                ["app_version"] = _appVersion,
                ["os_family"] = ResolveOsFamily(),
                ["is_packaged_build"] = ResolveIsPackagedBuild(),
                ["watch_profile_count"] = _settings.WatchProfiles.Count,
                ["enabled_watch_profile_count"] = _settings.WatchProfiles.Count(profile => profile.Enabled),
                ["destination_count"] = _settings.Destinations.Count,
                ["telemetry_schema_version"] = 1
            };

            if (properties is not null)
            {
                foreach (var pair in properties)
                {
                    payload[pair.Key] = pair.Value ?? string.Empty;
                }
            }

            _client.Capture(_settings.TelemetryInstallId, eventName, properties: payload);
        }
        catch (Exception ex)
        {
            DiagnosticLog.WriteException($"PostHog telemetry capture failed for event '{eventName}'.", ex);
        }
    }

    private static string ResolveOsFamily()
    {
        if (OperatingSystem.IsWindows())
        {
            return "Windows";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "macOS";
        }

        if (OperatingSystem.IsLinux())
        {
            return "Linux";
        }

        return "Unknown";
    }

    private static bool ResolveIsPackagedBuild()
    {
        var processPath = Environment.ProcessPath ?? string.Empty;
        if (OperatingSystem.IsMacOS())
        {
            return processPath.Contains(".app/Contents/MacOS/", StringComparison.OrdinalIgnoreCase);
        }

        return !string.IsNullOrWhiteSpace(processPath) &&
               !processPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
    }

    private static string ClassifyFailure(string message)
    {
        if (message.Contains("Exceeded storage allocation", StringComparison.OrdinalIgnoreCase))
        {
            return "ftp_storage_quota";
        }

        if (message.Contains("ClosingData", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Closing data connection", StringComparison.OrdinalIgnoreCase))
        {
            return "ftp_data_connection_closed";
        }

        if (message.Contains("No CYC files with revision markers", StringComparison.OrdinalIgnoreCase))
        {
            return "script_missing_revision_markers";
        }

        if (message.Contains("cannot access the file", StringComparison.OrdinalIgnoreCase))
        {
            return "staging_path_locked";
        }

        if (message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
        {
            return "timeout";
        }

        if (message.Contains("FTP", StringComparison.OrdinalIgnoreCase))
        {
            return "ftp_error";
        }

        return "other";
    }

    private static string BuildSanitizedSettingsJson(AppSettings settings)
    {
        var sanitized = JsonSerializer.Deserialize<AppSettings>(
            JsonSerializer.Serialize(settings),
            new JsonSerializerOptions()) ?? AppSettings.CreateDefault();

        sanitized.TelemetryInstallId = "[redacted]";

        foreach (var destination in sanitized.Destinations)
        {
            destination.Name = string.IsNullOrWhiteSpace(destination.Name) ? string.Empty : "[redacted]";
            destination.Host = RedactHost(destination.Host);
            destination.NetworkHost = RedactHost(destination.NetworkHost);
            destination.Username = string.Empty;
            destination.Password = string.Empty;
            destination.PrivateKeyPath = RedactPath(destination.PrivateKeyPath);
            destination.PrivateKeyPassphrase = string.Empty;
            destination.LocalRootPath = RedactPath(destination.LocalRootPath);
            destination.NetworkShareName = string.IsNullOrWhiteSpace(destination.NetworkShareName) ? string.Empty : "[redacted]";
            destination.NetworkDomain = string.IsNullOrWhiteSpace(destination.NetworkDomain) ? string.Empty : "[redacted]";
            destination.RequiredVpnConnectionName = string.IsNullOrWhiteSpace(destination.RequiredVpnConnectionName) ? string.Empty : "[redacted]";
            destination.RemoteBasePath = string.IsNullOrWhiteSpace(destination.RemoteBasePath) ? string.Empty : "[redacted]";
        }

        foreach (var profile in sanitized.WatchProfiles)
        {
            profile.Name = string.IsNullOrWhiteSpace(profile.Name) ? string.Empty : "[redacted]";
            profile.WatchFolder = RedactPath(profile.WatchFolder);
            profile.StagingFolder = RedactPath(profile.StagingFolder);
            profile.RemoteSubfolder = string.IsNullOrWhiteSpace(profile.RemoteSubfolder) ? string.Empty : "[redacted]";
        }

        foreach (var setup in sanitized.ProcessingSetups)
        {
            setup.Name = string.IsNullOrWhiteSpace(setup.Name) ? string.Empty : "[redacted]";
            setup.ScriptPath = RedactPath(setup.ScriptPath);
        }

        sanitized.CustomScriptSourceUrl = string.IsNullOrWhiteSpace(sanitized.CustomScriptSourceUrl) ? string.Empty : "[redacted]";

        return JsonSerializer.Serialize(sanitized, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string SanitizeLogText(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sanitizedLines = text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizeLogLine)
            .ToList();

        var result = string.Join(Environment.NewLine, sanitizedLines);
        if (result.Length <= maxLength)
        {
            return result;
        }

        return result[^maxLength..];
    }

    private static string SanitizeLogLine(string line)
    {
        var sanitized = IpAddressPattern.Replace(line, "[redacted-ip]");

        sanitized = Regex.Replace(sanitized, @"[A-Za-z]:\\[^ \r\n\t]+", "[redacted-path]");
        sanitized = Regex.Replace(sanitized, @"(/Users/|/home/|/private/|/var/|/tmp/)[^ \r\n\t]+", "[redacted-path]");

        return sanitized;
    }

    private static string RedactHost(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return IpAddressPattern.IsMatch(value) ? "[redacted-ip]" : "[redacted-host]";
    }

    private static string RedactPath(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : "[redacted-path]";
    }

    private static int CountLines(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        return value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private void SendChunkedLogPayload(
        string supportRequestId,
        string eventName,
        string propertyName,
        string payload,
        int chunkSize)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        var chunks = SplitIntoChunks(payload, chunkSize);
        for (var index = 0; index < chunks.Count; index++)
        {
            Capture(eventName, new Dictionary<string, object?>
            {
                ["support_request_id"] = supportRequestId,
                ["chunk_index"] = index + 1,
                ["chunk_count"] = chunks.Count,
                [propertyName] = chunks[index]
            });
        }
    }

    private static List<string> SplitIntoChunks(string value, int chunkSize)
    {
        var chunks = new List<string>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return chunks;
        }

        for (var index = 0; index < value.Length; index += chunkSize)
        {
            var length = Math.Min(chunkSize, value.Length - index);
            chunks.Add(value.Substring(index, length));
        }

        return chunks;
    }
}
