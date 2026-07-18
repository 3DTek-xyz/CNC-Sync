namespace CNCSync.Core.Configuration;

public sealed class ProcessingSetupSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public ProcessingMode Mode { get; set; } = ProcessingMode.DefaultUpload;
    public string ScriptPath { get; set; } = string.Empty;
    public ScriptRunnerMode RunnerMode { get; set; } = ScriptRunnerMode.Auto;
    public string ArgumentsTemplate { get; set; } = "\"{sourcePath}\" \"{outputPath}\"";
    public string ProCutServiceId { get; set; } = "gcode_processing";
    public string ProCutApiEndpoint { get; set; } = "/api/external/gcode/process";
    public bool ProCutArcFittingEnabled { get; set; }
    public double ProCutArcFittingToleranceMm { get; set; } = 0.05;
    public int ProCutArcFittingMinSegments { get; set; }
    public int ProCutArcFittingMaxSegments { get; set; }
    public bool ProCutLineJoinerEnabled { get; set; }
    public bool ProCutLineJoinerPreserveFeedBoundaries { get; set; } = true;
    public bool ProCutArcJoinerEnabled { get; set; }
    public double ProCutArcJoinerMaxCombinedAngleDeg { get; set; } = 180;
    public bool ProCutCornerSmoothEnabled { get; set; } = true;
    public double ProCutCornerSmoothAngleThresholdDeg { get; set; } = 45;
    public double ProCutCornerSmoothSlowdownDistanceMm { get; set; } = 5;
    public double ProCutCornerSmoothSlowdownFeedrateMmMin { get; set; } = 1250;
    public double ProCutCornerSmoothSmallArcThresholdMm { get; set; } = 10;
    public bool ProCutCornerSmoothIncludeCircularRamps { get; set; } = true;

    public static ProcessingSetupSettings CreateDefault(string name) =>
        new()
        {
            Name = name,
            Mode = ProcessingMode.DefaultUpload,
            ScriptPath = string.Empty,
            RunnerMode = ScriptRunnerMode.Auto,
            ArgumentsTemplate = "\"{sourcePath}\" \"{outputPath}\"",
            ProCutServiceId = "gcode_processing",
            ProCutApiEndpoint = "/api/external/gcode/process",
            ProCutCornerSmoothEnabled = true
        };
}
