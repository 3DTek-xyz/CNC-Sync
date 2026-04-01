namespace CNCSync.App.ViewModels;

public sealed class VpnConnectionOptionViewModel(string name, string displayName)
{
    public string Name { get; } = name;
    public string DisplayName { get; } = displayName;
}
