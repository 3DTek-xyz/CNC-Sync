namespace CNCSync.Core.Configuration;

public static class ProCutGcodeToolAvailability
{
    private static readonly IReadOnlyDictionary<string, string> TemporarilyUnavailableReasons =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["arc_fitting"] = "Temporarily disabled while awaiting production validation on real customer G-code.",
            ["arc_joiner"] = "Temporarily disabled while awaiting production validation on real customer G-code."
        };

    public static bool IsAvailable(string toolType, bool? schemaEnabled)
    {
        if (schemaEnabled.HasValue)
        {
            return schemaEnabled.Value;
        }

        return !TemporarilyUnavailableReasons.ContainsKey(toolType);
    }

    public static string GetUnavailableReason(string toolType, bool? schemaEnabled, string schemaReason)
    {
        if (IsAvailable(toolType, schemaEnabled))
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(schemaReason))
        {
            return schemaReason.Trim();
        }

        return TemporarilyUnavailableReasons.TryGetValue(toolType, out var reason)
            ? reason
            : "This ProCut Suite tool is not available from the current API schema.";
    }
}
