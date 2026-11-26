using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CNCSync.Core.Configuration;

namespace CNCSync.Infrastructure.DesktopPrinting;

public sealed record DesktopPrintWorkshop(string Id, string Name);
public sealed record DesktopPrintPrinter(string Id, string Name);
public sealed record DesktopPrintDocument(byte[] Pdf, string PrinterId, Guid JobId);

internal sealed class DesktopPrintBackendStatusException(HttpStatusCode statusCode)
    : InvalidOperationException($"ProCut Suite print sharing returned HTTP {(int)statusCode}.")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

internal sealed class DesktopPrintBackendAccess
{
    private static readonly Uri DevelopmentOrigin = new("http://127.0.0.1:5173");
    private readonly string? _baseUrl;
    private readonly string _key;

    private DesktopPrintBackendAccess(string? baseUrl, string? key, bool isDevelopment)
    {
        _baseUrl = baseUrl;
        _key = key?.Trim() ?? string.Empty;
        IsDevelopment = isDevelopment;
    }

    internal bool IsDevelopment { get; }
    internal string CertificateRealm => IsDevelopment ? DevelopmentOrigin.GetLeftPart(UriPartial.Authority) :
        ProCutApiUri.TryGetBaseUri(_baseUrl, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : string.Empty;

    internal static DesktopPrintBackendAccess ForProduction(ProCutApiSettings api) =>
        new(api.BaseUrl, api.ApiKey, isDevelopment: false);

    internal static DesktopPrintBackendAccess ForDevelopment(string? developmentKey) =>
        new(null, developmentKey, isDevelopment: true);

    internal HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        if (_key.Length is 0 or > 4096 || _key.Any(char.IsWhiteSpace) || _key.Any(char.IsControl))
        {
            throw new InvalidOperationException(IsDevelopment
                ? "A valid local development API key is required."
                : "A valid HTTPS ProCut Suite API URL and saved API key are required.");
        }

        Uri? uri;
        if (IsDevelopment)
        {
            if (!Uri.TryCreate(DevelopmentOrigin, path, out uri) || uri.Scheme != DevelopmentOrigin.Scheme ||
                uri.Host != DevelopmentOrigin.Host || uri.Port != DevelopmentOrigin.Port || uri.UserInfo.Length != 0)
            {
                throw new InvalidOperationException("The local development API endpoint is invalid.");
            }
        }
        else if (!ProCutApiUri.TryResolveEndpoint(_baseUrl, path, out uri))
        {
            throw new InvalidOperationException("A valid HTTPS ProCut Suite API URL and saved API key are required.");
        }

        return new HttpRequestMessage(method, uri)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", _key) }
        };
    }
}

public sealed partial class DesktopPrintBackendClient : IDisposable
{
    private const string Endpoint = "/api/nesting/desktop-print/";
    private const int MaxWorkshopBytes = 256 * 1024;
    private const int MaxPdfBytes = 4 * 1024 * 1024;
    private const int MaxIdLength = 128;
    private const int MaxWorkshopNameLength = 256;
    internal const int MaxRegistrationNameLength = 100;
    internal const int MaxRegistrationPrinters = 32;
    private const int MaxTicketLength = 4096;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
    private readonly HttpClient _httpClient;
    private readonly HttpClient _developmentHttpClient;
    private readonly bool _ownsHttpClient;
    private readonly bool _ownsDevelopmentHttpClient;

    /// <param name="httpClient">An optional client configured without redirects or cookies. The caller owns it.</param>
    public DesktopPrintBackendClient(HttpClient? httpClient = null) : this(httpClient, null)
    {
    }

    internal DesktopPrintBackendClient(HttpClient? httpClient, HttpClient? developmentHttpClient)
    {
        _httpClient = httpClient ?? new HttpClient(CreateHandler(isDevelopment: false));
        _developmentHttpClient = developmentHttpClient ?? new HttpClient(CreateHandler(isDevelopment: true));
        _ownsHttpClient = httpClient is null;
        _ownsDevelopmentHttpClient = developmentHttpClient is null;
    }

    internal static HttpClientHandler CreateHandler(bool isDevelopment) => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = !isDevelopment,
        MaxResponseHeadersLength = 16
    };

    public Task<IReadOnlyList<DesktopPrintWorkshop>> GetWorkshopsAsync(
        ProCutApiSettings api, CancellationToken token = default) =>
        GetWorkshopsAsync(DesktopPrintBackendAccess.ForProduction(api), token);

    internal async Task<IReadOnlyList<DesktopPrintWorkshop>> GetWorkshopsAsync(
        DesktopPrintBackendAccess access, CancellationToken token = default)
    {
        using var deadline = CreateDeadline(token);
        using var request = access.CreateRequest(HttpMethod.Get, Endpoint + "workshops/");
        using var response = await SendAsync(access, request, deadline.Token);
        var bytes = await ReadBoundedBodyAsync(response.Content, "application/json", MaxWorkshopBytes, deadline.Token);
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("workshops", out var workshops) ||
                workshops.ValueKind != JsonValueKind.Array)
            {
                throw InvalidResponse();
            }

            return workshops.EnumerateArray().Select(ReadWorkshop).ToArray();
        }
        catch (JsonException)
        {
            throw InvalidResponse();
        }
    }

    public Task RegisterAsync(ProCutApiSettings api, string workshopId, Guid stationId, string name, string origin,
        IReadOnlyList<DesktopPrintPrinter> printers, CancellationToken token = default) =>
        RegisterAsync(DesktopPrintBackendAccess.ForProduction(api), workshopId, stationId, name, origin, printers, token);

    internal async Task RegisterAsync(DesktopPrintBackendAccess access, string workshopId, Guid stationId, string name, string origin,
        IReadOnlyList<DesktopPrintPrinter> printers, CancellationToken token = default)
    {
        if (!IsWorkshopId(workshopId) || stationId == Guid.Empty || !IsName(name, MaxRegistrationNameLength) ||
            !ProCutApiUri.TryGetBaseUri(origin, out var stationUri) || stationUri.AbsolutePath != "/")
        {
            throw new InvalidOperationException("The print station registration is invalid.");
        }

        if (printers.Count is 0 or > MaxRegistrationPrinters ||
            printers.Any(printer => !IsPrinterId(printer.Id) || !IsName(printer.Name, MaxRegistrationNameLength)) ||
            printers.Select(printer => printer.Id).Distinct(StringComparer.Ordinal).Count() != printers.Count)
        {
            throw new InvalidOperationException("The print station printer mappings are invalid.");
        }

        using var deadline = CreateDeadline(token);
        using var request = access.CreateRequest(HttpMethod.Put, $"{Endpoint}workshops/{workshopId}/stations/{stationId:D}/");
        request.Content = CreateRequestBody(new
        {
            name,
            origin = stationUri.GetLeftPart(UriPartial.Authority),
            printers = printers.Select(printer => new { id = printer.Id, name = printer.Name }).ToArray()
        });
        using var response = await SendAsync(access, request, deadline.Token);
    }

    public Task<DesktopPrintDocument> RedeemAsync(
        ProCutApiSettings api, Guid stationId, string ticket, CancellationToken token = default) =>
        RedeemAsync(DesktopPrintBackendAccess.ForProduction(api), stationId, ticket, token);

    internal async Task<DesktopPrintDocument> RedeemAsync(
        DesktopPrintBackendAccess access, Guid stationId, string ticket, CancellationToken token = default)
    {
        if (stationId == Guid.Empty || ticket.Length is 0 or > MaxTicketLength || ticket.Any(char.IsWhiteSpace) ||
            ticket.Any(char.IsControl))
        {
            throw new InvalidOperationException("The print ticket is invalid.");
        }

        using var deadline = CreateDeadline(token);
        using var request = access.CreateRequest(HttpMethod.Post, Endpoint + "redeem/");
        request.Content = CreateRequestBody(new { station_id = stationId, ticket });
        using var response = await SendAsync(access, request, deadline.Token);

        var printerId = ReadSingleHeader(response, "X-PCS-Printer-ID");
        if (!IsPrinterId(printerId) ||
            !Guid.TryParseExact(ReadSingleHeader(response, "X-PCS-Job-ID"), "D", out var jobId) || jobId == Guid.Empty)
        {
            throw InvalidResponse();
        }

        var pdf = await ReadBoundedBodyAsync(response.Content, "application/pdf", MaxPdfBytes, deadline.Token);
        if (!pdf.AsSpan().StartsWith("%PDF-"u8))
        {
            throw InvalidResponse();
        }

        return new DesktopPrintDocument(pdf, printerId, jobId);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
        if (_ownsDevelopmentHttpClient)
        {
            _developmentHttpClient.Dispose();
        }
    }

    private static ByteArrayContent CreateRequestBody<T>(T value)
    {
        // The development WSGI server needs a known body length instead of chunked JSON.
        var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(value, JsonSerializerOptions.Web));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        return content;
    }

    private async Task<HttpResponseMessage> SendAsync(DesktopPrintBackendAccess access, HttpRequestMessage request, CancellationToken token)
    {
        HttpResponseMessage response;
        try
        {
            var transport = access.IsDevelopment ? _developmentHttpClient : _httpClient;
            response = await transport.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Could not reach the ProCut Suite print sharing API.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            response.Dispose();
            throw new DesktopPrintBackendStatusException(status);
        }

        return response;
    }

    private static async Task<byte[]> ReadBoundedBodyAsync(HttpContent content, string mediaType, int limit, CancellationToken token)
    {
        if (!string.Equals(content.Headers.ContentType?.MediaType, mediaType, StringComparison.OrdinalIgnoreCase) ||
            content.Headers.ContentLength > limit)
        {
            throw InvalidResponse();
        }

        var buffer = ArrayPool<byte>.Shared.Rent(32 * 1024);
        try
        {
            await using var stream = await content.ReadAsStreamAsync(token);
            using var body = new MemoryStream((int)(content.Headers.ContentLength ?? 0));
            int count;
            while ((count = await stream.ReadAsync(buffer, token)) != 0)
            {
                if (body.Length + count > limit)
                {
                    throw InvalidResponse();
                }

                body.Write(buffer, 0, count);
            }

            return body.ToArray();
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            throw InvalidResponse();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static DesktopPrintWorkshop ReadWorkshop(JsonElement workshop)
    {
        if (workshop.ValueKind != JsonValueKind.Object || !workshop.TryGetProperty("id", out var idElement) ||
            !workshop.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
        {
            throw InvalidResponse();
        }

        var id = idElement.ValueKind switch
        {
            JsonValueKind.String => idElement.GetString() ?? string.Empty,
            JsonValueKind.Number when idElement.TryGetInt64(out var number) && number > 0 =>
                number.ToString(CultureInfo.InvariantCulture),
            _ => string.Empty
        };
        var name = nameElement.GetString() ?? string.Empty;
        return IsWorkshopId(id) && IsName(name, MaxWorkshopNameLength)
            ? new DesktopPrintWorkshop(id, name)
            : throw InvalidResponse();
    }

    private static string ReadSingleHeader(HttpResponseMessage response, string name)
    {
        var values = response.Headers.TryGetValues(name, out var candidates) ? candidates.Take(2).ToArray() : [];
        return values.Length == 1 ? values[0] : throw InvalidResponse();
    }

    internal static bool IsWorkshopId(string? id) =>
        id is { Length: > 0 and <= MaxIdLength } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    internal static bool IsPrinterId(string? id) =>
        id is { Length: > 0 and <= MaxIdLength } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    private static bool IsName(string name, int maxLength) =>
        name.Length > 0 && name.Length <= maxLength && !string.IsNullOrWhiteSpace(name) && !name.Any(char.IsControl);

    private static InvalidOperationException InvalidResponse() => new("ProCut Suite returned an invalid print sharing response.");

    private static CancellationTokenSource CreateDeadline(CancellationToken token)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(RequestTimeout);
        return deadline;
    }
}
