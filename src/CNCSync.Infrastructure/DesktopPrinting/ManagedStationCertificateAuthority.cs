using System.Formats.Asn1;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using CNCSync.Core.Configuration;

namespace CNCSync.Infrastructure.DesktopPrinting;

internal sealed class ManagedStationCertificateException(string message) : InvalidOperationException(message)
{
}

internal sealed class ManagedStationCertificateAuthority(
    string settingsDirectory, Guid stationId, ISecretStore secretStore, TimeProvider? clock = null)
{
    internal static readonly TimeSpan RenewalWindow = TimeSpan.FromDays(14);
    private static readonly TimeSpan LeafLifetime = TimeSpan.FromDays(90);
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan IssuanceLockTimeout = TimeSpan.FromSeconds(5);
    private const string ServerAuthentication = "1.3.6.1.5.5.7.3.1";
    private const string UnusableDnsName = "pcs-print-station.invalid";
    private const int MaxRootCertificateSize = 16 * 1024;
    private readonly string _rootPath = Path.Combine(settingsDirectory, $"desktop-print-ca-{stationId:N}.cer");
    private readonly string _authoritySecret = $"print-station-ca:{stationId:N}:issuer";
    private readonly string _serverSecret = $"print-station-ca:{stationId:N}:server";

    internal DesktopPrintCertificate LoadOrIssue(IPAddress address)
    {
        if (!IsPrivateIpv4(address))
        {
            throw new ArgumentException("The print station certificate requires a private IPv4 address.", nameof(address));
        }

        try
        {
            using var mutex = new Mutex(false, $"PCS-Print-Station-CA-{stationId:N}",
                new NamedWaitHandleOptions { CurrentSessionOnly = false });
            TakeIssuanceLock(mutex);
            try
            {
                var now = (clock ?? TimeProvider.System).GetUtcNow();
                using var authority = LoadOrCreateAuthority(now);
                return LoadOrIssueServer(authority, address, now);
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
            CryptographicException or ArgumentException or WaitHandleCannotBeOpenedException)
        {
            throw new ManagedStationCertificateException("The print station certificate or its protected key storage could not be used.");
        }
    }

    private static void TakeIssuanceLock(Mutex mutex)
    {
        try
        {
            if (!mutex.WaitOne(IssuanceLockTimeout))
            {
                throw new ManagedStationCertificateException(
                    "Print station certificate setup is already in progress. Try again shortly.");
            }
        }
        catch (AbandonedMutexException)
        {
            // The OS grants ownership when the previous holder exits. Read the pinned and protected records afresh.
        }
    }

    internal byte[]? ExportRootCertificate()
    {
        var root = ReadPublishedRoot();
        if (root is null)
        {
            return null;
        }

        try
        {
            var stored = secretStore.GetSecret(_authoritySecret);
            var material = stored is null ? null : JsonSerializer.Deserialize<PublicAuthorityMaterial>(stored);
            if (material is null || !root.AsSpan().SequenceEqual(material.RootCertificate))
            {
                throw new ManagedStationCertificateException(
                    "The public print station CA certificate does not match the certificate in protected storage.");
            }

            return root;
        }
        catch (JsonException)
        {
            throw new ManagedStationCertificateException("The print station CA could not be read from protected storage.");
        }
    }

    private byte[]? ReadPublishedRoot()
    {
        try
        {
            using var file = new FileStream(_rootPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (file.Length is <= 0 or > MaxRootCertificateSize)
            {
                throw new CryptographicException();
            }

            var bytes = new byte[(int)file.Length];
            file.ReadExactly(bytes);
            using var root = X509CertificateLoader.LoadCertificate(bytes);
            return root.RawData;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            throw new ManagedStationCertificateException("The public print station CA certificate could not be read.");
        }
    }

    private Authority LoadOrCreateAuthority(DateTimeOffset now)
    {
        var pinnedRoot = ReadPublishedRoot();
        var stored = secretStore.GetSecret(_authoritySecret);
        if (stored is null && (pinnedRoot is not null || secretStore.GetSecret(_serverSecret) is not null))
        {
            throw new ManagedStationCertificateException("The print station CA key is missing from protected storage. Restore it before sharing printers.");
        }

        var material = stored is null
            ? CreateAuthority(now)
            : JsonSerializer.Deserialize<AuthorityMaterial>(stored) ?? throw new CryptographicException();
        Authority? authority = null;
        try
        {
            if (material.RootCertificate is not { Length: > 0 and <= MaxRootCertificateSize } ||
                material.IssuerCertificate is not { Length: > 0 and <= MaxRootCertificateSize } ||
                material.IssuerPrivateKey is not { Length: > 0 })
            {
                throw new CryptographicException();
            }

            if (pinnedRoot is not null && !pinnedRoot.AsSpan().SequenceEqual(material.RootCertificate))
            {
                throw new ManagedStationCertificateException("The print station CA key does not match its exported certificate.");
            }

            authority = OpenAuthority(material, now);
            if (stored is null)
            {
                secretStore.SetSecret(_authoritySecret, JsonSerializer.Serialize(material));
            }

            if (pinnedRoot is null)
            {
                PublishRoot(material.RootCertificate);
            }

            return authority;
        }
        catch
        {
            authority?.Dispose();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material.IssuerPrivateKey.AsSpan());
        }
    }

    private AuthorityMaterial CreateAuthority(DateTimeOffset now)
    {
        // The root private key only exists while the restricted issuing CA is being created.
        using var rootKey = NewKey();
        var rootRequest = CreateRequest($"PCS Print Station CA {stationId:N}", rootKey);
        AddAuthorityExtensions(rootRequest, pathLength: 1);
        using var root = rootRequest.CreateSelfSigned(now - ClockSkew, now.AddYears(6));

        using var issuerKey = NewKey();
        var issuerRequest = CreateRequest($"PCS Print Station Issuer {stationId:N}", issuerKey);
        AddAuthorityExtensions(issuerRequest, pathLength: 0);
        issuerRequest.CertificateExtensions.Add(CreateIpNameConstraints());
        issuerRequest.CertificateExtensions.Add(CreateServerAuthenticationExtension());
        issuerRequest.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(root, true, false));
        using var issuer = issuerRequest.Create(root, now - ClockSkew, now.AddYears(5), NewSerialNumber());
        return new AuthorityMaterial(root.RawData, issuer.RawData, issuerKey.ExportPkcs8PrivateKey());
    }

    private static Authority OpenAuthority(AuthorityMaterial material, DateTimeOffset now)
    {
        var root = X509CertificateLoader.LoadCertificate(material.RootCertificate);
        try
        {
            using var issuerCertificate = X509CertificateLoader.LoadCertificate(material.IssuerCertificate);
            if (issuerCertificate.NotAfter.ToUniversalTime() <= (now + RenewalWindow).UtcDateTime)
            {
                throw new ManagedStationCertificateException("The print station CA is reaching the end of its life. Set up a new station CA before sharing printers.");
            }

            var constraints = issuerCertificate.Extensions["2.5.29.30"];
            if (constraints is null || !constraints.Critical ||
                !constraints.RawData.AsSpan().SequenceEqual(CreateIpNameConstraints().RawData) ||
                !BuildChain(issuerCertificate, root, issuer: null, now))
            {
                throw new CryptographicException();
            }

            using var issuerKey = ImportKey(material.IssuerPrivateKey);
            return new Authority(root, issuerCertificate.CopyWithPrivateKey(issuerKey));
        }
        catch
        {
            root.Dispose();
            throw;
        }
    }

    private DesktopPrintCertificate LoadOrIssueServer(Authority authority, IPAddress address, DateTimeOffset now)
    {
        if (TryLoadServer(authority, address, now) is { } saved)
        {
            return saved;
        }

        using var serverKey = NewKey();
        var request = CreateRequest("PCS Print Station", serverKey);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(CreateServerAuthenticationExtension());
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(authority.Issuer, true, false));
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(address);
        request.CertificateExtensions.Add(names.Build(critical: true));

        var issuerExpiration = new DateTimeOffset(authority.Issuer.NotAfter.ToUniversalTime());
        var desiredExpiration = now + LeafLifetime;
        var actualExpiration = desiredExpiration < issuerExpiration ? desiredExpiration : issuerExpiration;
        using var certificate = request.Create(authority.Issuer, now - ClockSkew, actualExpiration, NewSerialNumber());
        if (!BuildChain(certificate, authority.Root, authority.Issuer, now))
        {
            throw new CryptographicException();
        }

        var exportedKey = serverKey.ExportPkcs8PrivateKey();
        try
        {
            secretStore.SetSecret(_serverSecret, JsonSerializer.Serialize(new ServerMaterial(certificate.RawData, exportedKey)));
            return CreateServerBundle(certificate, serverKey, authority.Issuer);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(exportedKey);
        }
    }

    private DesktopPrintCertificate? TryLoadServer(Authority authority, IPAddress address, DateTimeOffset now)
    {
        ServerMaterial? material = null;
        try
        {
            var stored = secretStore.GetSecret(_serverSecret);
            material = stored is null ? null : JsonSerializer.Deserialize<ServerMaterial>(stored);
            if (material?.Certificate is not { Length: > 0 and <= MaxRootCertificateSize } ||
                material.PrivateKey is not { Length: > 0 })
            {
                return null;
            }

            using var saved = X509CertificateLoader.LoadCertificate(material.Certificate);
            if (saved.NotBefore.ToUniversalTime() > now.UtcDateTime ||
                saved.NotAfter.ToUniversalTime() <= (now + RenewalWindow).UtcDateTime ||
                !saved.MatchesHostname(address.ToString(), allowWildcards: false, allowCommonName: false) ||
                !BuildChain(saved, authority.Root, authority.Issuer, now))
            {
                return null;
            }

            using var key = ImportKey(material.PrivateKey);
            return CreateServerBundle(saved, key, authority.Issuer);
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException or ArgumentException)
        {
            // The validated CA can replace corrupt or incomplete server credentials without changing client trust.
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory((material?.PrivateKey).AsSpan());
        }
    }

    private static DesktopPrintCertificate CreateServerBundle(
        X509Certificate2 certificate, ECDsa key, X509Certificate2 issuer)
    {
        var leaf = certificate.CopyWithPrivateKey(key);
        X509Certificate2? intermediate = null;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var pfx = leaf.Export(X509ContentType.Pkcs12);
                try
                {
                    var imported = X509CertificateLoader.LoadPkcs12(pfx, string.Empty, X509KeyStorageFlags.DefaultKeySet);
                    leaf.Dispose();
                    leaf = imported;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(pfx);
                }
            }

            intermediate = X509CertificateLoader.LoadCertificate(issuer.RawData);
            return new DesktopPrintCertificate(leaf, new X509Certificate2Collection(intermediate));
        }
        catch
        {
            leaf.Dispose();
            intermediate?.Dispose();
            throw;
        }
    }

    private void PublishRoot(byte[] bytes)
    {
        Directory.CreateDirectory(settingsDirectory);
        var temporary = _rootPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }

            File.Move(temporary, _rootPath);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static bool BuildChain(X509Certificate2 certificate, X509Certificate2 root, X509Certificate2? issuer, DateTimeOffset now)
    {
        using var chain = new X509Chain
        {
            ChainPolicy =
            {
                TrustMode = X509ChainTrustMode.CustomRootTrust,
                RevocationMode = X509RevocationMode.NoCheck,
                DisableCertificateDownloads = true,
                VerificationTime = now.UtcDateTime
            }
        };
        chain.ChainPolicy.CustomTrustStore.Add(root);
        if (issuer is not null)
        {
            chain.ChainPolicy.ExtraStore.Add(issuer);
        }

        return chain.Build(certificate);
    }

    private static CertificateRequest CreateRequest(string name, ECDsa key)
    {
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        return request;
    }

    private static void AddAuthorityExtensions(CertificateRequest request, int pathLength)
    {
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, pathLength, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
    }

    private static X509EnhancedKeyUsageExtension CreateServerAuthenticationExtension() =>
        new(new OidCollection { new Oid(ServerAuthentication) }, false);

    private static X509Extension CreateIpNameConstraints()
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)))
            {
                WriteDnsSubtree(writer, UnusableDnsName);
                WriteIpSubtree(writer, [10, 0, 0, 0, 255, 0, 0, 0]);
                WriteIpSubtree(writer, [172, 16, 0, 0, 255, 240, 0, 0]);
                WriteIpSubtree(writer, [192, 168, 0, 0, 255, 255, 0, 0]);
            }

            using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1, true)))
            {
                // The only permitted DNS subtree is also excluded, so the issuer can only certify private IP addresses.
                WriteDnsSubtree(writer, UnusableDnsName);
                WriteIpSubtree(writer, new byte[32]); // Exclude all IPv6.
            }
        }

        return new X509Extension("2.5.29.30", writer.Encode(), critical: true);
    }

    private static void WriteDnsSubtree(AsnWriter writer, string hostname)
    {
        using (writer.PushSequence())
        {
            writer.WriteCharacterString(UniversalTagNumber.IA5String, hostname, new Asn1Tag(TagClass.ContextSpecific, 2));
        }
    }

    private static void WriteIpSubtree(AsnWriter writer, ReadOnlySpan<byte> addressAndMask)
    {
        using (writer.PushSequence())
        {
            writer.WriteOctetString(addressAndMask, new Asn1Tag(TagClass.ContextSpecific, 7));
        }
    }

    private static bool IsPrivateIpv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
            bytes[0] == 192 && bytes[1] == 168;
    }

    private static ECDsa NewKey() => ECDsa.Create(ECCurve.NamedCurves.nistP256);

    private static ECDsa ImportKey(byte[] privateKey)
    {
        var key = ECDsa.Create();
        try
        {
            key.ImportPkcs8PrivateKey(privateKey, out var bytesRead);
            if (bytesRead != privateKey.Length)
            {
                throw new CryptographicException();
            }

            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    private static byte[] NewSerialNumber()
    {
        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7f;
        serial[0] |= 0x01;
        return serial;
    }

    private sealed record AuthorityMaterial(byte[] RootCertificate, byte[] IssuerCertificate, byte[] IssuerPrivateKey);

    private sealed record PublicAuthorityMaterial(byte[] RootCertificate);

    private sealed record ServerMaterial(byte[] Certificate, byte[] PrivateKey);

    private sealed class Authority(X509Certificate2 root, X509Certificate2 issuer) : IDisposable
    {
        public X509Certificate2 Root { get; } = root;

        public X509Certificate2 Issuer { get; } = issuer;

        public void Dispose()
        {
            Issuer.Dispose();
            Root.Dispose();
        }
    }
}
