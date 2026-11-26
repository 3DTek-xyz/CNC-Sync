using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CNCSync.Core.Configuration;
using CNCSync.Infrastructure.DesktopPrinting;

namespace CNCSync.Tests;

public sealed class DesktopPrintBackendClientTests
{
    private static readonly Guid StationId = Guid.NewGuid();
    private static readonly Guid JobId = Guid.NewGuid();
    private const string Ticket = "signed-print-ticket";
    private const string Prefix = "/api/nesting/desktop-print/";
    private static ProCutApiSettings Api => new()
    {
        BaseUrl = "https://api.example.test:444/unused/base/",
        ApiKey = TestSecrets.ProCutApiKey
    };

    [Fact]
    public async Task GetWorkshopsUsesConfiguredOriginAndParsesNumericAndStringIds()
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            AssertRequest(request, HttpMethod.Get, "workshops/");
            Assert.Null(request.Content);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"workshops":[{"id":123,"name":"Cabinet Shop"},{"id":"workshop-2","name":"Assembly"}]}""",
                    Encoding.UTF8, "application/json")
            });
        }));
        using var client = new DesktopPrintBackendClient(http);

        var result = await client.GetWorkshopsAsync(Api, TestContextToken);

        Assert.Equal([new DesktopPrintWorkshop("123", "Cabinet Shop"), new DesktopPrintWorkshop("workshop-2", "Assembly")], result);
    }

    [Fact]
    public async Task DevelopmentUsesOnlyFixedLoopbackOriginAndItsOwnKey()
    {
        var developmentKey = $"development-{Guid.NewGuid():N}";
        using var production = new HttpClient(new Handler((_, _) => throw new Xunit.Sdk.XunitException("Unexpected production request")));
        using var development = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal("http://127.0.0.1:5173/api/nesting/desktop-print/workshops/", request.RequestUri?.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(developmentKey, request.Headers.Authorization?.Parameter);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"workshops":[]}""", Encoding.UTF8, "application/json")
            });
        }));
        using var client = new DesktopPrintBackendClient(production, development);
        var access = DesktopPrintBackendAccess.ForDevelopment(developmentKey);

        Assert.DoesNotContain(developmentKey, access.ToString(), StringComparison.Ordinal);
        Assert.Empty(await client.GetWorkshopsAsync(access, TestContextToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid key")]
    public async Task DevelopmentRejectsMissingOrInvalidKeyBeforeSending(string? developmentKey)
    {
        using var transport = new HttpClient(new Handler((_, _) => throw new Xunit.Sdk.XunitException("Unexpected request")));
        using var client = new DesktopPrintBackendClient(transport, transport);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetWorkshopsAsync(DesktopPrintBackendAccess.ForDevelopment(developmentKey), TestContextToken));
    }

    [Fact]
    public void DevelopmentHandlerDisablesRedirectsCookiesAndHttpProxy()
    {
        using var handler = DesktopPrintBackendClient.CreateHandler(isDevelopment: true);

        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.False(handler.UseProxy);
    }

    [Fact]
    public async Task RegisterSendsOnlyPublicQueueMappingDetails()
    {
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            AssertRequest(request, HttpMethod.Put, $"workshops/42/stations/{StationId:D}/");
            Assert.True(request.Content?.Headers.ContentLength > 0);
            using var body = await ReadJsonAsync(request, token);
            var root = body.RootElement;
            Assert.Equal(3, root.EnumerateObject().Count());
            Assert.Equal("Workshop printer", root.GetProperty("name").GetString());
            Assert.Equal("https://printshop.local:7443", root.GetProperty("origin").GetString());
            var printer = Assert.Single(root.GetProperty("printers").EnumerateArray());
            Assert.Equal(2, printer.EnumerateObject().Count());
            Assert.Equal("labels-a", printer.GetProperty("id").GetString());
            Assert.Equal("Labels A", printer.GetProperty("name").GetString());
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        using var client = new DesktopPrintBackendClient(http);

        await client.RegisterAsync(Api, "42", StationId, "Workshop printer", "https://printshop.local:7443",
            [new DesktopPrintPrinter("labels-a", "Labels A")], TestContextToken);
    }

    [Fact]
    public async Task PublicCertificateDiscoveryAndRequestUseStationRouteAndSendOnlyProofAndLanAddress()
    {
        var hostname = $"s-{StationId:N}.print.example.test";
        var origin = $"https://{hostname}:8443";
        var endpoint = $"workshops/42/stations/{StationId:D}/certificate/";
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            AssertRequest(request, request.Method, endpoint);
            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { hostname, origin }), Encoding.UTF8, "application/json")
                };
            }

            Assert.Equal(HttpMethod.Post, request.Method);
            using var body = await ReadJsonAsync(request, token);
            Assert.Equal(2, body.RootElement.EnumerateObject().Count());
            Assert.Equal("192.168.1.25", body.RootElement.GetProperty("lan_address").GetString());
            Assert.Equal("test-csr", body.RootElement.GetProperty("csr_pem").GetString());
            return new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { state = "pending", hostname, origin }), Encoding.UTF8, "application/json")
            };
        }));
        using var client = new DesktopPrintBackendClient(http);
        var access = DesktopPrintBackendAccess.ForProduction(Api);

        var target = await client.DiscoverPublicCertificateAsync(access, "42", StationId, TestContextToken);
        var certificate = await client.RequestPublicCertificateAsync(access, "42", StationId, target,
            IPAddress.Parse("192.168.1.25"), "test-csr", TestContextToken);

        Assert.Equal(hostname, target.Hostname);
        Assert.Equal(origin, target.Origin);
        Assert.Null(certificate);
    }

    [Fact]
    public async Task RedeemSendsOnlyTicketAndStationIdAndValidatesPdfRouting()
    {
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            AssertRequest(request, HttpMethod.Post, "redeem/");
            Assert.True(request.Content?.Headers.ContentLength > 0);
            using var body = await ReadJsonAsync(request, token);
            Assert.Equal(2, body.RootElement.EnumerateObject().Count());
            Assert.Equal(Ticket, body.RootElement.GetProperty("ticket").GetString());
            Assert.Equal(StationId, body.RootElement.GetProperty("station_id").GetGuid());
            return PdfResponse();
        }));
        using var client = new DesktopPrintBackendClient(http);

        var result = await client.RedeemAsync(Api, StationId, Ticket, TestContextToken);

        Assert.Equal(JobId, result.JobId);
        Assert.Equal("labels-a", result.PrinterId);
        Assert.Equal("%PDF-1.7\n%%EOF"u8.ToArray(), result.Pdf);
    }

    [Theory]
    [InlineData("http://api.example.test")]
    [InlineData("http://127.0.0.1:5173")]
    [InlineData("https://user@api.example.test")]
    [InlineData("https://api.example.test/?token=value")]
    public async Task GetWorkshopsRejectsUnsafeApiBaseBeforeSending(string baseUrl)
    {
        using var http = new HttpClient(new Handler((_, _) => throw new Xunit.Sdk.XunitException("Unexpected request")));
        using var client = new DesktopPrintBackendClient(http);
        var api = Api;
        api.BaseUrl = baseUrl;

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetWorkshopsAsync(api, TestContextToken));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"workshops":{}}""")]
    [InlineData("""{"workshops":[{"id":"../other","name":"Workshop printer"}]}""")]
    [InlineData("""{"workshops":[{"id":-2,"name":"Workshop printer"}]}""")]
    public async Task GetWorkshopsRejectsInvalidResponseShape(string content)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        })));
        using var client = new DesktopPrintBackendClient(http);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetWorkshopsAsync(Api, TestContextToken));
    }

    [Theory]
    [InlineData("../other")]
    [InlineData("42/../../other")]
    [InlineData("%2f")]
    [InlineData("")]
    public async Task RegisterRejectsUnsafePathSegmentBeforeSending(string workshopId)
    {
        using var http = new HttpClient(new Handler((_, _) => throw new Xunit.Sdk.XunitException("Unexpected request")));
        using var client = new DesktopPrintBackendClient(http);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.RegisterAsync(
            Api, workshopId, StationId, "Workshop printer", "https://printshop.local:7443",
            [new DesktopPrintPrinter("labels-a", "Labels A")], TestContextToken));
    }

    [Theory]
    [InlineData("invalid,id", "labels-a")]
    [InlineData("labels-a", "labels-a")]
    public async Task RegisterRejectsInvalidOrDuplicatePrinterIdsBeforeSending(string printerId, string anotherPrinterId)
    {
        using var http = new HttpClient(new Handler((_, _) => throw new Xunit.Sdk.XunitException("Unexpected request")));
        using var client = new DesktopPrintBackendClient(http);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.RegisterAsync(
            Api, "42", StationId, "Workshop printer", "https://printshop.local:7443",
            [new DesktopPrintPrinter(printerId, "Labels A"), new DesktopPrintPrinter(anotherPrinterId, "Labels B")],
            TestContextToken));
    }

    [Fact]
    public async Task RedeemDoesNotRetryRedirectOrExposeResponseContents()
    {
        var requests = 0;
        using var http = new HttpClient(new Handler((_, _) =>
        {
            requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TemporaryRedirect)
            {
                Headers = { Location = new Uri("https://elsewhere.example.test/") },
                Content = new StringContent(Ticket)
            });
        }));
        using var client = new DesktopPrintBackendClient(http);

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => client.RedeemAsync(Api, StationId, Ticket, TestContextToken));

        Assert.Equal(1, requests);
        Assert.Contains("307", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Ticket, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("elsewhere.example.test", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RedeemPropagatesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new Handler((_, token) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(token);
        }));
        using var client = new DesktopPrintBackendClient(http);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.RedeemAsync(Api, StationId, Ticket, cancellation.Token));
    }

    [Theory]
    [InlineData("malformed PDF")]
    [InlineData("wrong media type")]
    [InlineData("missing printer")]
    [InlineData("invalid job ID")]
    [InlineData("multiple printers")]
    [InlineData("oversized response")]
    [InlineData("oversized stream")]
    public async Task RedeemRejectsMalformedOrOversizedPdfResponses(string failure)
    {
        using var http = new HttpClient(new Handler((_, _) =>
        {
            var response = PdfResponse();
            switch (failure)
            {
                case "malformed PDF": ReplaceContent(response, new StringContent("not a PDF", Encoding.UTF8, "application/pdf")); break;
                case "wrong media type": response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain"); break;
                case "missing printer": response.Headers.Remove("X-PCS-Printer-ID"); break;
                case "invalid job ID":
                    response.Headers.Remove("X-PCS-Job-ID");
                    response.Headers.Add("X-PCS-Job-ID", "not-a-guid");
                    break;
                case "multiple printers": response.Headers.Add("X-PCS-Printer-ID", "labels-b"); break;
                case "oversized response": response.Content.Headers.ContentLength = 5 * 1024 * 1024; break;
                case "oversized stream":
                    ReplaceContent(response, new StreamContent(new NonSeekableMemoryStream(new byte[5 * 1024 * 1024])));
                    response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                    response.Content.Headers.ContentLength = null;
                    break;
            }
            return Task.FromResult(response);
        }));
        using var client = new DesktopPrintBackendClient(http);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.RedeemAsync(Api, StationId, Ticket, TestContextToken));
    }

    private static void ReplaceContent(HttpResponseMessage response, HttpContent content)
    {
        response.Content.Dispose();
        response.Content = content;
    }

    private static HttpResponseMessage PdfResponse()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("%PDF-1.7\n%%EOF"u8.ToArray())
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        response.Headers.Add("X-PCS-Printer-ID", "labels-a");
        response.Headers.Add("X-PCS-Job-ID", JobId.ToString("D"));
        return response;
    }

    private static void AssertRequest(HttpRequestMessage request, HttpMethod method, string suffix)
    {
        Assert.Equal(method, request.Method);
        Assert.Equal($"https://api.example.test:444{Prefix}{suffix}", request.RequestUri?.AbsoluteUri);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal(TestSecrets.ProCutApiKey, request.Headers.Authorization?.Parameter);
        Assert.False(request.Headers.Contains("Cookie"));
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpRequestMessage request, CancellationToken token)
    {
        Assert.NotNull(request.Content);
        Assert.Equal("application/json", request.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(await request.Content.ReadAsByteArrayAsync(token));
    }

    private static CancellationToken TestContextToken => CancellationToken.None;

    private sealed class NonSeekableMemoryStream(byte[] buffer) : MemoryStream(buffer)
    {
        public override bool CanSeek => false;
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
