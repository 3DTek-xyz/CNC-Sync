using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using CNCSync.Core.Configuration;
using CNCSync.Infrastructure.DesktopPrinting;

namespace CNCSync.Tests;

public sealed class ManagedStationCertificateAuthorityTests : IDisposable
{
    private static readonly IPAddress InitialAddress = IPAddress.Parse("192.168.86.214");
    private static readonly IPAddress ChangedAddress = IPAddress.Parse("10.1.2.3");
    private readonly string _directory = Directory.CreateTempSubdirectory("pcs-ca-test-").FullName;
    private readonly Guid _stationId = Guid.NewGuid();
    private readonly TestSecretStore _secrets = new();

    [Fact]
    public void InitializationIsLazyAndOnlyPublicRootIsExported()
    {
        var authority = CreateAuthority();

        Assert.Null(authority.ExportRootCertificate());
        Assert.Empty(_secrets.Values);

        using var server = authority.LoadOrIssue(InitialAddress);
        using var root = ReadRoot(authority);

        Assert.False(root.HasPrivateKey);
        Assert.True(server.Leaf.HasPrivateKey);
        var issuer = Assert.Single(server.Chain.Cast<X509Certificate2>());
        Assert.False(issuer.HasPrivateKey);
        var credentials = Assert.Single(_secrets.Values.Values, value => value.Contains("IssuerPrivateKey", StringComparison.Ordinal));
        using var parsed = JsonDocument.Parse(credentials);
        Assert.DoesNotContain(parsed.RootElement.EnumerateObject(), property =>
            property.Name.Contains("RootPrivate", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(root.RawData, File.ReadAllBytes(Assert.Single(Directory.GetFiles(_directory))));
    }

    [Fact]
    public void RestartReusesLeafAndIpChangeKeepsRoot()
    {
        var original = CreateAuthority();
        using var first = original.LoadOrIssue(InitialAddress);

        var reopened = CreateAuthority();
        using var root = ReadRoot(reopened);
        using var same = reopened.LoadOrIssue(InitialAddress);
        using var changed = reopened.LoadOrIssue(ChangedAddress);

        Assert.Equal(first.Leaf.RawData, same.Leaf.RawData);
        Assert.NotEqual(first.Leaf.Thumbprint, changed.Leaf.Thumbprint);
        Assert.Equal(root.RawData, reopened.ExportRootCertificate());
        Assert.True(changed.Leaf.MatchesHostname(ChangedAddress.ToString(), allowCommonName: false));
        Assert.False(changed.Leaf.MatchesHostname(InitialAddress.ToString(), allowCommonName: false));
        Assert.True(BuildChain(changed.Leaf, Assert.Single(changed.Chain.Cast<X509Certificate2>()), root));
    }

    [Fact]
    public void LeafRenewsWithoutChangingTheTrustedRoot()
    {
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var authority = CreateAuthority(clock);
        using var first = authority.LoadOrIssue(InitialAddress);
        using var root = ReadRoot(authority);

        clock.UtcNow += TimeSpan.FromDays(80);
        using var renewed = authority.LoadOrIssue(InitialAddress);

        Assert.NotEqual(first.Leaf.Thumbprint, renewed.Leaf.Thumbprint);
        Assert.True(renewed.Leaf.NotAfter > first.Leaf.NotAfter);
        Assert.Equal(root.RawData, authority.ExportRootCertificate());
    }

    [Fact]
    public async Task HttpsReceiverSuppliesTheChainToAClientWithOnlyThePublicRoot()
    {
        const string browserOrigin = "https://procut.example";
        const string ticket = "test-print-ticket";
        var authority = CreateAuthority();
        using var server = authority.LoadOrIssue(InitialAddress);
        using var root = ReadRoot(authority);
        await using var receiver = await LanPrintReceiver.StartLoopbackForTestingAsync(server.Leaf, [browserOrigin],
            (value, _) => Task.FromResult(value == ticket), certificateChain: server.Chain);
        var trust = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
            DisableCertificateDownloads = true
        };
        trust.CustomTrustStore.Add(root);
        using var client = new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false,
            SslOptions = new SslClientAuthenticationOptions { CertificateChainPolicy = trust },
            ConnectCallback = async (_, token) =>
            {
                var connection = new TcpClient(AddressFamily.InterNetwork);
                try
                {
                    await connection.ConnectAsync(IPAddress.Loopback, receiver.BoundAddress.Port, token);
                    return connection.GetStream();
                }
                catch
                {
                    connection.Dispose();
                    throw;
                }
            }
        }) { Timeout = TimeSpan.FromSeconds(10) };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{InitialAddress}:{receiver.BoundAddress.Port}/v1/print")
        {
            Content = JsonContent.Create(new { ticket })
        };
        request.Headers.Add("Origin", browserOrigin);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("8.8.8.8")]
    [InlineData("169.254.10.2")]
    [InlineData("172.32.0.1")]
    [InlineData("::1")]
    [InlineData("fd00::1234")]
    public void RejectsAddressesOutsidePrivateIpv4(string target)
    {
        var authority = CreateAuthority();

        Assert.Throws<ArgumentException>(() => authority.LoadOrIssue(IPAddress.Parse(target)));
        Assert.Null(authority.ExportRootCertificate());
    }

    [Fact]
    public void APreviouslyPublishedRootCannotBeSilentlyReplacedWhenCredentialsDisappear()
    {
        var authority = CreateAuthority();
        using var leaf = authority.LoadOrIssue(InitialAddress);
        var pinned = authority.ExportRootCertificate();
        _secrets.Values.Clear();

        Assert.Throws<ManagedStationCertificateException>(() => CreateAuthority().LoadOrIssue(InitialAddress));
        Assert.Throws<ManagedStationCertificateException>(authority.ExportRootCertificate);
        Assert.Equal(pinned, File.ReadAllBytes(Assert.Single(Directory.GetFiles(_directory))));
    }

    [Fact]
    public void MissingPublicCopyIsRecoveredFromProtectedStorageWithoutChangingTrust()
    {
        var authority = CreateAuthority();
        using var first = authority.LoadOrIssue(InitialAddress);
        var pinned = authority.ExportRootCertificate();
        File.Delete(Assert.Single(Directory.GetFiles(_directory)));

        Assert.Null(authority.ExportRootCertificate());
        using var restored = CreateAuthority().LoadOrIssue(InitialAddress);

        Assert.Equal(pinned, authority.ExportRootCertificate());
        Assert.Equal(first.Leaf.Thumbprint, restored.Leaf.Thumbprint);
    }

    [Fact]
    public void AModifiedPublicCopyCannotBeExportedOrSilentlyChanged()
    {
        var authority = CreateAuthority();
        using var server = authority.LoadOrIssue(InitialAddress);
        var file = Assert.Single(Directory.GetFiles(_directory));
        File.WriteAllBytes(file, server.Leaf.RawData);

        Assert.Throws<ManagedStationCertificateException>(authority.ExportRootCertificate);
        Assert.Throws<ManagedStationCertificateException>(() => authority.LoadOrIssue(InitialAddress));
        Assert.Equal(server.Leaf.RawData, File.ReadAllBytes(file));
    }

    [Fact]
    public async Task ConcurrentFirstStartsUseTheSameStationAuthority()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string> StartAsync() => Task.Run(async () =>
        {
            await ready.Task;
            using var server = CreateAuthority().LoadOrIssue(InitialAddress);
            return server.Leaf.Thumbprint;
        });

        var first = StartAsync();
        var second = StartAsync();
        ready.SetResult();
        var leaves = await Task.WhenAll(first, second);

        Assert.Equal(leaves[0], leaves[1]);
        Assert.NotNull(CreateAuthority().ExportRootCertificate());
    }

    [Fact]
    public void CorruptServerCredentialsAreReplacedWithoutChangingTrust()
    {
        var authority = CreateAuthority();
        using var first = authority.LoadOrIssue(InitialAddress);
        var pinned = authority.ExportRootCertificate();
        var serverKey = Assert.Single(_secrets.Values.Keys, name => name.EndsWith(":server", StringComparison.Ordinal));
        _secrets.Values[serverKey] = """{"Certificate":""";

        using var recovered = authority.LoadOrIssue(InitialAddress);

        Assert.NotEqual(first.Leaf.Thumbprint, recovered.Leaf.Thumbprint);
        Assert.Equal(pinned, authority.ExportRootCertificate());
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("pcs-print-station.invalid")]
    [InlineData("8.8.8.8")]
    [InlineData("fd00::1234")]
    public void TheIssuingCaCannotValidateDnsNamesOrPublicIps(string name)
    {
        var authority = CreateAuthority();
        using var server = authority.LoadOrIssue(InitialAddress);
        using var root = ReadRoot(authority);
        var issuer = Assert.Single(server.Chain.Cast<X509Certificate2>());
        var credentials = Assert.Single(_secrets.Values.Values, value => value.Contains("IssuerPrivateKey", StringComparison.Ordinal));
        using var material = JsonDocument.Parse(credentials);
        using var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(material.RootElement.GetProperty("IssuerPrivateKey").GetBytesFromBase64(), out _);
        using var signer = issuer.CopyWithPrivateKey(key);

        Assert.True(BuildChain(server.Leaf, issuer, root));
        AssertRejected(signer, root, name);
    }

    private ManagedStationCertificateAuthority CreateAuthority(TimeProvider? clock = null) =>
        new(_directory, _stationId, _secrets, clock);

    private static X509Certificate2 ReadRoot(ManagedStationCertificateAuthority authority) =>
        X509CertificateLoader.LoadCertificate(Assert.IsType<byte[]>(authority.ExportRootCertificate()));

    private static bool BuildChain(X509Certificate2 certificate, X509Certificate2 issuer, X509Certificate2 root)
    {
        using var chain = new X509Chain
        {
            ChainPolicy = { TrustMode = X509ChainTrustMode.CustomRootTrust, RevocationMode = X509RevocationMode.NoCheck }
        };
        chain.ChainPolicy.CustomTrustStore.Add(root);
        chain.ChainPolicy.ExtraStore.Add(issuer);
        chain.ChainPolicy.DisableCertificateDownloads = true;
        return chain.Build(certificate);
    }

    private static void AssertRejected(X509Certificate2 issuer, X509Certificate2 root, string name)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
        var names = new SubjectAlternativeNameBuilder();
        if (IPAddress.TryParse(name, out var address))
        {
            names.AddIpAddress(address);
        }
        else
        {
            names.AddDnsName(name);
        }
        request.CertificateExtensions.Add(names.Build(critical: true));
        using var certificate = request.Create(issuer, DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1), RandomNumberGenerator.GetBytes(16));

        Assert.False(BuildChain(certificate, issuer, root));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class TestSecretStore : ISecretStore
    {
        public ConcurrentDictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public string? GetSecret(string key) => Values.GetValueOrDefault(key);

        public void SetSecret(string key, string secret) => Values[key] = secret;

        public void DeleteSecret(string key) => Values.TryRemove(key, out _);
    }

    private sealed class TestClock(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
