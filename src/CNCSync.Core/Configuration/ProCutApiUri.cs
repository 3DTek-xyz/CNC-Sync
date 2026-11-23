using System.Diagnostics.CodeAnalysis;

namespace CNCSync.Core.Configuration;

public static class ProCutApiUri
{
    public static bool TryGetBaseUri(string? baseUrl, [NotNullWhen(true)] out Uri? baseUri)
    {
        baseUri = null;
        var value = baseUrl?.Trim();
        if (value?.Contains('?') == true || !TryParseHttps(value, out var parsed))
        {
            return false;
        }

        baseUri = parsed;
        return true;
    }

    public static bool TryResolveEndpoint(string? baseUrl, string? endpoint, [NotNullWhen(true)] out Uri? requestUri)
    {
        requestUri = null;
        var value = endpoint?.Trim();
        if (!TryGetBaseUri(baseUrl, out var baseUri) || !IsUnambiguous(value) ||
            value.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        Uri? resolved;
        if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseHttps(value, out resolved))
            {
                return false;
            }
        }
        else if (!Uri.IsWellFormedUriString(value, UriKind.Relative) ||
                 !Uri.TryCreate(baseUri, value, out resolved))
        {
            return false;
        }

        if (!SameOrigin(baseUri, resolved))
        {
            return false;
        }

        requestUri = resolved;
        return true;
    }

    public static bool HasSameOrigin(string? firstBaseUrl, string? secondBaseUrl) =>
        TryGetBaseUri(firstBaseUrl, out var first) && TryGetBaseUri(secondBaseUrl, out var second) &&
        SameOrigin(first, second);

    private static bool TryParseHttps(string? value, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        if (!IsUnambiguous(value) || !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var parsed) || !parsed.IsWellFormedOriginalString() ||
            parsed.HostNameType is not (UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6))
        {
            return false;
        }

        // Check the raw authority as well, since an empty userinfo can be normalized away.
        var authority = value.AsSpan("https://".Length);
        var authorityEnd = authority.IndexOfAny('/', '?');
        if ((authorityEnd < 0 ? authority : authority[..authorityEnd]).Contains('@'))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    private static bool IsUnambiguous([NotNullWhen(true)] string? value) =>
        !string.IsNullOrEmpty(value) && !value.Any(c => c is '\\' or '#' || char.IsWhiteSpace(c) || char.IsControl(c));

    private static bool SameOrigin(Uri first, Uri second) =>
        first.Scheme == second.Scheme && first.Port == second.Port &&
        string.Equals(first.IdnHost, second.IdnHost, StringComparison.OrdinalIgnoreCase);
}
