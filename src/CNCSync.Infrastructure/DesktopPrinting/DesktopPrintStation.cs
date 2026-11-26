using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using CNCSync.Core.Configuration;
using CNCSync.Infrastructure.Configuration;
using CNCSync.Infrastructure.Logging;

namespace CNCSync.Infrastructure.DesktopPrinting;

internal delegate Task<IAsyncDisposable> DesktopPrintReceiverFactory(
    IPAddress address, int port, X509Certificate2 certificate, X509Certificate2Collection chain,
    IReadOnlyCollection<string> origins, string expectedOrigin, Func<string, CancellationToken, Task<bool>> queue,
    CancellationToken cancellationToken);

public sealed class DesktopPrintStation : IAsyncDisposable
{
    public const int ManagedHttpsPort = 8443;

    private const int MaxSettingsBytes = 256 * 1024;
    private readonly string _settingsPath;
    private readonly DesktopPrintBackendClient _backend;
    private readonly Lazy<ManagedStationCertificateAuthority> _certificateAuthority;
    private readonly Lazy<PublicStationCertificateProvisioner> _publicCertificate;
    private readonly Lazy<ISecretStore> _secretStore;
    private readonly string _developmentKeyAccount;
    private readonly IPrintSpooler? _spooler;
    private readonly Func<IReadOnlyList<DesktopPrintNetworkInterface>> _networkInterfaces;
    private readonly Func<CancellationToken, Task<IReadOnlyList<InstalledPrintQueue>>> _installedPrinters;
    private readonly TimeSpan _networkCheckInterval;
    private readonly TimeSpan _heartbeatInterval;
    private readonly DesktopPrintReceiverFactory _startReceiver;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private DesktopPrintStationSettings _settings;
    private ProCutApiSettings _api;
    private DesktopPrintSession? _session;
    private DesktopPrintNetworkInterface? _activeInterface;
    private Task? _networkCheck;
    private long _nextPublicCertificateAttempt;
    private string? _publicCertificateAddress;
    private string? _publicCertificateMessage;
    private string _status = "Print sharing is disabled.";
    private int _disposed;

    public DesktopPrintStation(string directory, ProCutApiSettings api)
        : this(directory, api, new DesktopPrintBackendClient(), null, null)
    {
    }

    internal DesktopPrintStation(string directory, ProCutApiSettings api, DesktopPrintBackendClient backend,
        IPrintSpooler? spooler, DesktopPrintReceiverFactory? startReceiver,
        Func<IReadOnlyList<DesktopPrintNetworkInterface>>? networkInterfaces = null, TimeSpan? networkCheckInterval = null,
        ISecretStore? certificateSecretStore = null,
        Func<CancellationToken, Task<IReadOnlyList<InstalledPrintQueue>>>? installedPrinters = null,
        TimeSpan? heartbeatInterval = null)
    {
        _settingsPath = Path.Combine(directory, "desktop-print.json");
        _api = CopyApi(api);
        _backend = backend;
        _spooler = spooler;
        _startReceiver = startReceiver ?? StartLanReceiverAsync;
        _networkInterfaces = networkInterfaces ?? DesktopPrintNetwork.GetInterfaces;
        _installedPrinters = installedPrinters ?? InstalledPrintQueues.GetInstalledAsync;
        _networkCheckInterval = networkCheckInterval ?? TimeSpan.FromSeconds(5);
        _heartbeatInterval = heartbeatInterval ?? TimeSpan.FromMinutes(1);

        var stored = ReadSettings();
        StationId = stored?.StationId ?? Guid.NewGuid();
        _settings = stored?.Settings ?? new DesktopPrintStationSettings { UsePublicCertificate = true, UseManagedCertificate = false };
        _developmentKeyAccount = "print-development-api:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(directory))));
        _secretStore = new Lazy<ISecretStore>(() => certificateSecretStore ?? PrintStationSecretStore.Create(directory));
        _certificateAuthority = new Lazy<ManagedStationCertificateAuthority>(() =>
            new ManagedStationCertificateAuthority(directory, StationId, _secretStore.Value));
        _publicCertificate = new Lazy<PublicStationCertificateProvisioner>(() =>
            new PublicStationCertificateProvisioner(_secretStore.Value, _backend, StationId));
    }

    public Guid StationId { get; }
    public static string DefaultName { get; } = DefaultStationName();
    public static IReadOnlyList<DesktopPrintNetworkInterface> GetNetworkInterfaces() => DesktopPrintNetwork.GetInterfaces();
    public DesktopPrintStationSettings Settings => DesktopPrintStationConfiguration.Copy(Volatile.Read(ref _settings));
    public byte[]? ExportTrustCertificate()
    {
        var settings = Settings;
        return !settings.UsePublicCertificate && settings.UseManagedCertificate
            ? _certificateAuthority.Value.ExportRootCertificate()
            : null;
    }

    public string Status => Volatile.Read(ref _status);
    public string? ActiveOrigin => Volatile.Read(ref _session)?.Origin;
    public event Action<string>? StatusChanged;
    public event Action<string>? ActivityLogged;

    private async Task<IAsyncDisposable> StartLanReceiverAsync(IPAddress address, int port,
        X509Certificate2 certificate, X509Certificate2Collection chain, IReadOnlyCollection<string> origins,
        string expectedOrigin, Func<string, CancellationToken, Task<bool>> queue, CancellationToken token) =>
        await LanPrintReceiver.StartAsync(address, port, certificate, origins, queue, token,
            certificateChain: chain, expectedOrigin: expectedOrigin, activity: LogActivity).ConfigureAwait(false);

    public Task<bool> HasSavedDevelopmentKeyAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => ResolveDevelopmentKey(null).Length > 0, cancellationToken);

    public async Task<IReadOnlyList<DesktopPrintWorkshop>> GetWorkshopsAsync(
        CancellationToken cancellationToken = default, string? developmentKey = null)
    {
        var api = Volatile.Read(ref _api);
        if (developmentKey is null)
        {
            return await _backend.GetWorkshopsAsync(DesktopPrintBackendAccess.ForProduction(api), cancellationToken)
                .ConfigureAwait(false);
        }

        var key = ResolveDevelopmentKey(developmentKey);
        var workshops = await _backend.GetWorkshopsAsync(CreateDevelopmentAccess(api, key), cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(developmentKey))
        {
            cancellationToken.ThrowIfCancellationRequested();
            StoreDevelopmentKey(key);
        }

        return workshops;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed == 0 && _settings.Enabled && _networkCheck is null)
            {
                StartNetworkCheck();
                await TryStartAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    // Returns a warning only if an entered development key could not be saved.
    public async Task<string?> SaveAsync(DesktopPrintStationSettings settings, CancellationToken cancellationToken = default,
        string? developmentKey = null)
    {
        var saved = DesktopPrintStationConfiguration.Copy(settings) with
        {
            Name = string.IsNullOrWhiteSpace(settings.Name) ? DefaultName : settings.Name
        };
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
            await WriteSettingsAsync(saved, linked.Token).ConfigureAwait(false);
            var previous = Interlocked.Exchange(ref _settings, saved);
            ResetPublicCertificateRetry();
            LogSettingsChanges(previous, saved);
            LogPrinterChanges(previous, saved);
            DiagnosticLog.WriteInfo($"Print Sharing settings saved. Enabled: {saved.Enabled}.");
            string? warning = null;
            try
            {
                await StopSessionAsync().ConfigureAwait(false);
                warning = saved.UseLocalDevelopmentApi && !string.IsNullOrWhiteSpace(developmentKey)
                    ? await TrySaveDevelopmentKeyAsync(developmentKey.Trim(), linked.Token).ConfigureAwait(false)
                    : null;
                if (saved.Enabled)
                {
                    StartNetworkCheck();
                    await TryStartAsync(linked.Token).ConfigureAwait(false);
                }
                else
                {
                    SetStatus("Print sharing is disabled.");
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                InvalidOperationException or CryptographicException)
            {
                SetStartupStatus("Could not apply the saved print sharing settings. Check Activity and try again.");
            }

            return warning;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private void LogSettingsChanges(DesktopPrintStationSettings previous, DesktopPrintStationSettings saved)
    {
        if (previous.Enabled != saved.Enabled)
        {
            LogActivity(saved.Enabled ? "Print sharing enabled." : "Print sharing disabled.");
        }

        var localOrigin = DesktopPrintStationConfiguration.LocalDevelopmentBrowserOrigin;
        var allowedWebsites = previous.BrowserOrigins.Where(origin => origin != localOrigin).ToHashSet(StringComparer.Ordinal);
        (bool Changed, string Name)[] inspected =
        {
            (previous.Name != saved.Name, "station name"),
            (previous.WorkshopId != saved.WorkshopId, "workshop"),
            (previous.NetworkInterfaceId != saved.NetworkInterfaceId, "network adapter"),
            (previous.UseLocalDevelopmentApi != saved.UseLocalDevelopmentApi, "ProCut Suite API target"),
            (previous.UsePublicCertificate != saved.UsePublicCertificate ||
                previous.UseManagedCertificate != saved.UseManagedCertificate ||
                previous.CertificatePath != saved.CertificatePath || previous.PrivateKeyPath != saved.PrivateKeyPath ||
                previous.Origin != saved.Origin, "HTTPS configuration"),
            (previous.AllowLocalDevelopmentOrigin != saved.AllowLocalDevelopmentOrigin ||
                !allowedWebsites.SetEquals(saved.BrowserOrigins.Where(origin => origin != localOrigin)), "permitted websites"),
            (previous.WindowsPdfRendererPath != saved.WindowsPdfRendererPath, "Windows PDF renderer")
        };
        var changed = inspected.Where(change => change.Changed).Select(change => change.Name).ToArray();
        if (changed.Length > 0)
        {
            LogActivity($"Print sharing settings saved. Updated: {string.Join(", ", changed)}.");
        }
    }

    private void LogPrinterChanges(DesktopPrintStationSettings previous, DesktopPrintStationSettings saved)
    {
        var before = GetSelectedPrinters(previous);
        var after = GetSelectedPrinters(saved);
        foreach (var (id, name) in before)
        {
            if (!after.ContainsKey(id))
            {
                LogActivity($"Printer unshared: {name}.");
            }
        }

        foreach (var (id, name) in after)
        {
            if (!before.TryGetValue(id, out var oldName))
            {
                LogActivity($"Printer shared: {name}.");
            }
            else if (name != oldName)
            {
                LogActivity($"Shared printer renamed from {oldName} to {name}.");
            }
        }
    }

    private static Dictionary<string, string> GetSelectedPrinters(DesktopPrintStationSettings settings) =>
        settings.Printers.Where(printer => printer is not null && DesktopPrintBackendClient.IsPrinterId(printer.Id))
            .GroupBy(printer => printer.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => DesktopPrintActivity.PrinterName(group.First().Name), StringComparer.Ordinal);

    public async Task UpdateApiSettingsAsync(ProCutApiSettings settings)
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed != 0 || (_api.BaseUrl == settings.BaseUrl && _api.ApiKey == settings.ApiKey))
            {
                return;
            }

            Volatile.Write(ref _api, CopyApi(settings));
            ResetPublicCertificateRetry();
            await StopSessionAsync().ConfigureAwait(false);
            if (_settings.Enabled)
            {
                await TryStartAsync(_shutdown.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _shutdown.Cancel();
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopSessionAsync().ConfigureAwait(false);
            _backend.Dispose();
        }
        finally
        {
            _lifecycle.Release();
        }

        if (_networkCheck is { } networkCheck)
        {
            await networkCheck.ConfigureAwait(false);
        }

        _shutdown.Dispose();
    }

    private async Task TryStartAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        DesktopPrintStationSettings validated;
        try
        {
            validated = DesktopPrintStationConfiguration.Validate(_settings, useBuiltInSpooler: _spooler is null);
        }
        catch (ArgumentException exception)
        {
            SetStartupStatus(exception.Message);
            return;
        }

        try
        {
            Volatile.Write(ref _settings, validated);
            await StartSessionAsync(validated, linked.Token).ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            SetStartupStatus(exception.Message);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
        }
    }

    private async Task StartSessionAsync(DesktopPrintStationSettings settings, CancellationToken cancellationToken,
        bool refreshExisting = false)
    {
        DesktopPrintBackendAccess access;
        try
        {
            access = settings.UseLocalDevelopmentApi
                ? CreateDevelopmentAccess(_api, ResolveDevelopmentKey(null))
                : DesktopPrintBackendAccess.ForProduction(_api);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            SetStartupStatus(exception.Message);
            return;
        }

        if (!settings.UseLocalDevelopmentApi &&
            (string.IsNullOrWhiteSpace(_api.ApiKey) || !ProCutApiUri.TryGetBaseUri(_api.BaseUrl, out _)))
        {
            SetStartupStatus("Print sharing needs a saved ProCut Suite API key and HTTPS URL.");
            return;
        }

        var networkInterface = FindNetworkInterface();
        if (networkInterface is null)
        {
            SetStartupStatus("Waiting for the selected network adapter to get a private IPv4 address.");
            return;
        }

        var address = IPAddress.Parse(networkInterface.Address);
        DesktopPrintCertificate certificate;
        if (settings.UsePublicCertificate)
        {
            var provisioned = await ResolvePublicCertificateAsync(access, settings.WorkshopId, address,
                refreshExisting, cancellationToken).ConfigureAwait(false);
            if (provisioned is null)
            {
                return;
            }

            certificate = provisioned.Certificate;
            if (refreshExisting && _session is { } active &&
                certificate.Leaf.NotAfter.ToUniversalTime() <= active.CertificateExpiresAtUtc)
            {
                certificate.Dispose();
                return;
            }

            settings = settings with { Origin = provisioned.Origin };
            if (refreshExisting)
            {
                try
                {
                    await StopSessionAsync().ConfigureAwait(false);
                }
                catch
                {
                    certificate.Dispose();
                    throw;
                }

                LogActivity("The publicly trusted print certificate was renewed.");
            }
        }
        else if (settings.UseManagedCertificate)
        {
            settings = settings with { Origin = new UriBuilder("https", address.ToString(), ManagedHttpsPort).Uri.GetLeftPart(UriPartial.Authority) };
            try
            {
                certificate = _certificateAuthority.Value.LoadOrIssue(address);
            }
            catch (ManagedStationCertificateException exception)
            {
                SetStartupStatus(exception.Message);
                return;
            }
            catch (Exception exception) when (exception is IOException or CryptographicException or UnauthorizedAccessException or
                InvalidOperationException or NotSupportedException)
            {
                SetStartupStatus("Could not prepare the station certificate. Check access to the system's protected key storage.");
                return;
            }
        }
        else
        {
            certificate = DesktopPrintCertificate.Load(settings);
        }

        var spooler = _spooler ?? new OsPrintSpooler(settings.WindowsPdfRendererPath);
        var session = new DesktopPrintSession(settings, access, StationId, _backend, spooler, certificate, address,
            _installedPrinters, _heartbeatInterval, SetStatus, LogActivity);
        try
        {
            await session.StartAsync(_startReceiver, cancellationToken).ConfigureAwait(false);
            _session = session;
            _activeInterface = networkInterface;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or
            System.Net.Sockets.SocketException or UnauthorizedAccessException or OperationCanceledException)
        {
            await session.DisposeAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            SetStartupStatus("Could not start the HTTPS listener. Check the LAN IP address, port and certificate.");
        }
    }

    private async Task StopSessionAsync()
    {
        if (_session is { } session)
        {
            _session = null;
            _activeInterface = null;
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
                LogActivity("HTTPS listener stopped.");
            }
            catch
            {
                LogActivity("An error occurred while stopping the HTTPS listener.");
                throw;
            }
        }
    }

    private DesktopPrintNetworkInterface? FindNetworkInterface() =>
        _networkInterfaces().FirstOrDefault(adapter => adapter.Id == _settings.NetworkInterfaceId);

    private void StartNetworkCheck() => _networkCheck ??= MonitorNetworkAsync(_shutdown.Token);

    private async Task MonitorNetworkAsync(CancellationToken cancellationToken)
    {
        using var interval = new PeriodicTimer(_networkCheckInterval);
        try
        {
            while (await interval.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (!_settings.Enabled || Volatile.Read(ref _disposed) != 0)
                    {
                        continue;
                    }

                    if (_session is not { } session)
                    {
                        await TryStartAsync(cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    var adapter = FindNetworkInterface();
                    if (adapter is not null && _activeInterface is not null &&
                        adapter.Address == _activeInterface.Address && adapter.PrefixLength == _activeInterface.PrefixLength)
                    {
                        var remaining = session.CertificateExpiresAtUtc - DateTime.UtcNow;
                        if (_settings.UsePublicCertificate && remaining > TimeSpan.Zero)
                        {
                            if (remaining <= PublicStationCertificateProvisioner.RenewalWindow)
                            {
                                await StartSessionAsync(_settings, cancellationToken, refreshExisting: true).ConfigureAwait(false);
                            }

                            continue;
                        }

                        if (!_settings.UsePublicCertificate &&
                            (!_settings.UseManagedCertificate || remaining > ManagedStationCertificateAuthority.RenewalWindow))
                        {
                            continue;
                        }
                    }

                    if (_settings.UsePublicCertificate)
                    {
                        ResetPublicCertificateRetry();
                        if (session.CertificateExpiresAtUtc <= DateTime.UtcNow)
                        {
                            SetStartupStatus("The publicly trusted print certificate expired. Waiting for renewal.");
                        }
                    }

                    await StopSessionAsync().ConfigureAwait(false);
                    if (adapter is not null)
                    {
                        await TryStartAsync(cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        SetStartupStatus("Waiting for the selected network adapter to get a private IPv4 address.");
                    }
                }
                finally
                {
                    _lifecycle.Release();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task<ProvisionedPrintCertificate?> ResolvePublicCertificateAsync(DesktopPrintBackendAccess access,
        string workshopId, IPAddress address, bool refreshExisting, CancellationToken token)
    {
        var addressText = address.ToString();
        var now = Environment.TickCount64;
        if (addressText == _publicCertificateAddress && now < _nextPublicCertificateAttempt)
        {
            return null;
        }

        _publicCertificateAddress = addressText;
        _nextPublicCertificateAttempt = now + (long)TimeSpan.FromSeconds(30).TotalMilliseconds;
        try
        {
            var result = await _publicCertificate.Value.ResolveAsync(access, workshopId, address, token).ConfigureAwait(false);
            if (result is null)
            {
                ReportPublicCertificateStatus("Waiting for the publicly trusted print certificate and DNS.", refreshExisting);
            }
            else
            {
                _publicCertificateMessage = null;
            }

            return result;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or
            CryptographicException or ArgumentException or NotSupportedException or OperationCanceledException)
        {
            token.ThrowIfCancellationRequested();
            var message = exception switch
            {
                DesktopPrintBackendStatusException { StatusCode: HttpStatusCode.NotFound } =>
                    "Public print certificates are unavailable for this workshop. Check with your administrator or choose the local certificate option.",
                DesktopPrintBackendStatusException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } =>
                    "ProCut Suite denied the public print certificate request. Check this station's API key and workshop.",
                PublicPrintCertificateProvisioningException => "ProCut Suite could not finish provisioning the public print certificate and DNS. It will retry.",
                CryptographicException => "Could not verify the public print certificate or access its protected key.",
                _ => "Could not prepare the public print certificate. PCS Sync will retry."
            };
            ReportPublicCertificateStatus(message, refreshExisting);
            return null;
        }
    }

    private void ResetPublicCertificateRetry()
    {
        _publicCertificateAddress = null;
        _publicCertificateMessage = null;
        _nextPublicCertificateAttempt = 0;
    }

    private void ReportPublicCertificateStatus(string message, bool refreshExisting)
    {
        if (!refreshExisting)
        {
            SetStartupStatus(message);
        }
        else if (_publicCertificateMessage != message)
        {
            LogActivity(message);
        }

        _publicCertificateMessage = message;
    }

    private StoredSettings? ReadSettings()
    {
        try
        {
            using var stream = File.OpenRead(_settingsPath);
            if (stream.Length > MaxSettingsBytes)
            {
                throw new JsonException();
            }

            using var document = JsonDocument.Parse(stream);
            var stored = document.Deserialize<StoredSettings>();
            if (stored is not { StationId: var id, Settings: not null } || id == Guid.Empty)
            {
                throw new JsonException();
            }

            var storedSettings = document.RootElement.GetProperty(nameof(StoredSettings.Settings));
            if (!storedSettings.TryGetProperty(nameof(DesktopPrintStationSettings.UseManagedCertificate), out _) &&
                (stored.Settings.Enabled || !string.IsNullOrWhiteSpace(stored.Settings.CertificatePath) ||
                 !string.IsNullOrWhiteSpace(stored.Settings.PrivateKeyPath)))
            {
                return stored with { Settings = stored.Settings with { UseManagedCertificate = false } };
            }

            return stored;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _status = "Could not read the print sharing configuration. Review the settings before enabling it.";
            return null;
        }
    }

    private async Task WriteSettingsAsync(DesktopPrintStationSettings settings, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new StoredSettings(StationId, settings));
        if (bytes.Length > MaxSettingsBytes)
        {
            throw new ArgumentException("The print sharing configuration is too large.");
        }

        var temporaryPath = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, bufferSize: 4096, useAsync: true))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private void SetStatus(string status)
    {
        if (Interlocked.Exchange(ref _status, status) != status)
        {
            StatusChanged?.Invoke(status);
        }
    }

    private void SetStartupStatus(string status)
    {
        if (Status != status)
        {
            SetStatus(status);
            LogActivity(status);
        }
    }

    private void LogActivity(string message) => ActivityLogged?.Invoke(message);

    private string ResolveDevelopmentKey(string? enteredKey)
    {
        if (!string.IsNullOrWhiteSpace(enteredKey))
        {
            return enteredKey.Trim();
        }

        try
        {
            return _secretStore.Value.GetSecret(_developmentKeyAccount)?.Trim() ?? string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or
            InvalidOperationException or NotSupportedException)
        {
            throw new InvalidOperationException("Could not read the development API key from protected storage.");
        }
    }

    private void StoreDevelopmentKey(string key)
    {
        try
        {
            _secretStore.Value.SetSecret(_developmentKeyAccount, key);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or
            InvalidOperationException or NotSupportedException)
        {
            throw new InvalidOperationException("Could not save the development API key to protected storage.");
        }
    }

    private async Task<string?> TrySaveDevelopmentKeyAsync(string key, CancellationToken token)
    {
        try
        {
            var access = CreateDevelopmentAccess(_api, key);
            if (key != ResolveDevelopmentKey(null))
            {
                await _backend.GetWorkshopsAsync(access, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                StoreDevelopmentKey(key);
                LogActivity("The print sharing development API key was updated.");
            }

            return null;
        }
        catch (DesktopPrintBackendStatusException exception)
        {
            return $"The new development API key could not be verified. The local API returned HTTP {(int)exception.StatusCode}.";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return exception.Message;
        }
        catch (OperationCanceledException)
        {
            return "The new development API key could not be verified before the request ended.";
        }
        catch (HttpRequestException)
        {
            return "The new development API key could not be verified. The local API could not be reached.";
        }
    }

    private static DesktopPrintBackendAccess CreateDevelopmentAccess(ProCutApiSettings api, string developmentKey)
    {
        if (string.IsNullOrWhiteSpace(developmentKey))
        {
            throw new ArgumentException("Enter a development API key from the local ProCut Suite site.");
        }

        if (string.Equals(developmentKey.Trim(), api.ApiKey?.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Use a separate development API key for the local ProCut Suite site.");
        }

        return DesktopPrintBackendAccess.ForDevelopment(developmentKey);
    }

    private static ProCutApiSettings CopyApi(ProCutApiSettings settings) =>
        new() { BaseUrl = settings.BaseUrl, ApiKey = settings.ApiKey };

    private static string DefaultStationName()
    {
        var name = Environment.MachineName;
        var maxLength = DesktopPrintBackendClient.MaxRegistrationNameLength;
        return !string.IsNullOrWhiteSpace(name) && name.Length <= maxLength && !name.Any(char.IsControl)
            ? name
            : "Workshop print station";
    }

    private sealed record StoredSettings(Guid StationId, DesktopPrintStationSettings Settings);
}
