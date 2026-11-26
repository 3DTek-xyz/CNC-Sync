using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace CNCSync.Infrastructure.DesktopPrinting;

public sealed class LanPrintReceiver : IAsyncDisposable
{
    private const string PrintPath = "/v1/print";
    private const string DevelopmentBrowserOrigin = "http://localhost:5173";
    private const int MaxBodyBytes = 12 * 1024;
    private const int MaxTicketLength = 4096;
    private const int MaxConcurrentPrintRequests = 4;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(45);

    private readonly WebApplication _app;
    private readonly HashSet<string> _allowedOrigins;
    private readonly Func<IPAddress, bool> _isAllowedPeer;
    private readonly Uri? _expectedOrigin;
    private readonly Func<string, CancellationToken, Task<bool>> _queueTicketAsync;
    private readonly Action<string>? _activity;
    private readonly PrintRequestActivityLimit _rejections = new();
    private readonly SemaphoreSlim _printRequests = new(MaxConcurrentPrintRequests);
    private int _disposed;

    private LanPrintReceiver(
        WebApplication app,
        HashSet<string> allowedOrigins,
        Func<IPAddress, bool> isAllowedPeer,
        Uri? expectedOrigin,
        Func<string, CancellationToken, Task<bool>> queueTicketAsync,
        Action<string>? activity)
    {
        _app = app;
        _allowedOrigins = allowedOrigins;
        _isAllowedPeer = isAllowedPeer;
        _expectedOrigin = expectedOrigin;
        _queueTicketAsync = queueTicketAsync;
        _activity = activity;
    }

    internal Uri BoundAddress => new(_app.Urls.Single());

    /// <summary>Starts a listener on one private IPv4 interface. Keep the supplied certificates alive until it stops.</summary>
    public static Task<LanPrintReceiver> StartAsync(
        IPAddress bindAddress,
        int port,
        X509Certificate2 certificate,
        IReadOnlyCollection<string> allowedOrigins,
        Func<string, CancellationToken, Task<bool>> queueTicketAsync,
        CancellationToken cancellationToken = default,
        X509Certificate2Collection? certificateChain = null,
        string? expectedOrigin = null,
        Action<string>? activity = null)
    {
        ArgumentNullException.ThrowIfNull(bindAddress);
        if (!IsPrivateIpv4(bindAddress))
        {
            throw new ArgumentException("Select a private IPv4 address on this computer.", nameof(bindAddress));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(port, IPEndPoint.MinPort + 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, IPEndPoint.MaxPort);
        var mask = NetworkInterface.GetAllNetworkInterfaces()
            .Where(networkInterface => networkInterface.OperationalStatus == OperationalStatus.Up)
            .SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses)
            .FirstOrDefault(address => address.Address.Equals(bindAddress))?.IPv4Mask;
        if (mask is null || mask.Equals(IPAddress.Any))
        {
            throw new ArgumentException("The private IPv4 address must belong to an active network interface.", nameof(bindAddress));
        }

        return StartCoreAsync(bindAddress, port, certificate, certificateChain, allowedOrigins,
            peer => IsPrivateIpv4(peer) && InSameSubnet(bindAddress, mask, peer), expectedOrigin, queueTicketAsync, cancellationToken, activity);
    }

    internal static Task<LanPrintReceiver> StartLoopbackForTestingAsync(
        X509Certificate2 certificate,
        IReadOnlyCollection<string> allowedOrigins,
        Func<string, CancellationToken, Task<bool>> queueTicketAsync,
        string? expectedOrigin = null,
        X509Certificate2Collection? certificateChain = null,
        Action<string>? activity = null) =>
        StartCoreAsync(IPAddress.Loopback, 0, certificate, certificateChain, allowedOrigins, IPAddress.IsLoopback, expectedOrigin, queueTicketAsync, default, activity);

    private static async Task<LanPrintReceiver> StartCoreAsync(
        IPAddress bindAddress,
        int port,
        X509Certificate2 certificate,
        X509Certificate2Collection? certificateChain,
        IReadOnlyCollection<string> allowedOrigins,
        Func<IPAddress, bool> isAllowedPeer,
        string? expectedOrigin,
        Func<string, CancellationToken, Task<bool>> queueTicketAsync,
        CancellationToken cancellationToken,
        Action<string>? activity)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentNullException.ThrowIfNull(allowedOrigins);
        ArgumentNullException.ThrowIfNull(queueTicketAsync);
        if (!certificate.HasPrivateKey)
        {
            throw new ArgumentException("The HTTPS certificate must have a private key.", nameof(certificate));
        }

        var originSet = new HashSet<string>(allowedOrigins, StringComparer.Ordinal);
        if (originSet.Count == 0 || originSet.Any(origin => origin != DevelopmentBrowserOrigin && !TryGetHttpsOrigin(origin, out _)))
        {
            throw new ArgumentException("Configure exact HTTPS browser origins. Local development may use http://localhost:5173.", nameof(allowedOrigins));
        }

        Uri? expectedEndpoint = null;
        if (expectedOrigin is not null && (!TryGetHttpsOrigin(expectedOrigin, out expectedEndpoint) || (port != 0 && expectedEndpoint.Port != port)))
        {
            throw new ArgumentException("The station origin must be HTTPS and use the listener port.", nameof(expectedOrigin));
        }

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], EnvironmentName = Environments.Production });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IHostLifetime, ReceiverHostLifetime>();
        builder.WebHost.UseSetting(WebHostDefaults.PreventHostingStartupKey, "true");
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxConcurrentConnections = 32;
            options.Limits.MaxRequestBodySize = MaxBodyBytes;
            options.Limits.MaxRequestHeadersTotalSize = 16 * 1024;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(15);
            options.Listen(bindAddress, port, listener =>
            {
                listener.Protocols = HttpProtocols.Http1;
                listener.UseHttps(https =>
                {
                    https.ServerCertificate = certificate;
                    https.ServerCertificateChain = certificateChain;
                });
            });
        });

        var app = builder.Build();
        var receiver = new LanPrintReceiver(app, originSet, isAllowedPeer, expectedEndpoint, queueTicketAsync, activity);
        app.Run(receiver.HandleAsync);
        try
        {
            await app.StartAsync(cancellationToken);
            return receiver;
        }
        catch
        {
            await receiver.DisposeAsync();
            throw;
        }
    }

    private async Task HandleAsync(HttpContext context)
    {
        var request = context.Request;
        var response = context.Response;
        if (!string.Equals(context.Features.Get<IHttpRequestFeature>()?.RawTarget, PrintPath, StringComparison.Ordinal))
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var preflight = request.Method == HttpMethods.Options;
        if (!preflight && request.Method != HttpMethods.Post)
        {
            response.Headers.Allow = "POST, OPTIONS";
            response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        response.Headers.Vary = "Origin";
        response.Headers.CacheControl = "no-store";
        var origin = request.Headers.Origin;
        if (!MatchesExpectedHost(request.Host) || context.Connection.RemoteIpAddress is not { } peer || !_isAllowedPeer(peer)
            || origin.Count != 1 || !_allowedOrigins.Contains(origin[0]!)
            || request.Headers.ContainsKey(HeaderNames.Cookie) || request.Headers.ContainsKey(HeaderNames.Authorization)
            || request.Headers.ContainsKey(HeaderNames.ProxyAuthorization))
        {
            response.StatusCode = StatusCodes.Status403Forbidden;
            LogRejection("The station address, local network or browser origin was not allowed (HTTP 403).");
            return;
        }

        if (preflight)
        {
            HandlePreflight(context, origin[0]!);
            return;
        }

        response.Headers.AccessControlAllowOrigin = origin;
        await HandlePostAsync(context);
    }

    private void HandlePreflight(HttpContext context, string origin)
    {
        var request = context.Request;
        var response = context.Response;
        var method = request.Headers.AccessControlRequestMethod;
        var headers = request.Headers.AccessControlRequestHeaders;
        var privateNetwork = request.Headers["Access-Control-Request-Private-Network"];
        response.Headers.Vary = "Origin, Access-Control-Request-Method, Access-Control-Request-Headers, Access-Control-Request-Private-Network";

        if (method.Count != 1 || method[0] != HttpMethods.Post
            || (headers.Count > 0 && (headers.Count != 1 || !string.Equals(headers[0]?.Trim(), HeaderNames.ContentType, StringComparison.OrdinalIgnoreCase)))
            || (privateNetwork.Count > 0 && (privateNetwork.Count != 1 || privateNetwork[0] != "true")))
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            LogRejection("The browser's print connection check was invalid (HTTP 400).");
            return;
        }

        response.Headers.AccessControlAllowOrigin = origin;
        response.Headers.AccessControlAllowMethods = HttpMethods.Post;
        response.Headers.AccessControlAllowHeaders = HeaderNames.ContentType;
        if (privateNetwork.Count == 1)
        {
            response.Headers["Access-Control-Allow-Private-Network"] = "true";
        }

        response.StatusCode = StatusCodes.Status204NoContent;
    }

    private async Task HandlePostAsync(HttpContext context)
    {
        var request = context.Request;
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType)
            || !string.Equals(contentType.MediaType.Value, "application/json", StringComparison.OrdinalIgnoreCase)
            || (contentType.Charset.HasValue && !string.Equals(contentType.Charset.Value?.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase))
            || request.Headers.ContainsKey(HeaderNames.ContentEncoding))
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            LogRejection("The request was not UTF-8 JSON (HTTP 415).");
            return;
        }

        if (request.ContentLength > MaxBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            LogRejection("The request was too large (HTTP 413).");
            return;
        }

        if (!_printRequests.Wait(0))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            LogRejection("The station is busy handling other print requests (HTTP 503).");
            return;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, _app.Lifetime.ApplicationStopping);
            timeout.CancelAfter(RequestTimeout);
            await ProcessTicketAsync(context, timeout.Token);
        }
        finally
        {
            _printRequests.Release();
        }
    }

    private async Task ProcessTicketAsync(HttpContext context, CancellationToken cancellationToken)
    {
        string? ticket;
        try
        {
            ticket = await ReadTicketAsync(context.Request.Body, cancellationToken);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            // The JSON reader rejects malformed escapes while decoding the ticket string.
            ticket = null;
        }
        catch (Microsoft.AspNetCore.Http.BadHttpRequestException error)
        {
            context.Response.StatusCode = error.StatusCode;
            LogRejection("The request body could not be read.");
            return;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            LogRejection("Reading the print request was cancelled or timed out.");
            return;
        }

        if (ticket is null)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            LogRejection("The request did not contain a valid print ticket (HTTP 400).");
            return;
        }

        var queued = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            queued = await _queueTicketAsync(ticket, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            return;
        }
        catch (Exception)
        {
            // Upstream and spooler errors may contain private information.
        }

        if (!queued)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            return;
        }

        context.Response.StatusCode = StatusCodes.Status202Accepted;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"status\":\"queued\"}", context.RequestAborted);
    }

    private void LogRejection(string reason)
    {
        if (_activity is not null && _rejections.ShouldLog(reason))
        {
            _activity($"Print request rejected. {reason}");
        }
    }

    private static async Task<string?> ReadTicketAsync(Stream body, CancellationToken cancellationToken)
    {
        using var json = await JsonDocument.ParseAsync(body, new JsonDocumentOptions { MaxDepth = 2 }, cancellationToken);
        if (json.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var properties = json.RootElement.EnumerateObject();
        if (!properties.MoveNext() || !properties.Current.NameEquals("ticket") || properties.Current.Value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var ticket = properties.Current.Value.GetString();
        if (properties.MoveNext() || ticket is not { Length: > 0 and <= MaxTicketLength } || ticket.Any(value => value is < '!' or > '~'))
        {
            return null;
        }

        return ticket;
    }

    private bool MatchesExpectedHost(HostString host) =>
        _expectedOrigin is null || (string.Equals(host.Host, _expectedOrigin.IdnHost, StringComparison.OrdinalIgnoreCase)
            && (host.Port ?? 443) == _expectedOrigin.Port);

    private static bool TryGetHttpsOrigin(string origin, [NotNullWhen(true)] out Uri? uri) =>
        Uri.TryCreate(origin, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps
        && uri.UserInfo.Length == 0 && string.Equals(origin, uri.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal);

    private static bool IsPrivateIpv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168);
    }

    private static bool InSameSubnet(IPAddress address, IPAddress mask, IPAddress peer)
    {
        var addressBytes = address.GetAddressBytes();
        var peerBytes = peer.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();
        for (var index = 0; index < addressBytes.Length; index++)
        {
            if ((addressBytes[index] & maskBytes[index]) != (peerBytes[index] & maskBytes[index]))
            {
                return false;
            }
        }

        return true;
    }

    // The desktop app owns process shutdown.
    private sealed class ReceiverHostLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await _app.StopAsync();
        }
        finally
        {
            await _app.DisposeAsync();
        }
    }
}
