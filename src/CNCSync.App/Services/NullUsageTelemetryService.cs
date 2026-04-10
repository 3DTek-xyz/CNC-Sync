using CNCSync.Core.Configuration;
using CNCSync.Core.Processing;

namespace CNCSync.App.Services;

public sealed class NullUsageTelemetryService : IUsageTelemetryService
{
    public string NoticeText => "Design preview only. Telemetry is not active here.";
    public void ApplySettings(AppSettings settings) { }
    public bool CaptureStartupState(AppSettings settings, bool launchedAtLogin) => false;
    public void CaptureMonitoringStarted(AppSettings settings) { }
    public void CaptureMonitoringStopped() { }
    public void CaptureProcessingCompleted(ProcessingResult result) { }
    public void CaptureManualCatchUpCompleted(bool success, string message) { }
    public void CaptureUpdateAvailable() { }
    public void CaptureUpdateApplyRequested() { }
    public bool SendSupportDiagnostics(AppSettings settings, string activityLogTail, string diagnosticsLogTail) => false;
}
