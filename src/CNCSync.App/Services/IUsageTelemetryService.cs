using CNCSync.Core.Configuration;
using CNCSync.Core.Processing;

namespace CNCSync.App.Services;

public interface IUsageTelemetryService
{
    string NoticeText { get; }
    void ApplySettings(AppSettings settings);
    bool CaptureStartupState(AppSettings settings, bool launchedAtLogin);
    void CaptureMonitoringStarted(AppSettings settings);
    void CaptureMonitoringStopped();
    void CaptureProcessingCompleted(ProcessingResult result);
    void CaptureManualCatchUpCompleted(bool success, string message);
    void CaptureUpdateAvailable();
    void CaptureUpdateApplyRequested();
    bool SendSupportDiagnostics(AppSettings settings, string activityLogTail, string diagnosticsLogTail);
}
