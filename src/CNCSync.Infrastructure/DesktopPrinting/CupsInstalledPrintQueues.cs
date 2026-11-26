using System.Runtime.InteropServices;
using CNCSync.Core.Configuration;

namespace CNCSync.Infrastructure.DesktopPrinting;

internal static class CupsInstalledPrintQueues
{
    private const uint DiscoveredPrinterMask = 0x01000000;
    private const uint DestinationError = 0x08;
    private const uint DestinationRemoved = 0x04;
    private const int IppClientError = 0x0400;
    private const int IppNotFound = 0x0406;

    internal static List<InstalledPrintQueue> Read(CancellationToken cancellationToken)
    {
        var libraryName = OperatingSystem.IsMacOS() ? "/usr/lib/libcups.2.dylib" : "libcups.so.2";
        if (!NativeLibrary.TryLoad(libraryName, out var library))
        {
            throw new IOException("Could not load CUPS to list installed printers.");
        }

        try
        {
            return ReadLibrary(library, cancellationToken);
        }
        catch (EntryPointNotFoundException)
        {
            throw new IOException("The installed CUPS library does not support printer discovery.");
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    private static List<InstalledPrintQueue> ReadLibrary(IntPtr library, CancellationToken cancellationToken)
    {
        var enumerate = Resolve<CupsEnumDests>(library, "cupsEnumDests");
        var option = Resolve<CupsGetOption>(library, "cupsGetOption");
        var lastError = Resolve<CupsLastError>(library, "cupsLastError");
        var printers = new List<InstalledPrintQueue>();
        var callbackFailed = false;
        var completed = enumerate(0, 0, IntPtr.Zero, 0, DiscoveredPrinterMask, OnDestination, IntPtr.Zero);
        cancellationToken.ThrowIfCancellationRequested();
        var code = lastError();
        if (completed == 0 || callbackFailed || (printers.Count == 0 && code >= IppClientError && code != IppNotFound))
        {
            throw new IOException("Could not list installed printers from CUPS.");
        }

        return printers;

        int OnDestination(IntPtr _, uint flags, IntPtr address)
        {
            callbackFailed |= (flags & DestinationError) != 0;
            if (cancellationToken.IsCancellationRequested || callbackFailed)
            {
                return 0;
            }

            if (address != IntPtr.Zero && (flags & DestinationRemoved) == 0)
            {
                var destination = Marshal.PtrToStructure<CupsDestination>(address);
                // CUPS can also report user-specific presets for each destination.
                if (destination.Instance == IntPtr.Zero && Marshal.PtrToStringUTF8(destination.Name) is { } queue)
                {
                    var name = Marshal.PtrToStringUTF8(option("printer-info", destination.OptionCount, destination.Options));
                    var uri = Marshal.PtrToStringUTF8(option("device-uri", destination.OptionCount, destination.Options));
                    var location = Marshal.PtrToStringUTF8(option("printer-location", destination.OptionCount, destination.Options));
                    printers.Add(new InstalledPrintQueue(queue, name ?? queue)
                    {
                        Connection = InstalledPrintQueues.ClassifyDeviceUri(uri),
                        Location = location
                    });
                }
            }

            return 1;
        }
    }

    private static T Resolve<T>(IntPtr library, string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CupsDestination
    {
        public readonly IntPtr Name;
        public readonly IntPtr Instance;
        public readonly int IsDefault;
        public readonly int OptionCount;
        public readonly IntPtr Options;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CupsEnumDests(uint flags, int milliseconds, IntPtr cancel, uint type, uint mask,
        CupsDestCallback callback, IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CupsDestCallback(IntPtr context, uint flags, IntPtr destination);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr CupsGetOption([MarshalAs(UnmanagedType.LPUTF8Str)] string name, int count, IntPtr options);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CupsLastError();
}
