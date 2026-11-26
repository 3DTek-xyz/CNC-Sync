using System.Net;
using System.Text.Json;

namespace CNCSync.Infrastructure.DesktopPrinting;

internal sealed record PublicPrintCertificateTarget(string Hostname, string Origin);
internal sealed class PublicPrintCertificateProvisioningException()
    : InvalidOperationException("The public print certificate provisioning failed. ProCut Suite will retry.");

public sealed partial class DesktopPrintBackendClient
{
    private const int MaxCertificateResponseBytes = 32 * 1024;

    internal async Task<PublicPrintCertificateTarget> DiscoverPublicCertificateAsync(
        DesktopPrintBackendAccess access, string workshopId, Guid stationId, CancellationToken token)
    {
        using var deadline = CreateDeadline(token);
        using var request = access.CreateRequest(HttpMethod.Get, PublicCertificateEndpoint(workshopId, stationId));
        using var response = await SendAsync(access, request, deadline.Token).ConfigureAwait(false);
        using var document = await ReadPublicCertificateResponseAsync(response, deadline.Token).ConfigureAwait(false);
        return ReadPublicCertificateTarget(document.RootElement, stationId);
    }

    internal async Task<string?> RequestPublicCertificateAsync(DesktopPrintBackendAccess access, string workshopId,
        Guid stationId, PublicPrintCertificateTarget target, IPAddress address, string csr, CancellationToken token)
    {
        if (!DesktopPrintNetwork.IsPrivateIPv4(address) || csr.Length is 0 or > 8192)
        {
            throw new ArgumentException("A private LAN address and station certificate signing request are required.");
        }

        using var deadline = CreateDeadline(token);
        using var request = access.CreateRequest(HttpMethod.Post, PublicCertificateEndpoint(workshopId, stationId));
        request.Content = CreateRequestBody(new { lan_address = address.ToString(), csr_pem = csr });
        using var response = await SendAsync(access, request, deadline.Token).ConfigureAwait(false);
        using var document = await ReadPublicCertificateResponseAsync(response, deadline.Token).ConfigureAwait(false);
        var root = document.RootElement;
        if (ReadPublicCertificateTarget(root, stationId) != target)
        {
            throw InvalidResponse();
        }

        var state = ReadPublicCertificateString(root, "state");
        if (response.StatusCode == HttpStatusCode.Accepted && state == "pending" &&
            root.TryGetProperty("problem", out var problem) && problem.ValueKind == JsonValueKind.String &&
            problem.GetString() == "provisioning_failed")
        {
            throw new PublicPrintCertificateProvisioningException();
        }

        return (response.StatusCode, state) switch
        {
            (HttpStatusCode.Accepted, "pending") => null,
            (HttpStatusCode.OK, "ready") => ReadPublicCertificateString(root, "certificate_pem"),
            _ => throw InvalidResponse()
        };
    }

    private static string PublicCertificateEndpoint(string workshopId, Guid stationId)
    {
        if (!IsWorkshopId(workshopId) || stationId == Guid.Empty)
        {
            throw new ArgumentException("A valid workshop and print station are required.");
        }

        return $"{Endpoint}workshops/{workshopId}/stations/{stationId:D}/certificate/";
    }

    private static async Task<JsonDocument> ReadPublicCertificateResponseAsync(HttpResponseMessage response, CancellationToken token)
    {
        var bytes = await ReadBoundedBodyAsync(response.Content, "application/json", MaxCertificateResponseBytes, token)
            .ConfigureAwait(false);
        try
        {
            return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
        }
        catch (JsonException)
        {
            throw InvalidResponse();
        }
    }

    private static PublicPrintCertificateTarget ReadPublicCertificateTarget(JsonElement element, Guid stationId)
    {
        var host = ReadPublicCertificateString(element, "hostname");
        var origin = ReadPublicCertificateString(element, "origin");
        if (!host.StartsWith($"s-{stationId:N}.", StringComparison.Ordinal) || host.Length > 237 ||
            host.Any(value => !(char.IsAsciiLetterOrDigit(value) || value is '-' or '.')) ||
            !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.HostNameType != UriHostNameType.Dns ||
            host != uri.IdnHost || origin != $"https://{host}:{DesktopPrintStation.ManagedHttpsPort}")
        {
            throw InvalidResponse();
        }

        return new PublicPrintCertificateTarget(host, origin);
    }

    private static string ReadPublicCertificateString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text : throw InvalidResponse();
}
