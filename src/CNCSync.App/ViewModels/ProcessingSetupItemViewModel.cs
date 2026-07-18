using CNCSync.Core.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CNCSync.App.ViewModels;

public partial class ProcessingSetupItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private ProcessingMode mode = ProcessingMode.DefaultUpload;

    [ObservableProperty]
    private string scriptPath = string.Empty;

    [ObservableProperty]
    private ScriptRunnerMode runnerMode = ScriptRunnerMode.Auto;

    [ObservableProperty]
    private string argumentsTemplate = "\"{sourcePath}\" \"{outputPath}\"";

    [ObservableProperty]
    private string proCutServiceId = "gcode_processing";

    [ObservableProperty]
    private string proCutApiEndpoint = "/api/external/gcode/process";

    [ObservableProperty]
    private bool proCutArcFittingEnabled;

    [ObservableProperty]
    private double proCutArcFittingToleranceMm = 0.05;

    [ObservableProperty]
    private int proCutArcFittingMinSegments;

    [ObservableProperty]
    private int proCutArcFittingMaxSegments;

    [ObservableProperty]
    private bool proCutLineJoinerEnabled;

    [ObservableProperty]
    private bool proCutLineJoinerPreserveFeedBoundaries = true;

    [ObservableProperty]
    private bool proCutArcJoinerEnabled;

    [ObservableProperty]
    private double proCutArcJoinerMaxCombinedAngleDeg = 180;

    [ObservableProperty]
    private bool proCutCornerSmoothEnabled = true;

    [ObservableProperty]
    private double proCutCornerSmoothAngleThresholdDeg = 45;

    [ObservableProperty]
    private double proCutCornerSmoothSlowdownDistanceMm = 5;

    [ObservableProperty]
    private double proCutCornerSmoothSlowdownFeedrateMmMin = 1250;

    [ObservableProperty]
    private double proCutCornerSmoothSmallArcThresholdMm = 10;

    [ObservableProperty]
    private bool proCutCornerSmoothIncludeCircularRamps = true;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Unnamed processing setup" : Name;

    public bool UsesExternalScript => Mode == ProcessingMode.ExternalScript;
    public bool UsesProCutApi => Mode == ProcessingMode.ProCutApi;

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    partial void OnModeChanged(ProcessingMode value)
    {
        OnPropertyChanged(nameof(UsesExternalScript));
        OnPropertyChanged(nameof(UsesProCutApi));
    }

    public static ProcessingSetupItemViewModel FromSettings(ProcessingSetupSettings settings) =>
        new()
        {
            Id = settings.Id,
            Name = settings.Name,
            Mode = settings.Mode,
            ScriptPath = settings.ScriptPath,
            RunnerMode = settings.RunnerMode,
            ArgumentsTemplate = settings.ArgumentsTemplate,
            ProCutServiceId = settings.ProCutServiceId,
            ProCutApiEndpoint = settings.ProCutApiEndpoint,
            ProCutArcFittingEnabled = settings.ProCutArcFittingEnabled,
            ProCutArcFittingToleranceMm = settings.ProCutArcFittingToleranceMm,
            ProCutArcFittingMinSegments = settings.ProCutArcFittingMinSegments,
            ProCutArcFittingMaxSegments = settings.ProCutArcFittingMaxSegments,
            ProCutLineJoinerEnabled = settings.ProCutLineJoinerEnabled,
            ProCutLineJoinerPreserveFeedBoundaries = settings.ProCutLineJoinerPreserveFeedBoundaries,
            ProCutArcJoinerEnabled = settings.ProCutArcJoinerEnabled,
            ProCutArcJoinerMaxCombinedAngleDeg = settings.ProCutArcJoinerMaxCombinedAngleDeg,
            ProCutCornerSmoothEnabled = settings.ProCutCornerSmoothEnabled,
            ProCutCornerSmoothAngleThresholdDeg = settings.ProCutCornerSmoothAngleThresholdDeg,
            ProCutCornerSmoothSlowdownDistanceMm = settings.ProCutCornerSmoothSlowdownDistanceMm,
            ProCutCornerSmoothSlowdownFeedrateMmMin = settings.ProCutCornerSmoothSlowdownFeedrateMmMin,
            ProCutCornerSmoothSmallArcThresholdMm = settings.ProCutCornerSmoothSmallArcThresholdMm,
            ProCutCornerSmoothIncludeCircularRamps = settings.ProCutCornerSmoothIncludeCircularRamps
        };

    public ProcessingSetupSettings ToSettings() =>
        new()
        {
            Id = Id,
            Name = Name,
            Mode = Mode,
            ScriptPath = ScriptPath,
            RunnerMode = RunnerMode,
            ArgumentsTemplate = ArgumentsTemplate,
            ProCutServiceId = ProCutServiceId,
            ProCutApiEndpoint = ProCutApiEndpoint,
            ProCutArcFittingEnabled = ProCutArcFittingEnabled,
            ProCutArcFittingToleranceMm = ProCutArcFittingToleranceMm,
            ProCutArcFittingMinSegments = ProCutArcFittingMinSegments,
            ProCutArcFittingMaxSegments = ProCutArcFittingMaxSegments,
            ProCutLineJoinerEnabled = ProCutLineJoinerEnabled,
            ProCutLineJoinerPreserveFeedBoundaries = ProCutLineJoinerPreserveFeedBoundaries,
            ProCutArcJoinerEnabled = ProCutArcJoinerEnabled,
            ProCutArcJoinerMaxCombinedAngleDeg = ProCutArcJoinerMaxCombinedAngleDeg,
            ProCutCornerSmoothEnabled = ProCutCornerSmoothEnabled,
            ProCutCornerSmoothAngleThresholdDeg = ProCutCornerSmoothAngleThresholdDeg,
            ProCutCornerSmoothSlowdownDistanceMm = ProCutCornerSmoothSlowdownDistanceMm,
            ProCutCornerSmoothSlowdownFeedrateMmMin = ProCutCornerSmoothSlowdownFeedrateMmMin,
            ProCutCornerSmoothSmallArcThresholdMm = ProCutCornerSmoothSmallArcThresholdMm,
            ProCutCornerSmoothIncludeCircularRamps = ProCutCornerSmoothIncludeCircularRamps
        };
}
