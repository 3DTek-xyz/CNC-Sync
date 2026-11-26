using CNCSync.Core.Configuration;
using CNCSync.Infrastructure.DesktopPrinting;

namespace CNCSync.Tests;

public sealed class InstalledPrintQueuesTests
{
    [Fact]
    public void KeepsExactQueuesWhenInstalledPrintersHaveTheSameDisplayName()
    {
        var results = InstalledPrintQueues.Normalize([
            new("TSC_DA210_2", "TSC DA210"),
            new("TSC_DA210", "TSC DA210"),
            new("tsc_da210", "TSC duplicate")
        ]);

        Assert.Equal([
            new InstalledPrintQueue("TSC_DA210", "TSC DA210"),
            new InstalledPrintQueue("TSC_DA210_2", "TSC DA210")
        ], results);
    }

    [Fact]
    public void UsesQueueNameWhenPrinterHasNoUsableDisplayName()
    {
        var results = InstalledPrintQueues.Normalize([
            new(@"\\workshop\Sheet Labels", " "),
            new("EPSON_ET_2750_Series", "  EPSON ET-2750 Series  "),
            new("-invalid", "Ignored"),
            new("queue\nname", "Ignored"),
            new("", "Ignored")
        ]);

        Assert.Contains(new InstalledPrintQueue(@"\\workshop\Sheet Labels", "Sheet Labels"), results);
        Assert.Contains(new InstalledPrintQueue("EPSON_ET_2750_Series", "EPSON ET-2750 Series"), results);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void HidesWindowsPrintServerInTheDefaultDisplayName()
    {
        const string route = @"\\workshop\Sheet Labels";
        var queue = Assert.Single(InstalledPrintQueues.Normalize([new(route, route)]));

        Assert.Equal(new InstalledPrintQueue(route, "Sheet Labels"), queue);
    }

    [Fact]
    public async Task CanceledEnumerationDoesNotCallTheSpooler()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => InstalledPrintQueues.GetInstalledAsync(new CancellationToken(canceled: true)));
    }

    [Theory]
    [InlineData("usb://TSC/DA210?serial=test", PrintQueueConnection.Usb)]
    [InlineData("dnssd://Workshop._ipps._tcp.local.", PrintQueueConnection.Network)]
    [InlineData("IPPS://local-user:password@printer.example/ipp/print", PrintQueueConnection.Network)]
    [InlineData("ipp://127.0.0.1:60000/ipp/print", PrintQueueConnection.Unknown)]
    [InlineData("bluetooth://device", PrintQueueConnection.Bluetooth)]
    [InlineData("cups-pdf:/", PrintQueueConnection.Virtual)]
    [InlineData("vendor://custom-port", PrintQueueConnection.Unknown)]
    [InlineData("ipp descripton: label", PrintQueueConnection.Unknown)]
    [InlineData(null, PrintQueueConnection.Unknown)]
    public void DeviceUriClassifiesTheQueueWithoutExposingAddresses(string? uri, PrintQueueConnection expected) =>
        Assert.Equal(expected, InstalledPrintQueues.ClassifyDeviceUri(uri));

    [Theory]
    [InlineData("USB001", PrintQueueConnection.Usb)]
    [InlineData("USB001, USB002", PrintQueueConnection.Usb)]
    [InlineData("USB001,IP_192.0.2.1", PrintQueueConnection.Unknown)]
    [InlineData("WSD-abcd123", PrintQueueConnection.Network)]
    [InlineData("IP_192.0.2.1", PrintQueueConnection.Network)]
    [InlineData("192.0.2.1", PrintQueueConnection.Network)]
    [InlineData(@"\\server\printer", PrintQueueConnection.Network)]
    [InlineData("PORTPROMPT:", PrintQueueConnection.Virtual)]
    [InlineData("COM3:", PrintQueueConnection.Unknown)]
    [InlineData("USBVendor", PrintQueueConnection.Unknown)]
    [InlineData(null, PrintQueueConnection.Unknown)]
    public void WindowsPortsClassifyKnownPortsWithoutInventingAPhysicalConnection(string? ports, PrintQueueConnection expected) =>
        Assert.Equal(expected, InstalledPrintQueues.ClassifyWindowsPorts(ports));

    [Theory]
    [InlineData("  Dispatch office  ", "Dispatch office")]
    [InlineData(" ", null)]
    [InlineData("Warehouse\nunchecked", null)]
    [InlineData("ipp://person:secret@print.example/queue", null)]
    [InlineData(@"\\server\printer", null)]
    public void TreatsLocationAsOptionalHumanReadableText(string location, string? expected) =>
        Assert.Equal(expected, Assert.Single(InstalledPrintQueues.Normalize([new("Queue", "Printer") { Location = location }])).Location);
}
