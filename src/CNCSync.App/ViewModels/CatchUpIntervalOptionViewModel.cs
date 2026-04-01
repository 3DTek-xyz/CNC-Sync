namespace CNCSync.App.ViewModels;

public sealed class CatchUpIntervalOptionViewModel(int minutes, string displayName)
{
    public int Minutes { get; } = minutes;
    public string DisplayName { get; } = displayName;
}
