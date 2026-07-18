using CNCSync.Core.Configuration;

namespace CNCSync.App.ViewModels;

public sealed class ProCutApiServiceOptionViewModel
{
    public ProCutApiServiceOptionViewModel(ProCutApiServiceOption service)
    {
        Id = service.Id;
        Name = string.IsNullOrWhiteSpace(service.Name) ? service.Id : service.Name;
        Endpoint = service.Endpoint;
        Description = service.Description;
        Deprecated = service.Deprecated;
        Tools = service.Tools.Select(tool => new ProCutApiToolOptionViewModel(tool)).ToList();
        Options = service.Options.Select(option => new ProCutApiParameterOptionViewModel(option)).ToList();
    }

    public string Id { get; }
    public string Name { get; }
    public string Endpoint { get; }
    public string Description { get; }
    public bool Deprecated { get; }
    public IReadOnlyList<ProCutApiToolOptionViewModel> Tools { get; }
    public IReadOnlyList<ProCutApiParameterOptionViewModel> Options { get; }
    public string DisplayName => Deprecated ? $"{Name} (deprecated)" : Name;
    public bool HasTools => Tools.Count > 0;
    public bool HasOptions => Options.Count > 0;
    public bool SupportsGcodeTools => Tools.Any(tool =>
        string.Equals(tool.Type, "arc_fitting", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(tool.Type, "line_joiner", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(tool.Type, "arc_joiner", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(tool.Type, "corner_smooth", StringComparison.OrdinalIgnoreCase));
    public bool HasNoParameterMetadata => !HasTools && !HasOptions;
    public string MetadataSummary => HasTools
        ? $"{Tools.Count} tool(s) exposed by this service."
        : HasOptions
            ? $"{Options.Count} service option(s) exposed by this service."
            : "No parameter metadata is exposed by this service.";
}

public sealed class ProCutApiToolOptionViewModel
{
    public ProCutApiToolOptionViewModel(ProCutApiToolOption tool)
    {
        Type = tool.Type;
        Label = string.IsNullOrWhiteSpace(tool.Label) ? tool.Type : tool.Label;
        Description = tool.Description;
        Enabled = tool.Enabled;
        Status = tool.Status;
        DisabledReason = ProCutGcodeToolAvailability.GetUnavailableReason(Type, Enabled, tool.DisabledReason);
        Options = tool.Options.Select(option => new ProCutApiParameterOptionViewModel(option)).ToList();
    }

    public string Type { get; }
    public string Label { get; }
    public string Description { get; }
    public bool? Enabled { get; }
    public string Status { get; }
    public string DisabledReason { get; }
    public IReadOnlyList<ProCutApiParameterOptionViewModel> Options { get; }
    public bool HasOptions => Options.Count > 0;
    public bool IsAvailable => ProCutGcodeToolAvailability.IsAvailable(Type, Enabled);
    public string AvailabilityText => IsAvailable
        ? string.IsNullOrWhiteSpace(Status) ? "Available" : Status
        : DisabledReason;
}

public sealed class ProCutApiParameterOptionViewModel
{
    public ProCutApiParameterOptionViewModel(ProCutApiParameterOption option)
    {
        Key = option.Key;
        Label = string.IsNullOrWhiteSpace(option.Label) ? option.Key : option.Label;
        Type = option.Type;
        DefaultValue = option.DefaultValue;
        Required = option.Required;
        Help = option.Help;
    }

    public string Key { get; }
    public string Label { get; }
    public string Type { get; }
    public string DefaultValue { get; }
    public bool Required { get; }
    public string Help { get; }
    public string RequirementText => Required ? "required" : "optional";
    public string DefaultText => string.IsNullOrWhiteSpace(DefaultValue) ? string.Empty : $"Default: {DefaultValue}";
}
