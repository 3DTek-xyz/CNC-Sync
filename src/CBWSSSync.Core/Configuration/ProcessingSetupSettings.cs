namespace CBWSSSync.Core.Configuration;

public sealed class ProcessingSetupSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public ProcessingMode Mode { get; set; } = ProcessingMode.DefaultUpload;
    public bool ReplaceRemoteFolderOnUpload { get; set; }
    public string ScriptPath { get; set; } = string.Empty;
    public ScriptRunnerMode RunnerMode { get; set; } = ScriptRunnerMode.Auto;
    public string ArgumentsTemplate { get; set; } = "\"{sourcePath}\" \"{outputPath}\"";

    public static ProcessingSetupSettings CreateDefault(string name) =>
        new()
        {
            Name = name,
            Mode = ProcessingMode.DefaultUpload,
            ReplaceRemoteFolderOnUpload = false,
            ScriptPath = string.Empty,
            RunnerMode = ScriptRunnerMode.Auto,
            ArgumentsTemplate = "\"{sourcePath}\" \"{outputPath}\""
        };
}
