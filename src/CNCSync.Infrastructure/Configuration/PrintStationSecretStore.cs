using CNCSync.Core.Configuration;

namespace CNCSync.Infrastructure.Configuration;

internal static class PrintStationSecretStore
{
    public static ISecretStore Create(string settingsDirectory)
    {
        if (OperatingSystem.IsMacOS())
        {
            return new MacPrintStationSecretStore();
        }

        if (OperatingSystem.IsWindows())
        {
            return new WindowsDpapiSecretStore(settingsDirectory);
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxPrintStationSecretStore();
        }

        throw new PlatformNotSupportedException("Automatic print certificates require protected key storage.");
    }
}
