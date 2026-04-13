namespace CNCSync.Core.Configuration;

public sealed class AppSettingsValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public bool HasWarnings => Warnings.Count > 0;
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
}
