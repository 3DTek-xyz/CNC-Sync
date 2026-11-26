using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CNCSync.Core.Configuration;

namespace CNCSync.Infrastructure.DesktopPrinting;

internal static class DesktopPrintStationConfiguration
{
    public static DesktopPrintStationSettings Copy(DesktopPrintStationSettings settings) => settings with
    {
        BrowserOrigins = settings.BrowserOrigins?.ToArray() ?? [],
        Printers = settings.Printers?.ToArray() ?? []
    };

    internal const string LocalDevelopmentBrowserOrigin = "http://localhost:5173";

    public static DesktopPrintStationSettings Validate(DesktopPrintStationSettings settings, bool useBuiltInSpooler = true)
    {
        settings = Copy(settings);
        if (!settings.Enabled)
        {
            return settings;
        }

        if (!IsText(settings.NetworkInterfaceId, 256))
        {
            throw new ArgumentException("Select the network adapter connected to the workshop LAN.");
        }

        if (!settings.UsePublicCertificate && !settings.UseManagedCertificate)
        {
            settings = ValidateManualCertificate(settings);
        }

        if (!DesktopPrintBackendClient.IsWorkshopId(settings.WorkshopId))
        {
            throw new ArgumentException("Select a workshop to start print sharing.");
        }

        var name = string.IsNullOrWhiteSpace(settings.Name) ? DesktopPrintStation.DefaultName : settings.Name;
        if (!IsText(name, DesktopPrintBackendClient.MaxRegistrationNameLength))
        {
            throw new ArgumentException("Enter a station name of at most 100 characters.");
        }

        if (useBuiltInSpooler && OperatingSystem.IsWindows() &&
            !WindowsPdfSpooler.IsConfiguredExecutable(settings.WindowsPdfRendererPath))
        {
            throw new ArgumentException(WindowsPdfSpooler.RendererRequiredMessage);
        }

        if (settings.BrowserOrigins.Length > 16 ||
            (settings.BrowserOrigins.Length == 0 && !settings.AllowLocalDevelopmentOrigin))
        {
            throw new ArgumentException("Enter between 1 and 16 permitted browser origins.");
        }

        if (settings.Printers.Length == 0)
        {
            throw new ArgumentException("Select at least one printer to start print sharing.");
        }

        if (settings.Printers.Length > DesktopPrintBackendClient.MaxRegistrationPrinters)
        {
            throw new ArgumentException($"Select up to {DesktopPrintBackendClient.MaxRegistrationPrinters} printers to share.");
        }

        if (settings.Printers.Any(printer => printer is null || !DesktopPrintBackendClient.IsPrinterId(printer.Id) ||
                !IsText(printer.Name, DesktopPrintBackendClient.MaxRegistrationNameLength) || !IsQueue(printer.Queue)) ||
            settings.Printers.Select(printer => printer.Id).Distinct(StringComparer.Ordinal).Count() != settings.Printers.Length)
        {
            throw new ArgumentException("Check the selected printers and their display names.");
        }

        var browserOrigins = settings.BrowserOrigins.Select(value =>
            settings.AllowLocalDevelopmentOrigin && value == LocalDevelopmentBrowserOrigin
                ? value : GetOrigin(value).GetLeftPart(UriPartial.Authority));
        if (settings.AllowLocalDevelopmentOrigin)
        {
            browserOrigins = browserOrigins.Append(LocalDevelopmentBrowserOrigin);
        }

        var allowedOrigins = browserOrigins.Distinct(StringComparer.Ordinal).ToArray();
        if (allowedOrigins.Length > 16)
        {
            throw new ArgumentException("Enter up to 16 permitted website origins including the local development website.");
        }

        return settings with { Name = name, BrowserOrigins = allowedOrigins };
    }

    private static DesktopPrintStationSettings ValidateManualCertificate(DesktopPrintStationSettings settings)
    {
        var origin = GetOrigin(settings.Origin);
        if (origin.HostNameType != UriHostNameType.Dns || origin.IsLoopback ||
            !origin.IdnHost.Contains('.') || origin.IdnHost.EndsWith('.') || origin.Port == 0 || settings.Origin.Length > 300)
        {
            throw new ArgumentException("Enter a stable HTTPS hostname that resolves to this station on the workshop LAN.");
        }

        if (string.IsNullOrWhiteSpace(settings.CertificatePath) || string.IsNullOrWhiteSpace(settings.PrivateKeyPath) ||
            !Path.IsPathFullyQualified(settings.CertificatePath) || !Path.IsPathFullyQualified(settings.PrivateKeyPath))
        {
            throw new ArgumentException("Select the HTTPS certificate and private key files.");
        }

        return settings with { Origin = origin.GetLeftPart(UriPartial.Authority) };
    }

    internal static Uri GetOrigin(string value)
    {
        if (value is null || !ProCutApiUri.TryGetBaseUri(value, out var uri) || uri.AbsolutePath != "/")
        {
            throw new ArgumentException("Enter HTTPS origins with a hostname and optional port.");
        }

        // Reject paths that Uri may have normalized to "/".
        var original = value.Trim().AsSpan("https://".Length);
        var slash = original.IndexOf('/');
        if (slash >= 0 && slash != original.Length - 1)
        {
            throw new ArgumentException("Enter HTTPS origins with a hostname and optional port.");
        }

        return new UriBuilder(uri.Scheme, uri.IdnHost, uri.IsDefaultPort ? -1 : uri.Port).Uri;
    }

    private static bool IsText(string? value, int limit) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= limit && !value.Any(char.IsControl);

    private static bool IsQueue(string? value) => IsText(value, 256) && value![0] != '-' && value.Trim() == value;
}

internal sealed class DesktopPrintCertificate : IDisposable
{
    internal DesktopPrintCertificate(X509Certificate2 leaf, X509Certificate2Collection chain)
    {
        Leaf = leaf;
        Chain = chain;
    }

    public X509Certificate2 Leaf { get; }
    public X509Certificate2Collection Chain { get; }

    public static DesktopPrintCertificate Load(DesktopPrintStationSettings settings)
    {
        X509Certificate2? leaf = null;
        var chain = new X509Certificate2Collection();
        try
        {
            leaf = X509Certificate2.CreateFromPemFile(settings.CertificatePath, settings.PrivateKeyPath);
            var host = new Uri(settings.Origin).IdnHost;
            var now = DateTime.UtcNow;
            if (!leaf.MatchesHostname(host, allowWildcards: true, allowCommonName: false) ||
                now < leaf.NotBefore.ToUniversalTime() || now > leaf.NotAfter.ToUniversalTime())
            {
                throw new ArgumentException("The HTTPS certificate must be current and match the station hostname.");
            }

            chain.ImportFromPemFile(settings.CertificatePath);
            foreach (var item in chain.Cast<X509Certificate2>().ToArray())
            {
                if (item.Thumbprint == leaf.Thumbprint ||
                    item.SubjectName.RawData.AsSpan().SequenceEqual(item.IssuerName.RawData))
                {
                    chain.Remove(item);
                    item.Dispose();
                }
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

            return new DesktopPrintCertificate(leaf, chain);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            leaf?.Dispose();
            DisposeChain(chain);
            throw new ArgumentException("Could not use the HTTPS certificate and key. Check the files, hostname and certificate expiry.");
        }
    }

    public void Dispose()
    {
        Leaf.Dispose();
        DisposeChain(Chain);
    }

    private static void DisposeChain(X509Certificate2Collection chain)
    {
        foreach (var item in chain)
        {
            item.Dispose();
        }
    }
}
