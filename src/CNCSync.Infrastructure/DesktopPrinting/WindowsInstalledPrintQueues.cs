using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CNCSync.Core.Configuration;

namespace CNCSync.Infrastructure.DesktopPrinting;

[SupportedOSPlatform("windows")]
internal static class WindowsInstalledPrintQueues
{
    private const uint LocalPrinters = 0x00000002;
    private const uint ConnectedPrinters = 0x00000004;
    private const uint CachedPrinterNamesLevel = 4;
    private const uint LocalPrinterPortsLevel = 5;
    private const uint NetworkPrinterAttribute = 0x00000010;
    private const int InsufficientBuffer = 122;
    private const uint MaxBufferBytes = 4 * 1024 * 1024;

    internal static List<InstalledPrintQueue> Read(CancellationToken cancellationToken)
    {
        var printers = ReadPrinterList<PrinterInfo4>(LocalPrinters | ConnectedPrinters, CachedPrinterNamesLevel,
            ReadCachedPrinter, cancellationToken);
        if (printers.All(printer => printer.Connection != PrintQueueConnection.Unknown))
        {
            return printers;
        }

        try
        {
            // Ask only for the local spooler's own configured ports. Remote connections come from the level 4 cache.
            var connections = ReadPrinterList<PrinterInfo5>(LocalPrinters, LocalPrinterPortsLevel,
                    ReadLocalPrinter, cancellationToken)
                .DistinctBy(printer => printer.Queue, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(printer => printer.Queue, printer => printer.Connection, StringComparer.OrdinalIgnoreCase);
            return printers.Select(printer => printer.Connection == PrintQueueConnection.Unknown &&
                connections.TryGetValue(printer.Queue, out var connection)
                    ? printer with { Connection = connection }
                    : printer).ToList();
        }
        catch (IOException)
        {
            return printers;
        }
    }

    private static List<InstalledPrintQueue> ReadPrinterList<TInfo>(uint flags, uint level,
        Func<TInfo, InstalledPrintQueue?> readPrinter, CancellationToken cancellationToken) where TInfo : struct
    {
        if (EnumPrinters(flags, IntPtr.Zero, level, IntPtr.Zero, 0, out var needed, out _))
        {
            return [];
        }

        var error = Marshal.GetLastPInvokeError();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (error != InsufficientBuffer || needed is 0 or > MaxBufferBytes)
            {
                break;
            }

            var allocated = needed;
            var buffer = Marshal.AllocHGlobal((int)allocated);
            try
            {
                if (EnumPrinters(flags, IntPtr.Zero, level, buffer, allocated, out needed, out var count))
                {
                    return ReadBuffer(buffer, allocated, count, readPrinter);
                }

                error = Marshal.GetLastPInvokeError();
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        throw new IOException("Could not list installed printers from the Windows Print Spooler.");
    }

    private static List<InstalledPrintQueue> ReadBuffer<TInfo>(IntPtr buffer, uint allocated, uint count,
        Func<TInfo, InstalledPrintQueue?> readPrinter) where TInfo : struct
    {
        var stride = Marshal.SizeOf<TInfo>();
        if (count > allocated / stride)
        {
            throw new IOException("The Windows Print Spooler returned invalid printer data.");
        }

        var printers = new List<InstalledPrintQueue>((int)count);
        for (var index = 0; index < count; index++)
        {
            var info = Marshal.PtrToStructure<TInfo>(IntPtr.Add(buffer, index * stride));
            if (readPrinter(info) is { } printer)
            {
                printers.Add(printer);
            }
        }

        return printers;
    }

    private static InstalledPrintQueue? ReadCachedPrinter(PrinterInfo4 info)
    {
        if (Marshal.PtrToStringUni(info.Name) is not { } name)
        {
            return null;
        }

        var network = (info.Attributes & NetworkPrinterAttribute) != 0 || name.StartsWith(@"\\", StringComparison.Ordinal);
        return new InstalledPrintQueue(name, name)
        {
            Connection = network ? PrintQueueConnection.Network : PrintQueueConnection.Unknown
        };
    }

    private static InstalledPrintQueue? ReadLocalPrinter(PrinterInfo5 info)
    {
        if (Marshal.PtrToStringUni(info.Name) is not { } name)
        {
            return null;
        }

        var ports = Marshal.PtrToStringUni(info.PortNames);
        return new InstalledPrintQueue(name, name) { Connection = InstalledPrintQueues.ClassifyWindowsPorts(ports) };
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PrinterInfo4
    {
        public readonly IntPtr Name;
        public readonly IntPtr ServerName;
        public readonly uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PrinterInfo5
    {
        public readonly IntPtr Name;
        public readonly IntPtr PortNames;
        public readonly uint Attributes;
        public readonly uint DeviceNotSelectedTimeout;
        public readonly uint TransmissionRetryTimeout;
    }

    [DllImport("winspool.drv", EntryPoint = "EnumPrintersW", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumPrinters(uint flags, IntPtr name, uint level, IntPtr printers, uint size,
        out uint needed, out uint returned);
}
