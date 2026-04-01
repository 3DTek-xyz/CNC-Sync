namespace CNCSync.Core.Configuration;

public sealed class AppSettingsValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<string> Errors { get; } = [];
}
