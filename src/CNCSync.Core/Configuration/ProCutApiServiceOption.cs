namespace CNCSync.Core.Configuration;

public sealed class ProCutApiServiceOption
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool Deprecated { get; init; }
    public IReadOnlyList<ProCutApiToolOption> Tools { get; init; } = [];
    public IReadOnlyList<ProCutApiParameterOption> Options { get; init; } = [];
}

public sealed class ProCutApiToolOption
{
    public string Type { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool? Enabled { get; init; }
    public string Status { get; init; } = string.Empty;
    public string DisabledReason { get; init; } = string.Empty;
    public IReadOnlyList<ProCutApiParameterOption> Options { get; init; } = [];
}

public sealed class ProCutApiParameterOption
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string DefaultValue { get; init; } = string.Empty;
    public bool Required { get; init; }
    public string Help { get; init; } = string.Empty;
}
