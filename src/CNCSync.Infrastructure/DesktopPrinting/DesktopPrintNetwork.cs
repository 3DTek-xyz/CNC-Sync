using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CNCSync.Infrastructure.DesktopPrinting;

public sealed record DesktopPrintNetworkInterface(string Id, string Name, string Address)
{
    internal int PrefixLength { get; init; }
}

internal static class DesktopPrintNetwork
{
    public static IReadOnlyList<DesktopPrintNetworkInterface> GetInterfaces()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
                .Select(adapter => new
                {
                    adapter.Id,
                    adapter.Name,
                    Address = adapter.GetIPProperties().UnicastAddresses
                        .Where(address => IsPrivateIPv4(address.Address) && address.PrefixLength is > 0 and < 32)
                        .OrderBy(address => address.Address.ToString(), StringComparer.Ordinal)
                        .FirstOrDefault()
                })
                .Where(adapter => adapter.Address is not null)
                .Select(adapter => new DesktopPrintNetworkInterface(adapter.Id, adapter.Name,
                    adapter.Address!.Address.ToString()) { PrefixLength = adapter.Address.PrefixLength })
                .OrderBy(adapter => adapter.Name, StringComparer.CurrentCulture)
                .ToArray();
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    internal static bool IsPrivateIPv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
            (bytes[0] == 192 && bytes[1] == 168);
    }
}
