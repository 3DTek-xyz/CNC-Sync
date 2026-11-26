using CNCSync.Infrastructure.Configuration;

namespace CNCSync.Tests;

public sealed class PrintStationSecretStoreTests
{
    [Fact]
    public void WindowsStoreReplacesProtectedSecretAndCleansUpTemporaryFile()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Directory.CreateTempSubdirectory("pcs-print-dpapi-");
        try
        {
            var store = new WindowsDpapiSecretStore(directory.FullName);
            store.SetSecret("station-key", "previous-key-material");
            store.SetSecret("station-key", "replacement-key-material");

            Assert.Equal("replacement-key-material", store.GetSecret("station-key"));
            var protectedFile = Assert.Single(directory.GetFiles("*", SearchOption.AllDirectories));
            Assert.Equal(".bin", protectedFile.Extension);
            Assert.DoesNotContain("replacement-key-material", File.ReadAllText(protectedFile.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void LinuxStore_PassesSecretOnStandardInputOnly()
    {
        var store = new LinuxPrintStationSecretStore((arguments, input) =>
        {
            Assert.Equal(
                ["store", "--label=ProCut Suite Desktop Print Sharing", "application", "procutsuite-desktop-print", "account", "certificate-key-id"],
                arguments);
            Assert.Equal("intermediate-key-material", input);
            return new(0, string.Empty, string.Empty);
        });

        store.SetSecret("certificate-key-id", "intermediate-key-material");
    }

    [Fact]
    public void LinuxStore_ReadsWithoutIncludingSecretInArguments()
    {
        var store = new LinuxPrintStationSecretStore((arguments, input) =>
        {
            Assert.Equal(["lookup", "application", "procutsuite-desktop-print", "account", "station-key"], arguments);
            Assert.Null(input);
            return new(0, "certificate-content\n", string.Empty);
        });

        Assert.Equal("certificate-content", store.GetSecret("station-key"));
    }

    [Fact]
    public void LinuxStore_DistinguishesMissingEntryFromServiceFailure()
    {
        var notFound = new LinuxPrintStationSecretStore((_, _) => new(1, string.Empty, string.Empty));
        var unavailable = new LinuxPrintStationSecretStore((_, _) => new(1, string.Empty, "private diagnostic"));

        Assert.Null(notFound.GetSecret("station-key"));
        Assert.DoesNotContain("private diagnostic",
            Assert.Throws<InvalidOperationException>(() => unavailable.GetSecret("station-key")).Message);
    }

    [Fact]
    public void LinuxStore_ReportsWriteFailureWithoutEchoingToolOutput()
    {
        var store = new LinuxPrintStationSecretStore((_, _) => new(1, "private diagnostic", "private diagnostic"));

        Assert.DoesNotContain("private diagnostic",
            Assert.Throws<InvalidOperationException>(() => store.SetSecret("station-key", "certificate-content")).Message);
    }

    [Fact]
    public void LinuxStore_DeletesByAccountAndTreatsAnAbsentEntryAsSuccess()
    {
        var store = new LinuxPrintStationSecretStore((arguments, input) =>
        {
            Assert.Equal(["clear", "application", "procutsuite-desktop-print", "account", "station-key"], arguments);
            Assert.Null(input);
            return new(1, string.Empty, string.Empty);
        });

        store.DeleteSecret("station-key");
    }
}
