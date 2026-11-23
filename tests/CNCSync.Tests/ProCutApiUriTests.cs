using CNCSync.Core.Configuration;

namespace CNCSync.Tests;

public sealed class ProCutApiUriTests
{
    [Theory]
    [InlineData("/process", "https://api.example.test/process")]
    [InlineData("process?q=1", "https://api.example.test/base/process?q=1")]
    [InlineData("https://API.example.test:443/process", "https://api.example.test/process")]
    public void TryResolveEndpoint_AcceptsHttpsEndpointsOnConfiguredOrigin(string endpoint, string expected)
    {
        Assert.True(ProCutApiUri.TryResolveEndpoint("https://api.example.test/base/", endpoint, out var uri));
        Assert.Equal(new Uri(expected), uri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("//api.example.test/process")]
    [InlineData("/\\other.example.test/process")]
    [InlineData("https:/other.example.test/process")]
    [InlineData("https://@api.example.test/process")]
    [InlineData("https://api.example.test@other.example.test/process")]
    [InlineData("https://api.example.test:444/process")]
    [InlineData("http://api.example.test/process")]
    [InlineData("/process#fragment")]
    [InlineData("/pro\ncess")]
    public void TryResolveEndpoint_RejectsUntrustedOrAmbiguousEndpoints(string endpoint)
    {
        Assert.False(ProCutApiUri.TryResolveEndpoint("https://api.example.test", endpoint, out var uri));
        Assert.Null(uri);
    }

    [Fact]
    public void HasSameOrigin_RecognizesIdnAndPunycodeAsSameHost()
    {
        Assert.True(ProCutApiUri.HasSameOrigin("https://b\u00fccher.example.test", "https://xn--bcher-kva.example.test:443/base"));
    }
}
