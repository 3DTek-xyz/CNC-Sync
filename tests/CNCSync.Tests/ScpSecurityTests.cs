using CNCSync.Core.Configuration;
using CNCSync.Infrastructure.Networking;

namespace CNCSync.Tests;

public sealed class ScpSecurityTests
{
    [Fact]
    public void BuildUploadCommand_UsesShellQuotingForPosixRemotePaths()
    {
        Assert.Equal("scp -t -d '/jobs/$(command) `command` $HOME'\"'\"'s'", ScpUploadProtocol.BuildCommand("/jobs/$(command) `command` $HOME's"));
    }

    [Fact]
    public void BuildScpUploadPath_PrefixesOptionLikeRelativePaths()
    {
        Assert.Equal("./-S/path.nc", ScpService.BuildScpUploadPath(".", "-S/path.nc"));
        Assert.Equal("/jobs/a'b.nc", ScpService.BuildScpUploadPath("/jobs", "a'b.nc"));
    }

    [Theory]
    [InlineData("part\nname.nc")]
    [InlineData("part\rname.nc")]
    [InlineData("part\0name.nc")]
    public void BuildScpUploadPath_RejectsProtocolControlCharacters(string relativePath)
    {
        Assert.Throws<ArgumentException>(() => ScpService.BuildScpUploadPath("/jobs", relativePath));
    }
}
