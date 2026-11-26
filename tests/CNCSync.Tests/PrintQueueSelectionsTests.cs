using CNCSync.Core.Configuration;

namespace CNCSync.Tests;

public sealed class PrintQueueSelectionsTests
{
    private static readonly Guid StationId = Guid.Parse("83449cf6-acd2-483e-9f2a-1471d2d19c66");

    [Fact]
    public void DiscoveredPrintersAreUnsharedAndUseTheirInstalledNamesByDefault()
    {
        var installed = new[] { new InstalledPrintQueue("TSC_DA210", "TSC DA210"), new InstalledPrintQueue("laser", "Office Laser") };
        var choices = PrintQueueSelections.Merge(StationId, installed, []);

        Assert.Empty(PrintQueueSelections.ToMappings(choices));
        var selected = Assert.Single(PrintQueueSelections.ToMappings([choices[0] with { IsShared = true }, choices[1]]));
        Assert.Equal("TSC DA210", selected.Name);
        Assert.Equal("TSC_DA210", selected.Queue);
        Assert.True(Guid.TryParse(selected.Id, out _));
        Assert.Equal(choices[0].Id, PrintQueueSelections.Merge(StationId, installed, [])[0].Id);
    }

    [Fact]
    public void SavedSharesKeepTheirIdsAndAliasesWhileOtherPrintersStayUnshared()
    {
        var choices = PrintQueueSelections.Merge(StationId,
            [new("LASER", "Office Laser"), new("new-queue", "New printer")],
            [new("old-id", "Shipping labels", "laser"), new("second-id", "Assembly labels", "laser")],
            ignoreQueueCase: true);

        Assert.Equal(3, choices.Count);
        var shared = PrintQueueSelections.ToMappings(choices);
        Assert.Equal(["old-id", "second-id"], shared.Select(printer => printer.Id));
        Assert.Equal(["Shipping labels", "Assembly labels"], shared.Select(printer => printer.Name));
        Assert.All(shared, printer => Assert.Equal("LASER", printer.Queue));
    }

    [Fact]
    public void RefreshKeepsDraftChangesAndPreviouslySharedUnavailablePrinters()
    {
        DesktopPrintQueueMapping[] saved = [new("existing", "Saved label printer", "offline")];
        var original = PrintQueueSelections.Merge(StationId, [new("laser", "Laser")], saved);
        var checkedLaser = original[0] with { IsShared = true, DisplayName = "Front office" };
        var uncheckedOffline = original[1] with { IsShared = false };

        var refreshed = PrintQueueSelections.Merge(StationId, [new("laser", "Laser (USB)")], saved,
            [checkedLaser, uncheckedOffline]);

        Assert.Equal(checkedLaser with { InstalledName = "Laser (USB)" }, refreshed[0]);
        Assert.Equal(uncheckedOffline, refreshed[1]);
        Assert.False(refreshed[1].IsAvailable);
        Assert.Equal("Front office", Assert.Single(PrintQueueSelections.ToMappings(refreshed)).Name);
    }

    [Fact]
    public void DefaultNameTracksTheInstalledNameAfterReload()
    {
        DesktopPrintQueueMapping[] saved = [new("existing", "Old default name", "laser") { DisplayName = string.Empty }];
        var choices = PrintQueueSelections.Merge(StationId, [new("laser", "New default name")], saved);

        Assert.Equal("New default name", Assert.Single(PrintQueueSelections.ToMappings(choices)).Name);
        Assert.Equal("Old default name", Assert.Single(PrintQueueSelections.ToMappings(
            PrintQueueSelections.Merge(StationId, [], saved))).Name);
    }

    [Fact]
    public void ConnectionAndLocationAreDiscoveredForTheChecklistAndStayOutOfSharedSettings()
    {
        var printer = new InstalledPrintQueue("tsc", "TSC DA210")
        {
            Connection = PrintQueueConnection.Usb,
            Location = "Label bench"
        };
        var initial = Assert.Single(PrintQueueSelections.Merge(StationId, [printer], []));
        var saved = PrintQueueSelections.ToMappings([initial with { IsShared = true }]);
        var refreshed = Assert.Single(PrintQueueSelections.Merge(StationId,
            [printer with { Connection = PrintQueueConnection.Network, Location = null }], saved));

        Assert.Equal(PrintQueueConnection.Usb, initial.Connection);
        Assert.Equal("Label bench", initial.Location);
        Assert.Equal(PrintQueueConnection.Network, refreshed.Connection);
        Assert.Null(refreshed.Location);
        Assert.Equal("TSC DA210", Assert.Single(saved).Name);
    }
}
