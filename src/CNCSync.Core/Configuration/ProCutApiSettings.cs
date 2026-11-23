namespace CNCSync.Core.Configuration;

public sealed class ProCutApiSettings
{
    public string BaseUrl { get; set; } = "https://procutsuite.com";
    public string ApiKey { get; set; } = string.Empty;

    public static ProCutApiSettings ForBoundKey(string baseUrl, string? keyOrigin, string apiKey) => new()
    {
        BaseUrl = baseUrl,
        ApiKey = ProCutApiUri.HasSameOrigin(baseUrl, keyOrigin) ? apiKey : string.Empty
    };
}
