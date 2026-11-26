using System.Net;
using CNCSync.Core.Configuration;

namespace CNCSync.Infrastructure.DesktopPrinting;

public static class InstalledPrintQueues
{
    private const int MaxLocationLength = 120;

    public static Task<IReadOnlyList<InstalledPrintQueue>> GetInstalledAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var queues = ReadInstalledQueues(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return Normalize(queues);
        }, cancellationToken);

    private static IEnumerable<InstalledPrintQueue> ReadInstalledQueues(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsInstalledPrintQueues.Read(cancellationToken);
        }

        if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            return CupsInstalledPrintQueues.Read(cancellationToken);
        }

        throw new PlatformNotSupportedException("Installed printer discovery is unavailable on this operating system.");
    }

    internal static IReadOnlyList<InstalledPrintQueue> Normalize(IEnumerable<InstalledPrintQueue> queues) => queues
        .Where(queue => IsRoutableQueueName(queue.Queue))
        .DistinctBy(queue => queue.Queue, StringComparer.OrdinalIgnoreCase)
        .Select(queue => queue with { Name = DisplayName(queue), Location = DisplayLocation(queue.Location) })
        .OrderBy(queue => queue.Name, StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(queue => queue.Queue, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    internal static PrintQueueConnection ClassifyDeviceUri(string? deviceUri)
    {
        var schemeEnd = deviceUri?.IndexOf(':') ?? -1;
        if (schemeEnd <= 0)
        {
            return PrintQueueConnection.Unknown;
        }

        // Bonjour service names can contain escaped characters and DNS-SD labels that are not URI hostnames.
        return deviceUri![..schemeEnd].ToLowerInvariant() switch
        {
            "usb" or "ippusb" or "ipp-usb" => PrintQueueConnection.Usb,
            "bluetooth" => PrintQueueConnection.Bluetooth,
            "file" or "cups-pdf" => PrintQueueConnection.Virtual,
            "dnssd" or "mdns" => PrintQueueConnection.Network,
            "ipp" or "ipps" or "http" or "https" or "socket" or "lpd" or "smb"
                when Uri.TryCreate(deviceUri, UriKind.Absolute, out var uri) && uri.Host.Length > 0 && !uri.IsLoopback
                => PrintQueueConnection.Network,
            _ => PrintQueueConnection.Unknown
        };
    }

    internal static PrintQueueConnection ClassifyWindowsPorts(string? portNames)
    {
        var types = portNames?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(ClassifyWindowsPort).Distinct().ToArray();
        return types is [var type] ? type : PrintQueueConnection.Unknown;
    }

    private static PrintQueueConnection ClassifyWindowsPort(string port)
    {
        if (port.Length > 3 && port.StartsWith("USB", StringComparison.OrdinalIgnoreCase) &&
            port[3..].All(char.IsAsciiDigit))
        {
            return PrintQueueConnection.Usb;
        }

        if (port.ToUpperInvariant() is "FILE:" or "PORTPROMPT:" or "NUL:" or "XPSPORT:")
        {
            return PrintQueueConnection.Virtual;
        }

        if (port.StartsWith("WSD-", StringComparison.OrdinalIgnoreCase) ||
            port.StartsWith("IP_", StringComparison.OrdinalIgnoreCase) ||
            port.StartsWith(@"\\", StringComparison.Ordinal) ||
            ((port.Contains('.') || port.Contains(':')) && IPAddress.TryParse(port, out var ip) && !IPAddress.IsLoopback(ip)))
        {
            return PrintQueueConnection.Network;
        }

        return ClassifyDeviceUri(port);
    }

    private static bool IsRoutableQueueName(string queue) =>
        queue.Length is > 0 and <= 256 && queue[0] != '-' && queue == queue.Trim() && !queue.Any(char.IsControl);

    private static string DisplayName(InstalledPrintQueue queue)
    {
        var title = queue.Name.Trim();
        var name = title.Length > 0 && !title.Any(char.IsControl) ? title : queue.Queue;
        if (!name.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return name;
        }

        var shareSeparator = name.LastIndexOf('\\');
        return shareSeparator > 2 && shareSeparator < name.Length - 1
            ? name[(shareSeparator + 1)..]
            : "Network printer";
    }

    private static string? DisplayLocation(string? value)
    {
        var location = value?.Trim();
        return location is { Length: > 0 and <= MaxLocationLength } && !location.Any(char.IsControl) &&
            !location.Contains("://", StringComparison.Ordinal) && !location.StartsWith(@"\\", StringComparison.Ordinal)
                ? location
                : null;
    }
}
