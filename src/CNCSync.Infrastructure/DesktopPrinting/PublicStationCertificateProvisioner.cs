using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using CNCSync.Core.Configuration;

namespace CNCSync.Infrastructure.DesktopPrinting;

internal sealed record ProvisionedPrintCertificate(string Origin, DesktopPrintCertificate Certificate);

internal sealed class PublicStationCertificateProvisioner(
    ISecretStore store, DesktopPrintBackendClient backend, Guid stationId)
{
    public static readonly TimeSpan RenewalWindow = TimeSpan.FromDays(14);

    public async Task<ProvisionedPrintCertificate?> ResolveAsync(
        DesktopPrintBackendAccess access, string workshopId, IPAddress address, CancellationToken token)
    {
        var target = await backend.DiscoverPublicCertificateAsync(access, workshopId, stationId, token).ConfigureAwait(false);
        var scope = $"{access.CertificateRealm}|{target.Hostname}";
        var keyAccount = "print-public-key:" + HashScope(scope);
        var certificateAccount = "print-public-certificate:" + HashScope($"{scope}|{workshopId}");
        using var key = RSA.Create(2048);
        LoadOrSaveKey(key, keyAccount);

        var cached = LoadCachedCertificate(certificateAccount, target.Hostname, address, key);
        try
        {
            var csr = CreateSigningRequest(key, target.Hostname);
            var pem = await backend.RequestPublicCertificateAsync(access, workshopId, stationId, target, address, csr, token)
                .ConfigureAwait(false);
            if (pem is null)
            {
                var current = cached;
                cached = null;
                return current is null ? null : new(target.Origin, current);
            }

            return SaveCertificate(certificateAccount, target, address, pem, key);
        }
        finally
        {
            cached?.Dispose();
        }
    }

    private static string HashScope(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private ProvisionedPrintCertificate SaveCertificate(string account, PublicPrintCertificateTarget target,
        IPAddress address, string pem, RSA key)
    {
        var issued = LoadCertificate(pem, target.Hostname, key);
        try
        {
            store.SetSecret(account, JsonSerializer.Serialize(new CachedCertificate(address.ToString(), pem)));
            return new(target.Origin, issued);
        }
        catch
        {
            issued.Dispose();
            throw;
        }
    }

    private void LoadOrSaveKey(RSA key, string keyAccount)
    {
        if (store.GetSecret(keyAccount) is { } pem)
        {
            key.ImportFromPem(pem);
        }
        else
        {
            store.SetSecret(keyAccount, key.ExportPkcs8PrivateKeyPem());
        }
    }

    private DesktopPrintCertificate? LoadCachedCertificate(string account, string hostname, IPAddress address, RSA key)
    {
        if (store.GetSecret(account) is not { Length: <= 40 * 1024 } value)
        {
            return null;
        }

        try
        {
            var cached = JsonSerializer.Deserialize<CachedCertificate>(value);
            return cached?.Address == address.ToString() && cached.CertificatePem is not null
                ? LoadCertificate(cached.CertificatePem, hostname, key) : null;
        }
        catch (Exception exception) when (exception is JsonException or CryptographicException or ArgumentException)
        {
            return null;
        }
    }

    internal static string CreateSigningRequest(RSA key, string hostname)
    {
        var useCommonName = hostname.Length <= 64;
        var subject = useCommonName
            ? new X500DistinguishedName($"CN={hostname}")
            : new X500DistinguishedName(new byte[] { 0x30, 0x00 }); // Empty DER-encoded distinguished name.
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(hostname);
        request.CertificateExtensions.Add(names.Build(critical: !useCommonName));
        return request.CreateSigningRequestPem();
    }

    internal static DesktopPrintCertificate LoadCertificate(string pem, string hostname, RSA key,
        Action<X509ChainPolicy>? configureTrust = null)
    {
        var supplied = new X509Certificate2Collection();
        var intermediates = new X509Certificate2Collection();
        X509Certificate2? leaf = null;
        try
        {
            supplied.ImportFromPem(pem);
            if (supplied.Count is < 1 or > 6 || !MatchesStationName(supplied[0], hostname) ||
                supplied[0].Extensions["2.5.29.19"] is X509BasicConstraintsExtension { CertificateAuthority: true })
            {
                throw new CryptographicException("The issued HTTPS certificate did not match this station.");
            }

            using var verifier = new X509Chain();
            verifier.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
            verifier.ChainPolicy.DisableCertificateDownloads = true;
            verifier.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            verifier.ChainPolicy.ExtraStore.AddRange(supplied);
            configureTrust?.Invoke(verifier.ChainPolicy);
            if (!verifier.Build(supplied[0]))
            {
                throw new CryptographicException("The issued HTTPS certificate chain is not trusted or is out of date.");
            }

            leaf = supplied[0].CopyWithPrivateKey(key);
            for (var index = 1; index < verifier.ChainElements.Count - 1; index++)
            {
                intermediates.Add(X509CertificateLoader.LoadCertificate(verifier.ChainElements[index].Certificate.RawData));
            }

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

            return new DesktopPrintCertificate(leaf, intermediates);
        }
        catch
        {
            leaf?.Dispose();
            foreach (var item in intermediates)
            {
                item.Dispose();
            }

            throw;
        }
        finally
        {
            foreach (var item in supplied)
            {
                item.Dispose();
            }
        }
    }

    private static bool MatchesStationName(X509Certificate2 certificate, string hostname)
    {
        if (certificate.Extensions["2.5.29.17"] is not X509SubjectAlternativeNameExtension names)
        {
            return false;
        }

        return names.EnumerateDnsNames().SequenceEqual([hostname], StringComparer.OrdinalIgnoreCase)
            && !names.EnumerateIPAddresses().Any();
    }

    private sealed record CachedCertificate(string Address, string CertificatePem);
}
