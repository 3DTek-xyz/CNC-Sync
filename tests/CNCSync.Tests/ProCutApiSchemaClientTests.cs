using System.Net;
using CNCSync.Core.Configuration;
using CNCSync.Infrastructure.ProCutApi;

namespace CNCSync.Tests;

public sealed class ProCutApiSchemaClientTests
{
    [Theory]
    [InlineData("https://other.example.test", false)]
    [InlineData("https://api.example.test:444", false)]
    [InlineData("http://api.example.test", false)]
    [InlineData("https://API.example.test:443", true)]
    public async Task GetServicesAsync_OnlySendsBoundKeyToItsOriginalOrigin(string editedBaseUrl, bool shouldSend)
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"services":[{"id":"valid","endpoint":"/process"}]}""")
        });
        using var http = new HttpClient(handler);
        var client = new ProCutApiSchemaClient(http);
        var settings = ProCutApiSettings.ForBoundKey(editedBaseUrl, "https://api.example.test", TestSecrets.ProCutApiKey);

        if (shouldSend)
        {
            await client.GetServicesAsync(settings);
            Assert.Equal(TestSecrets.ProCutApiKey, handler.Request?.Headers.Authorization?.Parameter);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetServicesAsync(settings));
            Assert.Null(handler.Request);
        }
    }
    [Fact]
    public async Task GetServicesAsync_RejectsRedirectRatherThanRetrying()
    {
        using var defaultHandler = ProCutApiSchemaClient.CreateDefaultHandler();
        Assert.False(defaultHandler.AllowAutoRedirect);

        var requests = 0;
        using var http = new HttpClient(new CapturingHandler(_ =>
        {
            requests++;
            return new HttpResponseMessage(HttpStatusCode.TemporaryRedirect)
            {
                Headers = { Location = new Uri("https://other.example.test/schema") }
            };
        }));
        var client = new ProCutApiSchemaClient(http);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetServicesAsync(new ProCutApiSettings
        {
            BaseUrl = "https://api.example.test",
            ApiKey = TestSecrets.ProCutApiKey
        }));

        Assert.Contains("307", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, requests);
    }

    [Theory]
    [InlineData("http://127.0.0.1:54321")]
    [InlineData("http://api.example.test")]
    [InlineData("https://user@api.example.test")]
    [InlineData("https://api.example.test/#fragment")]
    public async Task GetServicesAsync_RejectsInvalidBaseUrlBeforeSending(string baseUrl)
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        using var http = new HttpClient(handler);
        var client = new ProCutApiSchemaClient(http);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetServicesAsync(new ProCutApiSettings
        {
            BaseUrl = baseUrl,
            ApiKey = TestSecrets.ProCutApiKey
        }));

        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task GetServicesAsync_OmitsEndpointsOutsideConfiguredHttpsOrigin()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                { "services": [
                    { "id": "valid", "endpoint": "/process" },
                    { "id": "different_host", "endpoint": "//other.example.test/process" },
                    { "id": "different_port", "endpoint": "https://api.example.test:444/process" },
                    { "id": "insecure", "endpoint": "http://api.example.test/process" }
                ] }
                """)
        });
        using var http = new HttpClient(handler);
        var client = new ProCutApiSchemaClient(http);

        var services = await client.GetServicesAsync(new ProCutApiSettings
        {
            BaseUrl = "https://api.example.test",
            ApiKey = TestSecrets.ProCutApiKey
        });

        Assert.Equal("valid", Assert.Single(services).Id);
    }

    [Fact]
    public async Task GetServicesAsync_FetchesSchemaWithBearerTokenAndParsesServices()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "services": [
                {
                  "name": "G-code Processing",
                  "id": "gcode_processing",
                  "endpoint": "https://procutsuite.com/api/external/gcode/process",
                  "description": "Process G-code",
                  "deprecated": false,
                  "tools": [
                    {
                      "type": "arc_fitting",
                      "label": "Arc Fitting",
                      "enabled": false,
                      "status": "temporarily_disabled",
                      "disabledReason": "Awaiting validation",
                      "options": [
                        {
                          "key": "toleranceMm",
                          "label": "Tolerance (mm)",
                          "type": "number",
                          "default": 0.05,
                          "required": false,
                          "help": "Maximum deviation from the original path"
                        },
                        { "key": "minSegments", "type": "number", "default": 0 },
                        { "key": "maxSegments", "type": "number", "default": 0 }
                      ]
                    },
                    { "type": "line_joiner", "label": "Line Joiner", "enabled": true, "status": "available" },
                    { "type": "arc_joiner", "label": "Arc Joiner" },
                    { "type": "corner_smooth", "label": "Corner Smoothing", "enabled": true }
                  ]
                },
                {
                  "name": "Corner Smoothing",
                  "id": "corner_smoothing",
                  "endpoint": "https://procutsuite.com/api/external/smooth",
                  "deprecated": true
                },
                {
                  "name": "Generic Import",
                  "id": "generic_import",
                  "endpoint": "https://procutsuite.com/api/external/imports/generic",
                  "options": [
                    {
                      "key": "sourceSystem",
                      "label": "Source System",
                      "type": "string",
                      "default": "Mozaik",
                      "required": true,
                      "help": "Source adapter name"
                    }
                  ]
                }
              ]
            }
            """)
        });
        var client = new ProCutApiSchemaClient(new HttpClient(handler));

        var services = await client.GetServicesAsync(new ProCutApiSettings
        {
            BaseUrl = "https://procutsuite.com",
            ApiKey = TestSecrets.ProCutApiKey
        });

        Assert.Equal(new Uri("https://procutsuite.com/api/external/schema"), handler.Request?.RequestUri);
        Assert.Equal("Bearer", handler.Request?.Headers.Authorization?.Scheme);
        Assert.Equal(TestSecrets.ProCutApiKey, handler.Request?.Headers.Authorization?.Parameter);
        Assert.Equal(3, services.Count);
        Assert.Equal("gcode_processing", services[0].Id);
        Assert.Equal("https://procutsuite.com/api/external/gcode/process", services[0].Endpoint);
        Assert.Equal(4, services[0].Tools.Count);
        Assert.Equal("arc_fitting", services[0].Tools[0].Type);
        Assert.False(services[0].Tools[0].Enabled);
        Assert.Equal("temporarily_disabled", services[0].Tools[0].Status);
        Assert.Equal("Awaiting validation", services[0].Tools[0].DisabledReason);
        Assert.Equal(3, services[0].Tools[0].Options.Count);
        Assert.Equal("toleranceMm", services[0].Tools[0].Options[0].Key);
        Assert.Equal("0.05", services[0].Tools[0].Options[0].DefaultValue);
        Assert.True(services[0].Tools[1].Enabled);
        Assert.Null(services[0].Tools[2].Enabled);
        Assert.True(services[1].Deprecated);
        Assert.Single(services[2].Options);
        Assert.Equal("sourceSystem", services[2].Options[0].Key);
        Assert.Equal("Mozaik", services[2].Options[0].DefaultValue);
    }

    [Fact]
    public void ProCutGcodeToolAvailability_DisablesRiskyToolsWhenOlderSchemaOmitsEnabledFlag()
    {
        Assert.False(ProCutGcodeToolAvailability.IsAvailable("arc_fitting", null));
        Assert.False(ProCutGcodeToolAvailability.IsAvailable("arc_joiner", null));
        Assert.True(ProCutGcodeToolAvailability.IsAvailable("line_joiner", null));
        Assert.True(ProCutGcodeToolAvailability.IsAvailable("corner_smooth", null));
        Assert.True(ProCutGcodeToolAvailability.IsAvailable("arc_fitting", true));
    }

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(responseFactory(request));
        }
    }
}
