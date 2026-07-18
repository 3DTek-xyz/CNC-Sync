using System.Net.Http.Headers;
using System.Text.Json;
using CNCSync.Core.Configuration;

namespace CNCSync.Infrastructure.ProCutApi;

public sealed class ProCutApiSchemaClient
{
    private readonly HttpClient _httpClient;

    public ProCutApiSchemaClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
    }

    public async Task<IReadOnlyList<ProCutApiServiceOption>> GetServicesAsync(
        ProCutApiSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            throw new InvalidOperationException("ProCut Suite API base URL is required.");
        }

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException("ProCut Suite API key is required.");
        }

        if (!Uri.TryCreate(settings.BaseUrl.Trim(), UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException("ProCut Suite API base URL is invalid.");
        }

        var schemaUri = new Uri(baseUri, "api/external/schema");
        using var request = new HttpRequestMessage(HttpMethod.Get, schemaUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"ProCut Suite API schema returned {(int)response.StatusCode} {response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("services", out var servicesElement) ||
            servicesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("ProCut Suite API schema did not include a services array.");
        }

        var services = new List<ProCutApiServiceOption>();
        foreach (var serviceElement in servicesElement.EnumerateArray())
        {
            var id = ReadString(serviceElement, "id");
            var endpoint = ReadString(serviceElement, "endpoint");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(endpoint))
            {
                continue;
            }

            services.Add(new ProCutApiServiceOption
            {
                Id = id.Trim(),
                Name = ReadString(serviceElement, "name").Trim(),
                Endpoint = endpoint.Trim(),
                Description = ReadString(serviceElement, "description").Trim(),
                Deprecated = ReadBool(serviceElement, "deprecated"),
                Tools = ReadTools(serviceElement),
                Options = ReadParameters(serviceElement, "options")
            });
        }

        if (services.Count == 0)
        {
            throw new InvalidOperationException("ProCut Suite API schema did not include any usable services.");
        }

        return services;
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static bool ReadBool(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.True;
    }

    private static bool? ReadOptionalBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static IReadOnlyList<ProCutApiToolOption> ReadTools(JsonElement element)
    {
        if (!element.TryGetProperty("tools", out var toolsElement) ||
            toolsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var tools = new List<ProCutApiToolOption>();
        foreach (var toolElement in toolsElement.EnumerateArray())
        {
            var type = ReadString(toolElement, "type");
            if (string.IsNullOrWhiteSpace(type))
            {
                continue;
            }

            tools.Add(new ProCutApiToolOption
            {
                Type = type.Trim(),
                Label = ReadString(toolElement, "label").Trim(),
                Description = ReadString(toolElement, "description").Trim(),
                Enabled = ReadOptionalBool(toolElement, "enabled"),
                Status = ReadString(toolElement, "status").Trim(),
                DisabledReason = ReadString(toolElement, "disabledReason").Trim(),
                Options = ReadParameters(toolElement, "options")
            });
        }

        return tools;
    }

    private static IReadOnlyList<ProCutApiParameterOption> ReadParameters(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var optionsElement) ||
            optionsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var options = new List<ProCutApiParameterOption>();
        foreach (var optionElement in optionsElement.EnumerateArray())
        {
            var key = ReadString(optionElement, "key");
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            options.Add(new ProCutApiParameterOption
            {
                Key = key.Trim(),
                Label = ReadString(optionElement, "label").Trim(),
                Type = ReadString(optionElement, "type").Trim(),
                DefaultValue = ReadDefaultValue(optionElement),
                Required = ReadBool(optionElement, "required"),
                Help = ReadString(optionElement, "help").Trim()
            });
        }

        return options;
    }

    private static string ReadDefaultValue(JsonElement element)
    {
        if (!element.TryGetProperty("default", out var property))
        {
            return string.Empty;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? string.Empty,
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }
}
