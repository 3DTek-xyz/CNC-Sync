using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using CNCSync.Infrastructure.DesktopPrinting;

namespace CNCSync.Tests;

public sealed class LanPrintReceiverTests
{
    private const string BrowserOrigin = "https://procut.example";
    private const string TestTicket = "sample-print-ticket";

    [Fact]
    public async Task AllowedBrowserCanPreflightAndOnlyGetsAcceptedAfterQueueAccepts()
    {
        var entered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = await Fixture.StartAsync(async (ticket, token) =>
        {
            entered.TrySetResult(ticket);
            return await finish.Task.WaitAsync(token);
        });

        using var preflight = Request(HttpMethod.Options);
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type");
        preflight.Headers.Add("Access-Control-Request-Private-Network", "true");
        using var options = await fixture.Client.SendAsync(preflight);
        Assert.Equal(HttpStatusCode.NoContent, options.StatusCode);
        Assert.Equal(BrowserOrigin, Assert.Single(options.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal("true", Assert.Single(options.Headers.GetValues("Access-Control-Allow-Private-Network")));
        Assert.False(options.Headers.Contains("Access-Control-Allow-Credentials"));

        using var post = Request(HttpMethod.Post, "{\"ticket\":\"" + TestTicket + "\"}");
        var response = fixture.Client.SendAsync(post);
        Assert.Equal(TestTicket, await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(response.IsCompleted);
        finish.SetResult(true);
        using var accepted = await response;
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal("{\"status\":\"queued\"}", await accepted.Content.ReadAsStringAsync());
        Assert.Equal(BrowserOrigin, Assert.Single(accepted.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.False(accepted.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("https://procut.example.other")]
    [InlineData("https://procut.example/")]
    [InlineData("http://localhost:5173")]
    public async Task UnlistedOrMissingOriginCannotRedeem(string? origin)
    {
        var requests = 0;
        await using var fixture = await Fixture.StartAsync((_, _) => Task.FromResult(Interlocked.Increment(ref requests) > 0));
        using var post = Request(HttpMethod.Post, "{\"ticket\":\"sample\"}", origin);
        using var response = await fixture.Client.SendAsync(post);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task RejectedOriginsAreLoggedWithoutRequestDataAndRepeatedEntriesAreSuppressed()
    {
        const string untrustedOrigin = "https://untrusted.example";
        var activity = new ConcurrentQueue<string>();
        var requests = 0;
        await using var fixture = await Fixture.StartAsync((_, _) => Task.FromResult(Interlocked.Increment(ref requests) > 0),
            activity: activity.Enqueue);
        foreach (var origin in new[] { untrustedOrigin, "https://another.example" })
        {
            using var request = Request(HttpMethod.Post, "{\"ticket\":\"" + TestTicket + "\"}", origin);
            using var rejected = await fixture.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        }

        using var allowed = Request(HttpMethod.Post, "{\"ticket\":\"" + TestTicket + "\"}");
        using var accepted = await fixture.Client.SendAsync(allowed);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal(1, requests);

        var entry = Assert.Single(activity);
        Assert.StartsWith("Print request rejected", entry);
        Assert.Contains("HTTP 403", entry);
        Assert.DoesNotContain(".example", entry);
        Assert.DoesNotContain(TestTicket, entry);
    }

    [Fact]
    public async Task ExplicitDevelopmentOriginAllowsOnlyExactLocalhost()
    {
        await using var fixture = await Fixture.StartAsync((_, _) => Task.FromResult(true), ["http://localhost:5173"]);
        using var allowed = Request(HttpMethod.Post, "{\"ticket\":\"sample\"}", "http://localhost:5173");
        using var denied = Request(HttpMethod.Post, "{\"ticket\":\"sample\"}", "http://example.test:5173");
        using var allowedResponse = await fixture.Client.SendAsync(allowed);
        using var deniedResponse = await fixture.Client.SendAsync(denied);
        Assert.Equal(HttpStatusCode.Accepted, allowedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
    }

    [Fact]
    public async Task ConfiguredStationHostAndPortMustMatch()
    {
        await using var fixture = await Fixture.StartAsync((_, _) => Task.FromResult(true), expectedOrigin: "https://station.example:8443");
        using var allowed = Request(HttpMethod.Post, "{\"ticket\":\"sample\"}");
        allowed.Headers.Host = "Station.Example:8443";
        using var wrongHost = Request(HttpMethod.Post, "{\"ticket\":\"sample\"}");
        wrongHost.Headers.Host = "other.example:8443";
        using var wrongPort = Request(HttpMethod.Post, "{\"ticket\":\"sample\"}");
        wrongPort.Headers.Host = "station.example:8444";
        using var allowedResponse = await fixture.Client.SendAsync(allowed);
        using var wrongHostResponse = await fixture.Client.SendAsync(wrongHost);
        using var wrongPortResponse = await fixture.Client.SendAsync(wrongPort);
        Assert.Equal(HttpStatusCode.Accepted, allowedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrongHostResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrongPortResponse.StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"Ticket\":\"sample\"}")]
    [InlineData("{\"ticket\":\"sample\",\"ticket\":\"duplicate\"}")]
    [InlineData("{\"ticket\":\"sample\",\"printer\":\"unknown\"}")]
    [InlineData("{\"ticket\":42}")]
    [InlineData("{\"ticket\":\"white space\"}")]
    [InlineData("{\"ticket\":\"é\"}")]
    [InlineData("{\"ticket\":\"\\ud800\"}")]
    public async Task InvalidPayloadNeverRedeems(string json)
    {
        var requests = 0;
        await using var fixture = await Fixture.StartAsync((_, _) => Task.FromResult(Interlocked.Increment(ref requests) > 0));
        using var post = Request(HttpMethod.Post, json);
        using var response = await fixture.Client.SendAsync(post);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, requests);
    }

    [Theory]
    [InlineData("Cookie", "sample=browser-state")]
    [InlineData("Authorization", "Bearer browser-state")]
    public async Task BrowserCredentialsAreRejected(string name, string value)
    {
        var requests = 0;
        await using var fixture = await Fixture.StartAsync((_, _) => Task.FromResult(Interlocked.Increment(ref requests) > 0));
        using var post = Request(HttpMethod.Post, "{\"ticket\":\"sample\"}");
        post.Headers.TryAddWithoutValidation(name, value);
        using var response = await fixture.Client.SendAsync(post);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task AppliesTicketLimitAndStreamingBodyLimit()
    {
        var requests = 0;
        await using var fixture = await Fixture.StartAsync((_, _) => Task.FromResult(Interlocked.Increment(ref requests) > 0));
        using var longest = Request(HttpMethod.Post, "{\"ticket\":\"" + new string('a', 4096) + "\"}");
        using var longTicket = Request(HttpMethod.Post, "{\"ticket\":\"" + new string('a', 4097) + "\"}");
        using var chunked = Request(HttpMethod.Post, new string(' ', 13 * 1024));
        chunked.Headers.TransferEncodingChunked = true;
        using var accepted = await fixture.Client.SendAsync(longest);
        using var invalid = await fixture.Client.SendAsync(longTicket);
        using var oversized = await fixture.Client.SendAsync(chunked);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task RejectsWorkWhileFourRequestsAreInFlight()
    {
        var count = 0;
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = await Fixture.StartAsync(async (_, token) =>
        {
            if (Interlocked.Increment(ref count) == 4)
            {
                waiting.SetResult();
            }
            return await finish.Task.WaitAsync(token);
        });

        var firstRequests = Enumerable.Range(0, 4).Select(_ => Request(HttpMethod.Post, "{\"ticket\":\"sample\"}")).ToArray();
        var pendingResponses = firstRequests.Select(request => fixture.Client.SendAsync(request)).ToArray();
        try
        {
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var extra = Request(HttpMethod.Post, "{\"ticket\":\"extra\"}");
            using var busy = await fixture.Client.SendAsync(extra);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, busy.StatusCode);
        }
        finally
        {
            finish.SetResult(true);
            foreach (var request in firstRequests)
            {
                request.Dispose();
            }
            foreach (var response in await Task.WhenAll(pendingResponses))
            {
                response.Dispose();
            }
        }

        Assert.Equal(4, count);
    }

    [Fact]
    public async Task RejectsWrongPathMethodContentTypeAndOversizeBody()
    {
        var requests = 0;
        await using var fixture = await Fixture.StartAsync((_, _) => Task.FromResult(Interlocked.Increment(ref requests) > 0));
        using var path = Request(HttpMethod.Post, "{}", path: "/v1/print/extra");
        using var query = Request(HttpMethod.Post, "{}", path: "/v1/print?test=1");
        using var method = Request(HttpMethod.Get);
        using var contentType = Request(HttpMethod.Post);
        contentType.Content = new StringContent("{\"ticket\":\"sample\"}", Encoding.UTF8, "text/plain");
        using var longBody = Request(HttpMethod.Post, new string(' ', 13 * 1024));
        longBody.Headers.ExpectContinue = true;
        using var badPreflight = Request(HttpMethod.Options);
        badPreflight.Headers.Add("Access-Control-Request-Method", "POST");
        badPreflight.Headers.Add("Access-Control-Request-Headers", "content-type, authorization");

        foreach (var (request, status) in new[]
        {
            (path, HttpStatusCode.NotFound), (query, HttpStatusCode.NotFound), (method, HttpStatusCode.MethodNotAllowed),
            (contentType, HttpStatusCode.UnsupportedMediaType), (longBody, HttpStatusCode.RequestEntityTooLarge),
            (badPreflight, HttpStatusCode.BadRequest)
        })
        {
            using var response = await fixture.Client.SendAsync(request);
            Assert.Equal(status, response.StatusCode);
        }
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task QueueFailureHasNoAcceptanceOrInternalsInResponse()
    {
        await using var fixture = await Fixture.StartAsync((_, _) => throw new IOException("private queue detail"));
        using var request = Request(HttpMethod.Post, "{\"ticket\":\"sample\"}");
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("127.0.0.1")]
    [InlineData("203.0.113.1")]
    [InlineData("::")]
    public async Task PublicEntryPointRequiresPrivateIpv4Interface(string address)
    {
        using var certificate = CreateCertificate();
        await Assert.ThrowsAsync<ArgumentException>(() => LanPrintReceiver.StartAsync(
            IPAddress.Parse(address), 8443, certificate, [BrowserOrigin], (_, _) => Task.FromResult(true)));
    }

    private static HttpRequestMessage Request(HttpMethod method, string? body = null, string? origin = BrowserOrigin,
        string path = "/v1/print")
    {
        var request = new HttpRequestMessage(method, path);
        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }
        return request;
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(5));
    }

    private sealed class Fixture(LanPrintReceiver receiver, HttpClient client, X509Certificate2 certificate) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public static async Task<Fixture> StartAsync(
            Func<string, CancellationToken, Task<bool>> queue,
            IReadOnlyCollection<string>? origins = null,
            string? expectedOrigin = null,
            Action<string>? activity = null)
        {
            var certificate = CreateCertificate();
            // The client trusts that exact generated certificate.
            var receiver = await LanPrintReceiver.StartLoopbackForTestingAsync(certificate, origins ?? [BrowserOrigin], queue,
                expectedOrigin, activity: activity);
            var thumbprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
            var client = new HttpClient(new HttpClientHandler
            {
                UseCookies = false,
                UseProxy = false,
                ServerCertificateCustomValidationCallback = (_, remote, _, _) =>
                    remote?.GetCertHashString(HashAlgorithmName.SHA256) == thumbprint
            }) { BaseAddress = receiver.BoundAddress, Timeout = TimeSpan.FromSeconds(10) };
            return new Fixture(receiver, client, certificate);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await receiver.DisposeAsync();
            certificate.Dispose();
        }
    }
}
