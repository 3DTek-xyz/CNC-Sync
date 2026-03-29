using CBWSSSync.Core.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CBWSSSync.App.ViewModels;

public partial class ProcessingSetupItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private ProcessingMode mode = ProcessingMode.DefaultUpload;

    [ObservableProperty]
    private bool replaceRemoteFolderOnUpload;

    [ObservableProperty]
    private string scriptPath = string.Empty;

    [ObservableProperty]
    private ScriptRunnerMode runnerMode = ScriptRunnerMode.Auto;

    [ObservableProperty]
    private string argumentsTemplate = "\"{sourcePath}\" \"{outputPath}\"";

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Unnamed processing setup" : Name;

    public bool UsesExternalScript => Mode == ProcessingMode.ExternalScript;

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    partial void OnModeChanged(ProcessingMode value) => OnPropertyChanged(nameof(UsesExternalScript));

    public static ProcessingSetupItemViewModel FromSettings(ProcessingSetupSettings settings) =>
        new()
        {
            Id = settings.Id,
            Name = settings.Name,
            Mode = settings.Mode,
            ReplaceRemoteFolderOnUpload = settings.ReplaceRemoteFolderOnUpload,
            ScriptPath = settings.ScriptPath,
            RunnerMode = settings.RunnerMode,
            ArgumentsTemplate = settings.ArgumentsTemplate
        };

    public ProcessingSetupSettings ToSettings() =>
        new()
        {
            Id = Id,
            Name = Name,
            Mode = Mode,
            ReplaceRemoteFolderOnUpload = ReplaceRemoteFolderOnUpload,
            ScriptPath = ScriptPath,
            RunnerMode = RunnerMode,
            ArgumentsTemplate = ArgumentsTemplate
        };
}
