namespace CNCSync.Core.Configuration;

public sealed class ProCutApiSettings
{
    public string BaseUrl { get; set; } = "https://procutsuite.com";
    public string ApiKey { get; set; } = string.Empty;
}
