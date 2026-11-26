using System.Net;
using CNCSync.Core.Configuration;
using CNCSync.Infrastructure.Logging;

namespace CNCSync.Infrastructure.DesktopPrinting;

internal sealed class DesktopPrintSession(
    DesktopPrintStationSettings settings, DesktopPrintBackendAccess api, Guid stationId,
    DesktopPrintBackendClient backend, IPrintSpooler spooler, DesktopPrintCertificate certificate,
    IPAddress bindAddress, Func<CancellationToken, Task<IReadOnlyList<InstalledPrintQueue>>> installedPrinters,
    TimeSpan heartbeatInterval, Action<string> status, Action<string> activity) : IAsyncDisposable
{
    private static readonly StringComparer QueueComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly CancellationTokenSource _stopping = new();
    private readonly PrintRequestActivityLimit _redemptionFailures = new();
    private readonly Dictionary<string, DesktopPrintQueueMapping> _queues = settings.Printers.ToDictionary(
        printer => printer.Id, StringComparer.Ordinal);
    private IAsyncDisposable? _receiver;
    private Task? _heartbeat;

    public DateTime CertificateExpiresAtUtc => certificate.Leaf.NotAfter.ToUniversalTime();
    public string Origin => settings.Origin;

    public async Task StartAsync(DesktopPrintReceiverFactory startReceiver, CancellationToken token)
    {
        _receiver = await startReceiver(bindAddress, new Uri(settings.Origin).Port,
            certificate.Leaf, certificate.Chain, settings.BrowserOrigins, settings.Origin, QueueTicketAsync, token).ConfigureAwait(false);
        activity($"HTTPS listener started at {settings.Origin}.");
        status("HTTPS listener running. Registering with ProCut Suite.");
        _heartbeat = Task.Run(HeartbeatAsync);
    }

    private async Task<bool> QueueTicketAsync(string ticket, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);
        DesktopPrintDocument document;
        try
        {
            document = await backend.RedeemAsync(api, stationId, ticket, linked.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            var reason = exception switch
            {
                DesktopPrintBackendStatusException response => $"ProCut Suite rejected the ticket (HTTP {(int)response.StatusCode}).",
                OperationCanceledException => "Ticket redemption was cancelled or timed out.",
                HttpRequestException => "Could not reach the ProCut Suite print API.",
                _ => "Could not retrieve a valid print document from ProCut Suite."
            };
            if (_redemptionFailures.ShouldLog(reason))
            {
                activity($"Print request failed. {reason}");
            }
            return false;
        }

        if (!_queues.TryGetValue(document.PrinterId, out var printer))
        {
            activity($"Print job {document.JobId} failed. The requested printer is no longer shared.");
            return false;
        }

        var name = DesktopPrintActivity.PrinterName(printer.Name);
        try
        {
            var installed = await GetInstalledQueueNamesAsync(linked.Token).ConfigureAwait(false);
            if (installed is null)
            {
                activity($"Print job {document.JobId} failed for {name}. Could not check installed print queues.");
                return false;
            }

            if (!installed.Contains(printer.Queue))
            {
                activity($"Print job {document.JobId} failed for {name}. Its print queue is no longer installed.");
                return false;
            }

            activity($"Print job {document.JobId} received for {name}. Sending to the local print queue.");
            await spooler.QueuePdfAsync(printer.Queue, document.Pdf, document.JobId, linked.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            var reason = exception is OperationCanceledException
                ? "Queue submission was cancelled or timed out."
                : "The local print queue did not confirm submission. Check the printer queue on this computer.";
            activity($"Print job {document.JobId} failed for {name}. {reason}");
            return false;
        }

        activity($"Print job {document.JobId} queued on {name}.");
        return true;
    }

    private async Task<HashSet<string>?> GetInstalledQueueNamesAsync(CancellationToken token)
    {
        try
        {
            var installed = await installedPrinters(token).ConfigureAwait(false);
            return installed.Select(printer => printer.Queue).ToHashSet(QueueComparer);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private async Task HeartbeatAsync()
    {
        var target = api.IsDevelopment ? "the local ProCut Suite API (127.0.0.1:5173)" : "the configured ProCut Suite API";
        string? previousRegistration = null;
        void ReportRegistration(string message)
        {
            status($"HTTPS listener running. {message}");
            if (previousRegistration != message)
            {
                activity(message);
                previousRegistration = message;
            }
        }

        using var interval = new PeriodicTimer(heartbeatInterval);
        try
        {
            do
            {
                try
                {
                    var installed = await GetInstalledQueueNamesAsync(_stopping.Token).ConfigureAwait(false);
                    if (installed is null)
                    {
                        ReportRegistration("Could not check installed print queues. Retrying in one minute.");
                        continue;
                    }

                    var printers = settings.Printers.Where(printer => installed.Contains(printer.Queue))
                        .Select(printer => new DesktopPrintPrinter(printer.Id, printer.Name)).ToArray();
                    if (printers.Length == 0)
                    {
                        // The backend requires at least one printer. Let any previous registration expire.
                        ReportRegistration("No shared printers are currently installed. Retrying in one minute.");
                        continue;
                    }

                    await backend.RegisterAsync(api, settings.WorkshopId, stationId, settings.Name, settings.Origin,
                        printers, _stopping.Token).ConfigureAwait(false);
                    var missing = settings.Printers.Length - printers.Length;
                    var registration = $"Station registered with {target}.";
                    ReportRegistration(missing switch
                    {
                        0 => registration,
                        1 => $"{registration} One selected printer is no longer installed.",
                        _ => $"{registration} {missing} selected printers are no longer installed."
                    });
                }
                catch (DesktopPrintBackendStatusException exception) when (!_stopping.IsCancellationRequested)
                {
                    var reason = exception.StatusCode switch
                    {
                        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                            "Registration was rejected. Check the API key and its workshop permissions.",
                        HttpStatusCode.NotFound =>
                            "The workshop or station registration is unavailable for this API key.",
                        HttpStatusCode.TooManyRequests => "The API is limiting registration requests.",
                        _ => "The API could not register this station."
                    };
                    DiagnosticLog.WriteInfo($"Print Sharing registration to {target} returned HTTP {(int)exception.StatusCode}.");
                    ReportRegistration($"Station registration failed with {target} (HTTP {(int)exception.StatusCode}). {reason} Retrying in one minute.");
                }
                catch (Exception exception) when (!_stopping.IsCancellationRequested &&
                    exception is (InvalidOperationException or IOException or HttpRequestException or OperationCanceledException))
                {
                    var reason = exception is InvalidOperationException
                        ? exception.Message
                        : "The registration request could not be completed.";
                    DiagnosticLog.WriteInfo($"Print Sharing registration to {target} failed. {reason}");
                    ReportRegistration($"Station registration failed with {target}. The registration request could not be completed. Retrying in one minute.");
                }
            }
            while (await interval.WaitForNextTickAsync(_stopping.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stopping.Cancel();
        try
        {
            if (_receiver is not null)
            {
                await _receiver.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                if (_heartbeat is not null)
                {
                    await _heartbeat.ConfigureAwait(false);
                }
            }
            finally
            {
                certificate.Dispose();
                _stopping.Dispose();
            }
        }
    }
}
