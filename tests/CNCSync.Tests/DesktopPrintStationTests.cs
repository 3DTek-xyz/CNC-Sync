using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CNCSync.Core.Configuration;
using CNCSync.Infrastructure.DesktopPrinting;

namespace CNCSync.Tests;

public sealed class DesktopPrintStationTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("pcs-print-").FullName;
    private static readonly ProCutApiSettings Api = new() { BaseUrl = "https://api.example.com", ApiKey = "fixture-key" };

    [Fact]
    public async Task StationIsOptInAndPersistsMachineIdentityAcrossRestarts()
    {
        await using var station = new DesktopPrintStation(_directory, Api);
        await station.StartAsync();
        Assert.False(station.Settings.Enabled);
        Assert.True(station.Settings.UsePublicCertificate);
        await station.SaveAsync(station.Settings with { Name = "Label bench" });
        using var stored = System.Text.Json.JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(_directory, "desktop-print.json")));
        var identity = stored.RootElement.GetProperty("StationId").GetGuid();

        await using var reopened = new DesktopPrintStation(_directory, Api);
        Assert.Equal(identity, reopened.StationId);
        Assert.Equal("Label bench", reopened.Settings.Name);
        Assert.DoesNotContain(Api.ApiKey, stored.RootElement.GetRawText());
    }

    [Fact]
    public async Task ManagedStationCreatesTrustOnlyOnEnableAndFollowsItsLanAddress()
    {
        var handler = new PrintBackendHandler(Guid.NewGuid());
        using var http = new HttpClient(handler);
        using var backend = new DesktopPrintBackendClient(http);
        var secrets = new MemorySecretStore();
        var adapter = new DesktopPrintNetworkInterface("adapter-id", "Ethernet", "192.168.86.214");
        var listeners = System.Threading.Channels.Channel.CreateUnbounded<(string Origin, string Fingerprint)>();
        DesktopPrintReceiverFactory factory = (address, port, certificate, chain, _, origin, _, _) =>
        {
            Assert.Equal(DesktopPrintStation.ManagedHttpsPort, port);
            Assert.True(certificate.MatchesHostname(address.ToString(), allowWildcards: false, allowCommonName: false));
            Assert.NotEmpty(chain);
            listeners.Writer.TryWrite((origin, certificate.Thumbprint));
            return Task.FromResult<IAsyncDisposable>(new NoopReceiver());
        };
        await using var station = CreateStation();
        Assert.True(station.Settings.UsePublicCertificate);
        Assert.Null(station.ExportTrustCertificate());
        await station.StartAsync();
        await station.SaveAsync(station.Settings with { Name = "Draft" });
        Assert.Empty(secrets.Entries);

        await station.SaveAsync(CreateSettings() with
        {
            UseManagedCertificate = true,
            Origin = "",
            CertificatePath = "",
            PrivateKeyPath = ""
        });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var first = await listeners.Reader.ReadAsync(timeout.Token);
        Assert.Equal("https://192.168.86.214:8443", first.Origin);
        Assert.Equal(first.Origin, await handler.RegisteredOrigins.Reader.ReadAsync(timeout.Token));
        var root = station.ExportTrustCertificate();
        Assert.NotNull(root);
        using var rootCertificate = X509CertificateLoader.LoadCertificate(root);
        Assert.False(rootCertificate.HasPrivateKey);

        Volatile.Write(ref adapter, adapter with { Address = "192.168.86.215" });
        var next = await listeners.Reader.ReadAsync(timeout.Token);
        Assert.Equal("https://192.168.86.215:8443", next.Origin);
        Assert.Equal(next.Origin, await handler.RegisteredOrigins.Reader.ReadAsync(timeout.Token));
        Assert.NotEqual(first.Fingerprint, next.Fingerprint);
        Assert.Equal(root, station.ExportTrustCertificate());
        Assert.Equal("", station.Settings.Origin);
        await station.DisposeAsync();

        await using var restarted = CreateStation();
        Assert.Equal(station.StationId, restarted.StationId);
        Assert.Equal(root, restarted.ExportTrustCertificate());
        await restarted.StartAsync();
        var afterRestart = await listeners.Reader.ReadAsync(timeout.Token);
        Assert.Equal(next, afterRestart);

        DesktopPrintStation CreateStation() => new(_directory, Api, backend, new CapturingSpooler(),
            factory, () => [Volatile.Read(ref adapter)], TimeSpan.FromMilliseconds(20), secrets, ReadInstalledLabelQueue);
    }

    [Fact]
    public async Task ExistingManualCertificateSettingsRetainTheirMode()
    {
        var identity = Guid.NewGuid();
        var saved = System.Text.Json.JsonSerializer.SerializeToNode(new
        {
            StationId = identity, Settings = CreateSettings()
        })!.AsObject();
        saved["Settings"]!.AsObject().Remove(nameof(DesktopPrintStationSettings.UseManagedCertificate));
        await File.WriteAllTextAsync(Path.Combine(_directory, "desktop-print.json"), saved.ToJsonString());

        await using var station = new DesktopPrintStation(_directory, Api);

        Assert.Equal(identity, station.StationId);
        Assert.False(station.Settings.UseManagedCertificate);
        Assert.EndsWith("certificate.pem", station.Settings.CertificatePath);
    }

    [Fact]
    public async Task PublicCertificateRequestsKeepTheSameKeyAcrossDhcpChangesAndDoNotBindWhilePending()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var handler = new PendingPublicCertificateHandler();
        using var http = new HttpClient(handler);
        var secrets = new MemorySecretStore();
        var adapter = new DesktopPrintNetworkInterface("adapter-id", "Ethernet", "192.168.86.214");
        await using var station = new DesktopPrintStation(_directory, Api, new DesktopPrintBackendClient(http), null,
            (_, _, _, _, _, _, _, _) => throw new Xunit.Sdk.XunitException("Unexpected listener with pending TLS"),
            () => [Volatile.Read(ref adapter)], TimeSpan.FromMilliseconds(25), secrets);
        var activity = new ConcurrentQueue<string>();
        station.ActivityLogged += activity.Enqueue;
        await station.StartAsync(timeout.Token);
        Assert.Empty(secrets.Entries);

        await station.SaveAsync(station.Settings with
        {
            Enabled = true, WorkshopId = "12", Name = "Label bench", NetworkInterfaceId = "adapter-id",
            WindowsPdfRendererPath = Environment.ProcessPath!,
            Printers = [new("label-a", "Label printer", "OS-Label-Queue")]
        }, timeout.Token);

        var first = await handler.Requests.Reader.ReadAsync(timeout.Token);
        Assert.Equal("192.168.86.214", first.Address);
        Assert.StartsWith("-----BEGIN CERTIFICATE REQUEST-----", first.Csr);
        Assert.Contains(activity, message => message.Contains("Waiting for the publicly trusted print certificate"));
        Assert.Null(station.ActiveOrigin);
        Assert.Null(station.ExportTrustCertificate());

        Volatile.Write(ref adapter, adapter with { Address = "192.168.86.215" });
        var next = await handler.Requests.Reader.ReadAsync(timeout.Token);
        Assert.Equal("192.168.86.215", next.Address);
        Assert.Equal(first.Csr, next.Csr);
        Assert.StartsWith("-----BEGIN PRIVATE KEY-----", Assert.Single(secrets.Entries).Value);
        var file = await File.ReadAllTextAsync(Path.Combine(_directory, "desktop-print.json"), timeout.Token);
        Assert.DoesNotContain("PRIVATE KEY", file);
        Assert.DoesNotContain(activity, message => message.Contains(first.Csr) || message.Contains(Api.ApiKey));
    }

    [Theory]
    [InlineData("s-print.print.example.com", "chain")]
    [InlineData("*.print.example.com", "match")]
    public void PublicCertificateRejectsUntrustedRootsAndWildcardNames(string suppliedName, string expectedMessage)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={suppliedName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var builder = new SubjectAlternativeNameBuilder();
        builder.AddDnsName(suppliedName);
        request.CertificateExtensions.Add(builder.Build());
        using var leaf = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        var error = Assert.Throws<CryptographicException>(() =>
            PublicStationCertificateProvisioner.LoadCertificate(leaf.ExportCertificatePem(), "s-print.print.example.com", key));
        Assert.Contains(expectedMessage, error.Message);
    }

    [Fact]
    public void PublicCertificateMatchesProtectedKeyAndSendsOnlyTheIntermediate()
    {
        var now = DateTimeOffset.UtcNow;
        using var rootKey = RSA.Create(2048);
        using var intermediateKey = RSA.Create(2048);
        using var stationKey = RSA.Create(2048);
        using var otherKey = RSA.Create(2048);
        using var root = CreateCaRequest("Test public root", rootKey)
            .CreateSelfSigned(now.AddDays(-2), now.AddDays(90));
        using var intermediatePublic = CreateCaRequest("Test issuing authority", intermediateKey)
            .Create(root, now.AddDays(-1), now.AddDays(60), RandomNumberGenerator.GetBytes(16));
        using var intermediate = intermediatePublic.CopyWithPrivateKey(intermediateKey);

        const string hostname = "s-print.print.example.com";
        var csr = PublicStationCertificateProvisioner.CreateSigningRequest(stationKey, hostname);
        var stationRequest = CertificateRequest.LoadSigningRequestPem(csr, HashAlgorithmName.SHA256,
            CertificateRequestLoadOptions.UnsafeLoadCertificateExtensions, RSASignaturePadding.Pkcs1);
        using var stationCertificate = stationRequest.Create(intermediate, now.AddHours(-2), now.AddDays(30),
            RandomNumberGenerator.GetBytes(16));
        var pem = string.Join("\n", stationCertificate.ExportCertificatePem(), intermediate.ExportCertificatePem(), root.ExportCertificatePem());

        using var loaded = PublicStationCertificateProvisioner.LoadCertificate(pem, hostname, stationKey, UseTestRoot);
        Assert.True(loaded.Leaf.HasPrivateKey);
        Assert.Equal(intermediate.Thumbprint, Assert.Single(loaded.Chain).Thumbprint);
        Assert.ThrowsAny<ArgumentException>(() =>
            PublicStationCertificateProvisioner.LoadCertificate(pem, hostname, otherKey, UseTestRoot));

        void UseTestRoot(X509ChainPolicy policy)
        {
            policy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            policy.CustomTrustStore.Add(root);
        }

        static CertificateRequest CreateCaRequest(string name, RSA key)
        {
            var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, critical: true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, critical: true));
            return request;
        }
    }

    [Fact]
    public void PublicCertificateRequestForLongDomainsUsesOnlyTheDnsNameExtension()
    {
        const string host = "s-1234567890abcdef1234567890abcdef.workshopprintingservice.example.com";
        using var key = RSA.Create(2048);
        var csr = PublicStationCertificateProvisioner.CreateSigningRequest(key, host);
        var request = CertificateRequest.LoadSigningRequestPem(csr, HashAlgorithmName.SHA256,
            CertificateRequestLoadOptions.UnsafeLoadCertificateExtensions, RSASignaturePadding.Pkcs1);

        Assert.Equal(new byte[] { 0x30, 0x00 }, request.SubjectName.RawData);
        var extension = Assert.Single(request.CertificateExtensions);
        Assert.True(extension.Critical);
        Assert.Equal(host, Assert.Single(new X509SubjectAlternativeNameExtension(extension.RawData).EnumerateDnsNames()));
    }

    [Fact]
    public async Task LocalWorkshopDiscoveryUsesOnlyTheSeparateDevelopmentKey()
    {
        var productionRequests = 0;
        var secrets = new MemorySecretStore();
        var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stationId = Guid.Empty;
        using var productionHttp = new HttpClient(new WorkshopHandler(request =>
        {
            Interlocked.Increment(ref productionRequests);
            Assert.Equal("api.example.com", request.RequestUri!.Host);
            Assert.Equal(Api.ApiKey, request.Headers.Authorization?.Parameter);
        }));
        using var developmentHttp = new HttpClient(new WorkshopHandler(request =>
        {
            var path = request.Method == HttpMethod.Put
                ? $"workshops/12/stations/{stationId}/"
                : "workshops/";
            Assert.Equal(new Uri($"http://127.0.0.1:5173/api/nesting/desktop-print/{path}"), request.RequestUri);
            if (request.Headers.Authorization?.Parameter == "fixture-rejected-key")
            {
                return HttpStatusCode.Unauthorized;
            }

            Assert.Equal("fixture-development-key", request.Headers.Authorization?.Parameter);
            if (request.Method == HttpMethod.Put)
            {
                registered.TrySetResult();
            }

            return HttpStatusCode.OK;
        }));
        using var backend = new DesktopPrintBackendClient(productionHttp, developmentHttp);
        await using var station = new DesktopPrintStation(_directory, Api, backend, new CapturingSpooler(),
            (_, _, _, _, _, _, _, _) => Task.FromResult<IAsyncDisposable>(new NoopReceiver()),
            certificateSecretStore: secrets);
        stationId = station.StationId;

        Assert.False(await station.HasSavedDevelopmentKeyAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => station.GetWorkshopsAsync(default, Api.ApiKey));
        await Assert.ThrowsAsync<ArgumentException>(() => station.GetWorkshopsAsync(default, string.Empty));
        Assert.Single(await station.GetWorkshopsAsync(default, "fixture-development-key"));
        Assert.True(await station.HasSavedDevelopmentKeyAsync());
        Assert.Equal(0, productionRequests);
        Assert.Equal("fixture-development-key", Assert.Single(secrets.Entries).Value);
        await Assert.ThrowsAsync<DesktopPrintBackendStatusException>(
            () => station.GetWorkshopsAsync(default, "fixture-rejected-key"));
        var warning = await station.SaveAsync(station.Settings with { UseLocalDevelopmentApi = true }, default, "fixture-rejected-key");
        Assert.Contains("HTTP 401", warning);
        Assert.True(station.Settings.UseLocalDevelopmentApi);
        Assert.Equal("fixture-development-key", Assert.Single(secrets.Entries).Value);
        Assert.Single(await station.GetWorkshopsAsync());
        Assert.Equal(1, productionRequests);

        await station.SaveAsync(CreateSettings() with { UseLocalDevelopmentApi = true });
        var stored = await File.ReadAllTextAsync(Path.Combine(_directory, "desktop-print.json"));
        Assert.DoesNotContain("fixture-development-key", stored);

        using var reopenedBackend = new DesktopPrintBackendClient(productionHttp, developmentHttp);
        await using var reopened = new DesktopPrintStation(_directory, Api, reopenedBackend, new CapturingSpooler(),
            (_, _, _, _, _, _, _, _) => Task.FromResult<IAsyncDisposable>(new NoopReceiver()),
            () => [new DesktopPrintNetworkInterface("adapter-id", "Ethernet", "192.168.86.214")],
            certificateSecretStore: secrets, installedPrinters: ReadInstalledLabelQueue);
        Assert.True(await reopened.HasSavedDevelopmentKeyAsync());
        Assert.Single(await reopened.GetWorkshopsAsync(default, string.Empty));
        await reopened.StartAsync();
        await registered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, productionRequests);
    }

    [Fact]
    public async Task CorruptSettingsFailClosed()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, "desktop-print.json"), "{invalid");
        await using var station = new DesktopPrintStation(_directory, Api);

        await station.StartAsync();

        Assert.False(station.Settings.Enabled);
        Assert.Contains("Could not read", station.Status);
    }

    [Fact]
    public async Task AnEnabledDevelopmentStationRemembersItsSettingsWithoutAnApiKey()
    {
        var secrets = new MemorySecretStore();
        var requested = CreateSettings() with
        {
            UseLocalDevelopmentApi = true,
            AllowLocalDevelopmentOrigin = true,
            Printers = [new("printer-id", "Shipping labels", "TSC_DA210") { DisplayName = "Shipping labels" }]
        };
        await using (var station = CreateStation())
        {
            await station.SaveAsync(requested);
            Assert.Contains("development API key", station.Status);
        }

        await using var reopened = CreateStation();
        Assert.True(reopened.Settings.Enabled);
        Assert.True(reopened.Settings.UseLocalDevelopmentApi);
        Assert.True(reopened.Settings.AllowLocalDevelopmentOrigin);
        Assert.Equal(requested.WorkshopId, reopened.Settings.WorkshopId);
        Assert.Equal(requested.NetworkInterfaceId, reopened.Settings.NetworkInterfaceId);
        Assert.Equal(requested.Printers, reopened.Settings.Printers);
        await reopened.StartAsync();
        Assert.Contains("development API key", reopened.Status);

        DesktopPrintStation CreateStation() => new(_directory, Api, new DesktopPrintBackendClient(), new CapturingSpooler(),
            (_, _, _, _, _, _, _, _) => throw new InvalidOperationException("An unauthenticated station must not start."),
            certificateSecretStore: secrets);
    }

    [Fact]
    public async Task AnIncompletePrintSharingSetupSavesBothOptionsAndWaitsForAPrinter()
    {
        var requested = CreateSettings() with
        {
            Name = string.Empty, UseManagedCertificate = true, UseLocalDevelopmentApi = true,
            AllowLocalDevelopmentOrigin = true, Printers = []
        };
        var started = false;
        await using (var station = CreateStation())
        {
            await station.SaveAsync(requested);
            Assert.Contains("Select at least one printer", station.Status);
        }

        await using var reopened = CreateStation();
        await reopened.StartAsync();
        Assert.True(reopened.Settings.Enabled);
        Assert.True(reopened.Settings.UseLocalDevelopmentApi);
        Assert.True(reopened.Settings.AllowLocalDevelopmentOrigin);
        Assert.Equal(requested.WorkshopId, reopened.Settings.WorkshopId);
        Assert.False(string.IsNullOrWhiteSpace(reopened.Settings.Name));
        Assert.Empty(reopened.Settings.Printers);
        Assert.Contains("Select at least one printer", reopened.Status);
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        Assert.Contains("Select at least one printer", reopened.Status);
        Assert.False(started);

        DesktopPrintStation CreateStation() => new(_directory, Api, new DesktopPrintBackendClient(), new CapturingSpooler(),
            (_, _, _, _, _, _, _, _) =>
            {
                started = true;
                return Task.FromResult<IAsyncDisposable>(new NoopReceiver());
            }, () => [], TimeSpan.FromMilliseconds(10));
    }

    [Fact]
    public async Task InvalidCertificateIsSavedButDoesNotStartTheListener()
    {
        var started = false;
        await using var station = new DesktopPrintStation(_directory, Api, new DesktopPrintBackendClient(), new CapturingSpooler(),
            (_, _, _, _, _, _, _, _) =>
            {
                started = true;
                return Task.FromResult<IAsyncDisposable>(new NoopReceiver());
            }, () => [new DesktopPrintNetworkInterface("adapter-id", "Ethernet", "192.168.86.214")]);
        var enabled = CreateSettings() with { Origin = "https://different.example.com:7443" };
        var activity = new ConcurrentQueue<string>();
        station.ActivityLogged += activity.Enqueue;

        await station.SaveAsync(enabled);

        Assert.True(station.Settings.Enabled);
        Assert.Equal(enabled.Origin, station.Settings.Origin);
        Assert.Contains("HTTPS certificate and key", station.Status);
        Assert.False(started);
        Assert.Contains(activity, entry => ContainsAll(entry, "certificate and key"));
        Assert.DoesNotContain(activity, entry => ContainsAll(entry, "listener", "started"));
    }

    [Fact]
    public void AvailableAdaptersAdvertiseOnlyPrivateIpv4Addresses()
    {
        var systemAdapters = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
            .ToDictionary(adapter => adapter.Id);

        Assert.All(DesktopPrintStation.GetNetworkInterfaces(), adapter =>
        {
            var bytes = IPAddress.Parse(adapter.Address).GetAddressBytes();
            Assert.Equal(4, bytes.Length);
            Assert.True(bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                bytes[0] == 192 && bytes[1] == 168);
            Assert.Equal(systemAdapters[adapter.Id].Name, adapter.Name);
            Assert.InRange(adapter.PrefixLength, 1, 31);
        });
    }

    [Fact]
    public void ValidationRequiresNetworkAdapterAndRejectsDuplicatePrinterIds()
    {
        var settings = CreateSettings();
        Assert.Throws<ArgumentException>(() => DesktopPrintStationConfiguration.Validate(settings with { NetworkInterfaceId = "" }));
        Assert.Throws<ArgumentException>(() => DesktopPrintStationConfiguration.Validate(settings with { Origin = "https://labels.example.com/job" }));
        Assert.Throws<ArgumentException>(() => DesktopPrintStationConfiguration.Validate(settings with
        {
            Printers = [settings.Printers[0], settings.Printers[0] with { Queue = "Other" }]
        }, useBuiltInSpooler: false));
    }

    [Fact]
    public void DevelopmentOriginRequiresBothTheOptInAndExactOrigin()
    {
        var settings = CreateSettings() with { BrowserOrigins = ["http://localhost:5173"] };
        Assert.Throws<ArgumentException>(() => DesktopPrintStationConfiguration.Validate(settings, useBuiltInSpooler: false));
        Assert.Equal("http://localhost:5173",
            Assert.Single(DesktopPrintStationConfiguration.Validate(
                settings with { AllowLocalDevelopmentOrigin = true }, useBuiltInSpooler: false).BrowserOrigins));
        Assert.Throws<ArgumentException>(() => DesktopPrintStationConfiguration.Validate(settings with
        {
            AllowLocalDevelopmentOrigin = true,
            BrowserOrigins = ["http://localhost:5174"]
        }, useBuiltInSpooler: false));
    }

    [Theory]
    [InlineData("192.168.1.76", 24)]
    [InlineData("192.168.1.51", 25)]
    public async Task DhcpChangeRebindsAndRegistersTheSameStationAndOrigin(string nextAddress, int nextPrefix)
    {
        var capture = new PrintBackendHandler(Guid.NewGuid());
        using var client = new HttpClient(capture);
        using var backend = new DesktopPrintBackendClient(client);
        var listeners = System.Threading.Channels.Channel.CreateUnbounded<(IPAddress Address, string Origin, NoopReceiver Receiver)>();
        const string firstAddress = "192.168.1.51";
        var currentAdapter = new DesktopPrintNetworkInterface("adapter-id", "Ethernet", firstAddress) { PrefixLength = 24 };
        await using var station = new DesktopPrintStation(_directory, Api, backend, new CapturingSpooler(),
            (address, _, _, _, _, origin, _, _) =>
            {
                var receiver = new NoopReceiver();
                listeners.Writer.TryWrite((address, origin, receiver));
                return Task.FromResult<IAsyncDisposable>(receiver);
            }, () => [Volatile.Read(ref currentAdapter)], TimeSpan.FromMilliseconds(20), installedPrinters: ReadInstalledLabelQueue);

        await station.SaveAsync(CreateSettings());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var first = await listeners.Reader.ReadAsync(timeout.Token);
        var firstRegistration = await capture.Registrations.Reader.ReadAsync(timeout.Token);
        Volatile.Write(ref currentAdapter, currentAdapter with { Address = nextAddress, PrefixLength = nextPrefix });

        var next = await listeners.Reader.ReadAsync(timeout.Token);
        var nextRegistration = await capture.Registrations.Reader.ReadAsync(timeout.Token);

        Assert.True(first.Receiver.Disposed);
        Assert.Equal(firstAddress, first.Address.ToString());
        Assert.Equal(nextAddress, next.Address.ToString());
        Assert.Equal(first.Origin, next.Origin);
        Assert.Equal(firstRegistration, nextRegistration);
        Assert.EndsWith($"/stations/{station.StationId:D}/", nextRegistration);
    }

    [Fact]
    public async Task RestoredStationWaitsForItsAdapterAndRegistersWhenConnected()
    {
        var capture = new PrintBackendHandler(Guid.NewGuid());
        using var client = new HttpClient(capture);
        using var backend = new DesktopPrintBackendClient(client);
        var adapter = new DesktopPrintNetworkInterface("adapter-id", "Ethernet", "192.168.1.51");
        DesktopPrintNetworkInterface? activeAdapter = null;
        var listenerStarts = 0;
        DesktopPrintReceiverFactory factory = (_, _, _, _, _, _, _, _) =>
        {
            Interlocked.Increment(ref listenerStarts);
            return Task.FromResult<IAsyncDisposable>(new NoopReceiver());
        };
        IReadOnlyList<DesktopPrintNetworkInterface> Interfaces() =>
            Volatile.Read(ref activeAdapter) is { } active ? [active] : [];

        Guid identity;
        await using (var station = new DesktopPrintStation(_directory, Api, backend, new CapturingSpooler(), factory, Interfaces))
        {
            identity = station.StationId;
            await station.SaveAsync(CreateSettings());
            Assert.Contains("Waiting", station.Status);
            Assert.Equal(0, listenerStarts);
        }

        await using var restored = new DesktopPrintStation(_directory, Api, backend, new CapturingSpooler(),
            factory, Interfaces, TimeSpan.FromMilliseconds(20), installedPrinters: ReadInstalledLabelQueue);
        Assert.Equal(identity, restored.StationId);
        await restored.StartAsync();
        Assert.Contains("Waiting", restored.Status);
        Assert.Equal(0, listenerStarts);

        Volatile.Write(ref activeAdapter, adapter);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await capture.Registered.Task.WaitAsync(timeout.Token);

        Assert.Equal(1, Volatile.Read(ref listenerStarts));
    }

    [Fact]
    public async Task RegistrationRejectionShowsAnActionableStatusWithoutBackendDetails()
    {
        using var client = new HttpClient(new PrintBackendHandler(Guid.NewGuid()) { RegistrationStatus = HttpStatusCode.Unauthorized });
        using var backend = new DesktopPrintBackendClient(client);
        await using var station = new DesktopPrintStation(_directory, Api, backend, new CapturingSpooler(),
            (_, _, _, _, _, _, _, _) => Task.FromResult<IAsyncDisposable>(new NoopReceiver()),
            () => [new("adapter-id", "Ethernet", "192.168.1.51")], installedPrinters: ReadInstalledLabelQueue);
        var status = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var activity = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        station.ActivityLogged += value =>
        {
            if (value.Contains("HTTP 401", StringComparison.Ordinal))
            {
                activity.TrySetResult(value);
            }
        };
        station.StatusChanged += value =>
        {
            if (value.Contains("rejected", StringComparison.Ordinal))
            {
                status.TrySetResult(value);
            }
        };

        await station.SaveAsync(CreateSettings());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var message = await status.Task.WaitAsync(timeout.Token);

        Assert.Contains("API key", message);
        Assert.Contains("HTTP 401", message);
        Assert.DoesNotContain(Api.ApiKey, message);
        Assert.DoesNotContain("private backend response", message);

        var entry = await activity.Task.WaitAsync(timeout.Token);
        Assert.Contains("registration", entry, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Api.ApiKey, entry);
        Assert.DoesNotContain("private backend response", entry);
    }

    [Fact]
    public async Task ActivityRecordsListenerAndPrinterSharingChanges()
    {
        using var client = new HttpClient(new PrintBackendHandler(Guid.NewGuid()));
        using var backend = new DesktopPrintBackendClient(client);
        await using var station = new DesktopPrintStation(_directory, Api, backend, new CapturingSpooler(),
            (_, _, _, _, _, _, _, _) => Task.FromResult<IAsyncDisposable>(new NoopReceiver()),
            () => [new("adapter-id", "Ethernet", "192.168.1.51")]);
        var activity = new ConcurrentQueue<string>();
        station.ActivityLogged += activity.Enqueue;
        var settings = CreateSettings();

        await station.SaveAsync(settings);
        Assert.Contains(activity, entry => ContainsAll(entry, "listener", "started"));
        Assert.Single(activity, entry => ContainsAll(entry, "Label printer", "shared"));
        Assert.Single(activity, entry => entry == "Print sharing enabled.");

        await station.SaveAsync(settings);
        Assert.Single(activity, entry => ContainsAll(entry, "Label printer", "shared"));

        await station.SaveAsync(settings with { Enabled = false, Printers = [] });
        Assert.Contains(activity, entry => ContainsAll(entry, "listener", "stopped"));
        Assert.Contains(activity, entry => ContainsAll(entry, "Label printer", "unshared"));
        Assert.Single(activity, entry => entry == "Print sharing disabled.");
    }

    [Fact]
    public async Task IncompleteSettingsSaveAndActivityDescribesChangesWithoutConfigurationValues()
    {
        await using var station = new DesktopPrintStation(_directory, Api);
        var activity = new ConcurrentQueue<string>();
        station.ActivityLogged += activity.Enqueue;

        await station.SaveAsync(station.Settings with
        {
            Enabled = true,
            Name = "private fixture station",
            WorkshopId = "private fixture workshop",
            NetworkInterfaceId = "private fixture adapter",
            UseLocalDevelopmentApi = true,
            Printers = []
        });

        await using var reopened = new DesktopPrintStation(_directory, Api);
        Assert.True(reopened.Settings.Enabled);
        Assert.Empty(reopened.Settings.Printers);
        Assert.Contains(activity, entry => ContainsAll(entry, "settings", "saved", "workshop"));
        Assert.Contains(activity, entry => ContainsAll(entry, "settings", "saved", "API target"));
        Assert.DoesNotContain(activity, entry => entry.Contains("private fixture", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HeartbeatPublishesOnlyInstalledQueuesAndRecoversAfterAnEmptyRefresh()
    {
        var capture = new PrintBackendHandler(Guid.NewGuid());
        using var client = new HttpClient(capture);
        using var backend = new DesktopPrintBackendClient(client);
        InstalledPrintQueue[] installed = [new("OS-Label-Queue", "Labels")];
        await using var station = new DesktopPrintStation(_directory, Api, backend, new CapturingSpooler(),
            (_, _, _, _, _, _, _, _) => Task.FromResult<IAsyncDisposable>(new NoopReceiver()),
            () => [new("adapter-id", "Ethernet", "192.168.1.51")],
            installedPrinters: _ => Task.FromResult<IReadOnlyList<InstalledPrintQueue>>(Volatile.Read(ref installed)),
            heartbeatInterval: TimeSpan.FromMilliseconds(25));
        var noInstalledQueues = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        station.StatusChanged += value =>
        {
            if (value.Contains("No shared printers", StringComparison.Ordinal))
            {
                noInstalledQueues.TrySetResult();
            }
        };
        var settings = CreateSettings();
        await station.SaveAsync(settings with
        {
            Printers = [.. settings.Printers, new("label-b", "Other labels", "Second-Queue")]
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.Equal(["label-a"], await capture.RegisteredPrinters.Reader.ReadAsync(timeout.Token));

        Volatile.Write(ref installed, []);
        await noInstalledQueues.Task.WaitAsync(timeout.Token);
        while (capture.RegisteredPrinters.Reader.TryRead(out var previous))
        {
            Assert.Equal(["label-a"], previous);
        }
        Assert.Equal(2, station.Settings.Printers.Length);

        Volatile.Write(ref installed, [new("Second-Queue", "Other labels")]);
        Assert.Equal(["label-b"], await capture.RegisteredPrinters.Reader.ReadAsync(timeout.Token));
    }

    [Fact]
    public async Task HttpsRequestReachesBackendAndReturnsAcceptedAfterSpoolSubmission()
    {
        var jobId = Guid.NewGuid();
        var capture = new PrintBackendHandler(jobId);
        using var backendHttp = new HttpClient(capture);
        using var backend = new DesktopPrintBackendClient(backendHttp);
        var spooler = new CapturingSpooler();
        Uri? receiverAddress = null;
        string? thumbprint = null;
        await using var station = new DesktopPrintStation(_directory, Api, backend, spooler,
            async (_, _, certificate, _, origins, _, queue, _) =>
            {
                var receiver = await LanPrintReceiver.StartLoopbackForTestingAsync(certificate, origins, queue);
                receiverAddress = receiver.BoundAddress;
                thumbprint = certificate.Thumbprint;
                return receiver;
            }, () => [new("adapter-id", "Ethernet", "192.168.1.51")], installedPrinters: ReadInstalledLabelQueue);
        var activity = new ConcurrentQueue<string>();
        station.ActivityLogged += activity.Enqueue;
        await station.SaveAsync(CreateSettings());

        using var http = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                certificate?.Thumbprint == thumbprint && thumbprint is not null
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(receiverAddress!, "/v1/print"))
        {
            Content = JsonContent.Create(new { ticket = "fixture-ticket" })
        };
        request.Headers.Add("Origin", "https://procutsuite.com");
        using var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("{\"status\":\"queued\"}", await response.Content.ReadAsStringAsync());
        Assert.Equal(("OS-Label-Queue", jobId), spooler.Accepted);
        Assert.Equal(station.StationId, capture.RedeemedStation);
        Assert.Equal("fixture-ticket", capture.RedeemedTicket);
        Assert.Contains(activity, entry => ContainsAll(entry, "queued", "Label printer", jobId.ToString()));
        Assert.DoesNotContain(activity, entry => ContainsAll(entry, "fixture-ticket"));
    }

    [Fact]
    public async Task RedemptionUsesRegisteredMappingAndOnlyAcknowledgesAnAcceptedSpoolJob()
    {
        var jobId = Guid.NewGuid();
        var capture = new PrintBackendHandler(jobId);
        using var client = new HttpClient(capture);
        using var backend = new DesktopPrintBackendClient(client);
        var spooler = new CapturingSpooler();
        InstalledPrintQueue[] installed = [new("OS-Label-Queue", "Labels")];
        var failDiscovery = false;
        Func<string, CancellationToken, Task<bool>>? queueTicket = null;
        await using var station = new DesktopPrintStation(_directory, Api, backend, spooler,
            (_, _, _, _, _, _, callback, _) =>
            {
                queueTicket = callback;
                return Task.FromResult<IAsyncDisposable>(new NoopReceiver());
            }, () => [new("adapter-id", "Ethernet", "192.168.1.51")],
            installedPrinters: _ => Volatile.Read(ref failDiscovery)
                ? Task.FromException<IReadOnlyList<InstalledPrintQueue>>(new IOException("private fixture spooler error"))
                : Task.FromResult<IReadOnlyList<InstalledPrintQueue>>(Volatile.Read(ref installed)));
        var activity = new ConcurrentQueue<string>();
        station.ActivityLogged += activity.Enqueue;
        await station.SaveAsync(CreateSettings());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await capture.Registered.Task.WaitAsync(timeout.Token);

        Assert.NotNull(queueTicket);
        Assert.True(await queueTicket("fixture-ticket", timeout.Token));
        Assert.Equal(("OS-Label-Queue", jobId), spooler.Accepted);
        Assert.Equal(station.StationId, capture.RedeemedStation);
        Assert.Equal("fixture-ticket", capture.RedeemedTicket);

        capture.PrinterId = "unknown";
        Assert.False(await queueTicket("other-ticket", timeout.Token));
        Assert.Equal(1, spooler.Attempts);
        Assert.Contains(activity, entry => ContainsAll(entry, jobId.ToString(), "shared"));

        capture.PrinterId = "label-a";
        Volatile.Write(ref installed, []);
        Assert.False(await queueTicket("missing-queue-ticket", timeout.Token));
        Assert.Equal(1, spooler.Attempts);
        Assert.Contains(activity, entry => ContainsAll(entry, jobId.ToString(), "Label printer", "no longer installed"));

        Volatile.Write(ref installed, [new("OS-Label-Queue", "Labels")]);
        Volatile.Write(ref failDiscovery, true);
        Assert.False(await queueTicket("discovery-error-ticket", timeout.Token));
        Assert.Equal(1, spooler.Attempts);
        Assert.Contains(activity, entry => ContainsAll(entry, jobId.ToString(), "Could not check installed print queues"));
        Assert.DoesNotContain(activity, entry => ContainsAll(entry, "private fixture spooler error"));

        Volatile.Write(ref failDiscovery, false);
        spooler.Fail = true;
        Assert.False(await queueTicket("third-ticket", timeout.Token));
        Assert.Equal(2, spooler.Attempts);
        Assert.Contains(activity, entry => ContainsAll(entry, jobId.ToString(), "Label printer", "queue", "failed"));

        capture.RedemptionStatus = HttpStatusCode.Conflict;
        Assert.False(await queueTicket("rejected-ticket", timeout.Token));
        Assert.Equal(2, spooler.Attempts);
        Assert.Contains(activity, entry => ContainsAll(entry, "HTTP 409"));
        Assert.DoesNotContain(activity, entry => entry.Contains("-ticket", StringComparison.Ordinal) ||
            entry.Contains(Api.ApiKey, StringComparison.Ordinal) || entry.Contains("private backend response", StringComparison.Ordinal));
    }

    private static bool ContainsAll(string entry, params string[] words) =>
        words.All(word => entry.Contains(word, StringComparison.OrdinalIgnoreCase));

    private static Task<IReadOnlyList<InstalledPrintQueue>> ReadInstalledLabelQueue(CancellationToken _) =>
        Task.FromResult<IReadOnlyList<InstalledPrintQueue>>([new("OS-Label-Queue", "Labels")]);

    private DesktopPrintStationSettings CreateSettings()
    {
        var certificatePath = Path.Combine(_directory, "certificate.pem");
        var privateKeyPath = Path.Combine(_directory, "private-key.pem");
        using var key = ECDsa.Create();
        var request = new CertificateRequest("CN=labels.example.com", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("labels.example.com");
        request.CertificateExtensions.Add(names.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        File.WriteAllText(certificatePath, certificate.ExportCertificatePem());
        File.WriteAllText(privateKeyPath, key.ExportPkcs8PrivateKeyPem());
        return new()
        {
            Enabled = true, UseManagedCertificate = false, WorkshopId = "12", Name = "Label bench",
            Origin = "https://labels.example.com:7443", NetworkInterfaceId = "adapter-id",
            CertificatePath = certificatePath, PrivateKeyPath = privateKeyPath,
            Printers = [new("label-a", "Label printer", "OS-Label-Queue")]
        };
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class NoopReceiver : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CapturingSpooler : IPrintSpooler
    {
        public (string Queue, Guid JobId) Accepted { get; private set; }
        public int Attempts { get; private set; }
        public bool Fail { get; set; }

        public Task QueuePdfAsync(string queue, ReadOnlyMemory<byte> pdf, Guid jobId, CancellationToken cancellationToken)
        {
            Attempts++;
            if (Fail)
            {
                throw new IOException("Spool rejected the job.");
            }

            Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(pdf.Span));
            Accepted = (queue, jobId);
            return Task.CompletedTask;
        }
    }

    private sealed class MemorySecretStore : ISecretStore
    {
        public Dictionary<string, string> Entries { get; } = [];
        public string? GetSecret(string key) => Entries.GetValueOrDefault(key);
        public void SetSecret(string key, string secret) => Entries[key] = secret;
        public void DeleteSecret(string key) => Entries.Remove(key);
    }

    private sealed class PendingPublicCertificateHandler : HttpMessageHandler
    {
        public System.Threading.Channels.Channel<(string Address, string Csr)> Requests { get; } =
            System.Threading.Channels.Channel.CreateUnbounded<(string, string)>();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var segments = request.RequestUri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal("certificate", segments[^1]);
            var host = $"s-{Guid.Parse(segments[^2]):N}.print.example.com";
            var origin = $"https://{host}:8443";
            if (request.Method == HttpMethod.Get)
            {
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { hostname = host, origin }) };
            }

            Assert.Equal(HttpMethod.Post, request.Method);
            var payload = await request.Content!.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken);
            var address = payload.GetProperty("lan_address").GetString()!;
            var csr = payload.GetProperty("csr_pem").GetString()!;
            var signingRequest = CertificateRequest.LoadSigningRequestPem(csr, HashAlgorithmName.SHA256,
                CertificateRequestLoadOptions.UnsafeLoadCertificateExtensions, RSASignaturePadding.Pkcs1);
            var names = new X509SubjectAlternativeNameExtension(
                signingRequest.CertificateExtensions.Single(extension => extension.Oid?.Value == "2.5.29.17").RawData);
            Assert.Equal(host, Assert.Single(names.EnumerateDnsNames()));
            Requests.Writer.TryWrite((address, csr));
            return new(HttpStatusCode.Accepted) { Content = JsonContent.Create(new { state = "pending", hostname = host, origin }) };
        }
    }

    private sealed class WorkshopHandler(Func<HttpRequestMessage, HttpStatusCode> handle) : HttpMessageHandler
    {
        public WorkshopHandler(Action<HttpRequestMessage> verify) : this(request =>
        {
            verify(request);
            return HttpStatusCode.OK;
        })
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(handle(request))
            {
                Content = JsonContent.Create(new { workshops = new[] { new { id = 12, name = "Workshop" } } })
            });
        }
    }

    private sealed class PrintBackendHandler(Guid jobId) : HttpMessageHandler
    {
        public System.Threading.Channels.Channel<string> RegisteredOrigins { get; } =
            System.Threading.Channels.Channel.CreateUnbounded<string>();
        public System.Threading.Channels.Channel<string> Registrations { get; } =
            System.Threading.Channels.Channel.CreateUnbounded<string>();
        public System.Threading.Channels.Channel<string[]> RegisteredPrinters { get; } =
            System.Threading.Channels.Channel.CreateUnbounded<string[]>();
        public TaskCompletionSource Registered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Guid RedeemedStation { get; private set; }
        public string? RedeemedTicket { get; private set; }
        public string PrinterId { get; set; } = "label-a";
        public HttpStatusCode RegistrationStatus { get; init; } = HttpStatusCode.OK;
        public HttpStatusCode RedemptionStatus { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(Api.ApiKey, request.Headers.Authorization?.Parameter);
            if (request.Method == HttpMethod.Put)
            {
                Registered.TrySetResult();
                Registrations.Writer.TryWrite(request.RequestUri!.AbsolutePath);
                var station = await request.Content!.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken);
                RegisteredOrigins.Writer.TryWrite(station.GetProperty("origin").GetString()!);
                RegisteredPrinters.Writer.TryWrite(station.GetProperty("printers").EnumerateArray()
                    .Select(printer => printer.GetProperty("id").GetString()!).ToArray());
                return new(RegistrationStatus) { Content = new StringContent("private backend response") };
            }

            var payload = await request.Content!.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken);
            RedeemedStation = payload.GetProperty("station_id").GetGuid();
            RedeemedTicket = payload.GetProperty("ticket").GetString();
            if (RedemptionStatus != HttpStatusCode.OK)
            {
                return new(RedemptionStatus) { Content = new StringContent("private backend response") };
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("%PDF-1.7\nfixture\n%%EOF\n"u8.ToArray())
            };
            response.Content.Headers.ContentType = new("application/pdf");
            response.Headers.Add("X-PCS-Printer-ID", PrinterId);
            response.Headers.Add("X-PCS-Job-ID", jobId.ToString());
            return response;
        }
    }
}
