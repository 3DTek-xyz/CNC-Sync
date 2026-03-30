using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Threading;
using Avalonia.Threading;
using CBWSSSync.Core.Configuration;
using CBWSSSync.Core.Processing;
using CBWSSSync.Core.Services;
using CBWSSSync.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CBWSSSync.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private static readonly string ResolvedAppVersion = ResolveAppVersion();
    private readonly IAppSettingsStore _settingsStore;
    private readonly AppSettingsValidator _validator;
    private readonly ISyncCoordinator _syncCoordinator;
    private readonly IFtpService _ftpService;
    private readonly IAppUpdateService _updateService;
    private readonly ILoginStartupService _loginStartupService;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private bool _isApplyingSettings;

    [ObservableProperty]
    private string appTitle = "CNC Sync";

    [ObservableProperty]
    private string subtitle = string.Empty;

    [ObservableProperty]
    private string monitoringStatus = "Stopped";

    [ObservableProperty]
    private string currentTask = "Idle";

    [ObservableProperty]
    private string settingsPath = string.Empty;

    [ObservableProperty]
    private string scriptsPath = string.Empty;

    [ObservableProperty]
    private bool launchAtLogin;

    [ObservableProperty]
    private bool startMinimized = true;

    [ObservableProperty]
    private string saveMessage = "Changes save automatically.";

    [ObservableProperty]
    private string updateStatus = "Installed CNC Sync builds use the public update feed with Velopack.";

    [ObservableProperty]
    private bool hasUpdatePrompt;

    [ObservableProperty]
    private string updatePromptTitle = "Software Updates";

    [ObservableProperty]
    private string updatePromptMessage = "CNC Sync can check for newer releases from the public update feed.";

    [ObservableProperty]
    private string validationSummary = "Validation has not been run.";

    [ObservableProperty]
    private string lastProcessingSummary = "No processing run yet.";

    [ObservableProperty]
    private WatchProfileItemViewModel? selectedWatchProfile;

    [ObservableProperty]
    private FtpDestinationItemViewModel? selectedFtpDestination;

    [ObservableProperty]
    private WatchProfileItemViewModel? selectedManualWatchProfile;

    [ObservableProperty]
    private ProcessingSetupItemViewModel? selectedProcessingSetup;

    [ObservableProperty]
    private string remoteBrowserPath = "/";

    [ObservableProperty]
    private string remoteBrowserStatus = "Choose an FTP server and refresh to browse the remote path.";

    [ObservableProperty]
    private RemoteBrowserItemViewModel? selectedRemoteBrowserItem;

    public MainWindowViewModel(
        IAppSettingsStore settingsStore,
        AppSettingsValidator validator,
        ISyncCoordinator syncCoordinator,
        IFtpService ftpService,
        IAppUpdateService updateService,
        ILoginStartupService loginStartupService,
        AppSettings initialSettings)
    {
        _settingsStore = settingsStore;
        _validator = validator;
        _syncCoordinator = syncCoordinator;
        _ftpService = ftpService;
        _updateService = updateService;
        _loginStartupService = loginStartupService;
        PropertyChanged += OnViewModelPropertyChanged;
        WatchProfiles.CollectionChanged += OnWatchProfilesCollectionChanged;
        FtpDestinations.CollectionChanged += OnFtpDestinationsCollectionChanged;
        ProcessingSetups.CollectionChanged += OnProcessingSetupsCollectionChanged;

        SettingsPath = _settingsStore.SettingsFilePath;
        ScriptsPath = _settingsStore.ScriptsDirectoryPath;
        UpdateStatus = _updateService.IsSupported
            ? "Update checks are available for installed packaged releases from the public CNC Sync update feed."
            : "Automatic updates are only available on supported packaged desktop builds.";
        Apply(initialSettings);
        ValidationSummary = "Run validation to check saved settings.";

        _syncCoordinator.ActivityLogged += OnActivityLogged;
        _syncCoordinator.StatusChanged += OnStatusChanged;
        _syncCoordinator.ProcessingCompleted += OnProcessingCompleted;

        AddActivity("App shell created.");
        AddActivity("Initial settings loaded.");
    }

    public MainWindowViewModel()
        : this(
            new DesignSettingsStore(),
            new AppSettingsValidator(),
            new DesignSyncCoordinator(),
            new DesignFtpService(),
            new DesignAppUpdateService(),
            new DesignLoginStartupService(),
            AppSettings.CreateDefault())
    {
    }

    public ObservableCollection<ActivityLogEntry> ActivityItems { get; } = [];

    public ObservableCollection<string> ValidationErrors { get; } = [];

    public ObservableCollection<WatchProfileItemViewModel> WatchProfiles { get; } = [];

    public ObservableCollection<FtpDestinationItemViewModel> FtpDestinations { get; } = [];

    public ObservableCollection<ProcessingSetupItemViewModel> ProcessingSetups { get; } = [];

    public ObservableCollection<RemoteBrowserItemViewModel> RemoteBrowserItems { get; } = [];

    public IReadOnlyList<ProcessingMode> AvailableProcessingModes { get; } = Enum.GetValues<ProcessingMode>();

    public IReadOnlyList<ScriptRunnerMode> AvailableRunnerModes { get; } = Enum.GetValues<ScriptRunnerMode>();

    public string AppVersion => ResolvedAppVersion;

    public string ActiveMonitoringProfilesSummary
    {
        get
        {
            var activeProfiles = WatchProfiles.Where(profile => profile.Enabled).Select(profile => profile.DisplayName).ToList();
            return activeProfiles.Count switch
            {
                0 => "No watch profiles are enabled.",
                1 => $"Active profile: {activeProfiles[0]}",
                _ => $"Active profiles: {string.Join(", ", activeProfiles)}"
            };
        }
    }

    public string ManualActionSummary => SelectedManualWatchProfile is null
        ? "Choose a watch profile to check its watch folder against the FTP server."
        : $"Catch-up will scan '{SelectedManualWatchProfile.DisplayName}' and only upload items the FTP server does not already have.";

    public FtpDestinationItemViewModel? SelectedProfileDestination
    {
        get
        {
            if (SelectedWatchProfile is null)
            {
                return null;
            }

            return FtpDestinations.FirstOrDefault(destination =>
                string.Equals(destination.Id, SelectedWatchProfile.FtpDestinationId, StringComparison.OrdinalIgnoreCase));
        }
        set
        {
            if (SelectedWatchProfile is null)
            {
                return;
            }

            SelectedWatchProfile.FtpDestinationId = value?.Id ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public ProcessingSetupItemViewModel? SelectedProfileProcessingSetup
    {
        get
        {
            if (SelectedWatchProfile is null)
            {
                return null;
            }

            return ProcessingSetups.FirstOrDefault(setup =>
                string.Equals(setup.Id, SelectedWatchProfile.ProcessingSetupId, StringComparison.OrdinalIgnoreCase));
        }
        set
        {
            if (SelectedWatchProfile is null)
            {
                return;
            }

            SelectedWatchProfile.ProcessingSetupId = value?.Id ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public bool HasSelectedWatchProfile => SelectedWatchProfile is not null;

    public bool HasSelectedFtpDestination => SelectedFtpDestination is not null;

    public bool HasSelectedRemoteBrowserItem => SelectedRemoteBrowserItem is not null;

    public bool CanStartMonitoring => !string.Equals(MonitoringStatus, "Running", StringComparison.OrdinalIgnoreCase);

    public bool CanStopMonitoring => string.Equals(MonitoringStatus, "Running", StringComparison.OrdinalIgnoreCase);

    public bool CanApplyUpdate => _updateService.CanApplyUpdate;

    partial void OnMonitoringStatusChanged(string value)
    {
        OnPropertyChanged(nameof(CanStartMonitoring));
        OnPropertyChanged(nameof(CanStopMonitoring));
        StartMonitoringCommand.NotifyCanExecuteChanged();
        StopMonitoringCommand.NotifyCanExecuteChanged();
    }

    partial void OnUpdateStatusChanged(string value)
    {
        OnPropertyChanged(nameof(CanApplyUpdate));
        DownloadAndRestartUpdateCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedWatchProfileChanged(WatchProfileItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedWatchProfile));
        OnPropertyChanged(nameof(SelectedProfileDestination));
        OnPropertyChanged(nameof(SelectedProfileProcessingSetup));
    }

    partial void OnSelectedFtpDestinationChanged(FtpDestinationItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedFtpDestination));
        ResetRemoteBrowserState(value);

        if (!_isApplyingSettings && value is not null)
        {
            _ = RefreshRemoteBrowserAsync();
        }
    }

    partial void OnSelectedRemoteBrowserItemChanged(RemoteBrowserItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedRemoteBrowserItem));
    }

    partial void OnSelectedManualWatchProfileChanged(WatchProfileItemViewModel? value)
    {
        OnPropertyChanged(nameof(ManualActionSummary));
    }

    [RelayCommand]
    private void AddWatchProfile()
    {
        var firstDestination = FtpDestinations.FirstOrDefault();
        var firstProcessingSetup = ProcessingSetups.FirstOrDefault();
        var profile = WatchProfileItemViewModel.FromSettings(
            WatchProfileSettings.CreateDefault(
                $"Watch Profile {WatchProfiles.Count + 1}",
                firstDestination?.Id ?? string.Empty,
                firstProcessingSetup?.Id ?? string.Empty));
        WatchProfiles.Add(profile);
        SelectedWatchProfile = profile;
        ValidationSummary = "Profile added. Run validation to check settings.";
    }

    [RelayCommand]
    private void RemoveWatchProfile()
    {
        if (SelectedWatchProfile is null)
        {
            return;
        }

        var index = WatchProfiles.IndexOf(SelectedWatchProfile);
        WatchProfiles.Remove(SelectedWatchProfile);
        SelectedWatchProfile = WatchProfiles.Count == 0
            ? null
            : WatchProfiles[Math.Clamp(index, 0, WatchProfiles.Count - 1)];
        ValidationSummary = "Profile removed. Run validation to refresh issues.";
    }

    [RelayCommand]
    private void AddFtpDestination()
    {
        var destination = FtpDestinationItemViewModel.FromSettings(
            FtpDestinationSettings.CreateDefault($"FTP Destination {FtpDestinations.Count + 1}"));
        FtpDestinations.Add(destination);
        SelectedFtpDestination = destination;

        if (SelectedWatchProfile is not null && string.IsNullOrWhiteSpace(SelectedWatchProfile.FtpDestinationId))
        {
            SelectedWatchProfile.FtpDestinationId = destination.Id;
            OnPropertyChanged(nameof(SelectedProfileDestination));
        }

        ValidationSummary = "FTP destination added. Run validation to check settings.";
    }

    [RelayCommand]
    private void RemoveFtpDestination()
    {
        if (SelectedFtpDestination is null)
        {
            return;
        }

        var removedId = SelectedFtpDestination.Id;
        var index = FtpDestinations.IndexOf(SelectedFtpDestination);
        FtpDestinations.Remove(SelectedFtpDestination);

        foreach (var profile in WatchProfiles.Where(profile =>
                     string.Equals(profile.FtpDestinationId, removedId, StringComparison.OrdinalIgnoreCase)))
        {
            profile.FtpDestinationId = string.Empty;
        }

        SelectedFtpDestination = FtpDestinations.Count == 0
            ? null
            : FtpDestinations[Math.Clamp(index, 0, FtpDestinations.Count - 1)];

        OnPropertyChanged(nameof(SelectedProfileDestination));
        ValidationSummary = "FTP destination removed. Run validation to refresh issues.";
    }

    [RelayCommand]
    private void AddProcessingSetup()
    {
        var setup = ProcessingSetupItemViewModel.FromSettings(
            ProcessingSetupSettings.CreateDefault($"Processing Setup {ProcessingSetups.Count + 1}"));
        ProcessingSetups.Add(setup);
        SelectedProcessingSetup = setup;

        if (SelectedWatchProfile is not null && string.IsNullOrWhiteSpace(SelectedWatchProfile.ProcessingSetupId))
        {
            SelectedWatchProfile.ProcessingSetupId = setup.Id;
            OnPropertyChanged(nameof(SelectedProfileProcessingSetup));
        }
    }

    [RelayCommand]
    private void RemoveProcessingSetup()
    {
        if (SelectedProcessingSetup is null)
        {
            return;
        }

        var removedId = SelectedProcessingSetup.Id;
        var index = ProcessingSetups.IndexOf(SelectedProcessingSetup);
        ProcessingSetups.Remove(SelectedProcessingSetup);

        foreach (var profile in WatchProfiles.Where(profile =>
                     string.Equals(profile.ProcessingSetupId, removedId, StringComparison.OrdinalIgnoreCase)))
        {
            profile.ProcessingSetupId = string.Empty;
        }

        SelectedProcessingSetup = ProcessingSetups.Count == 0
            ? null
            : ProcessingSetups[Math.Clamp(index, 0, ProcessingSetups.Count - 1)];

        OnPropertyChanged(nameof(SelectedProfileProcessingSetup));
    }

    [RelayCommand]
    private async Task LoadSettingsAsync()
    {
        var settings = await _settingsStore.LoadAsync();
        Apply(settings);
        SaveMessage = "Settings loaded.";
        ValidationSummary = "Settings loaded. Run validation to check them.";
        AddActivity($"Loaded settings from {SettingsPath}");
    }

    [RelayCommand]
    private void ValidateSettings()
    {
        ValidateCurrentSettings();
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        await RunUpdateCheckAsync(silentIfCurrent: false);
    }

    [RelayCommand(CanExecute = nameof(CanApplyUpdate))]
    private async Task DownloadAndRestartUpdateAsync()
    {
        try
        {
            CurrentTask = "Downloading update";
            var result = await _updateService.DownloadAndApplyUpdateAsync();
            UpdateStatus = result.Message;
            AddActivity(result.Message);
            CurrentTask = result.Message;
            OnPropertyChanged(nameof(CanApplyUpdate));
            DownloadAndRestartUpdateCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            var message = $"Update download failed: {ex.Message}";
            UpdateStatus = message;
            AddActivity(message);
            CurrentTask = message;
            OnPropertyChanged(nameof(CanApplyUpdate));
            DownloadAndRestartUpdateCommand.NotifyCanExecuteChanged();
        }
    }

    public async Task<AppUpdateResult> CheckForUpdatesOnStartupAsync(CancellationToken cancellationToken = default)
    {
        return await RunUpdateCheckAsync(silentIfCurrent: true, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanStartMonitoring))]
    private async Task StartMonitoringAsync()
    {
        var settings = ToSettings();
        var validation = _validator.Validate(settings);
        ApplyValidation(validation);
        if (!validation.IsValid)
        {
            AddActivity("Monitoring start blocked by validation errors.");
            return;
        }

        await _syncCoordinator.StartAsync(settings);
    }

    [RelayCommand(CanExecute = nameof(CanStopMonitoring))]
    private async Task StopMonitoringAsync()
    {
        await _syncCoordinator.StopAsync();
    }

    [RelayCommand]
    private async Task TestFtpAsync()
    {
        if (SelectedFtpDestination is null)
        {
            AddActivity("FTP test skipped because no FTP destination is selected.");
            return;
        }

        CurrentTask = $"Testing FTP connectivity for {SelectedFtpDestination.DisplayName}";
        var result = await _syncCoordinator.TestFtpAsync(SelectedFtpDestination.ToSettings());
        AddActivity(result.Message);
        CurrentTask = result.Success ? "FTP test passed" : result.Message;
    }

    [RelayCommand]
    private async Task RefreshRemoteBrowserAsync()
    {
        if (SelectedFtpDestination is null)
        {
            RemoteBrowserStatus = "Choose an FTP server first.";
            return;
        }

        var effectivePath = NormalizeRemoteBrowserPath(RemoteBrowserPath, SelectedFtpDestination.RemoteBasePath);
        RemoteBrowserPath = effectivePath;
        var result = await _ftpService.ListRootEntriesAsync(SelectedFtpDestination.ToSettings(), effectivePath);

        RemoteBrowserItems.Clear();
        if (!result.Success)
        {
            RemoteBrowserStatus = result.Message;
            AddActivity(result.Message);
            return;
        }

        foreach (var entry in result.Entries
                     .OrderByDescending(item => item.IsDirectory)
                     .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            RemoteBrowserItems.Add(RemoteBrowserItemViewModel.FromRemoteEntry(entry));
        }

        RemoteBrowserStatus = $"{RemoteBrowserItems.Count} item(s) loaded from {effectivePath}.";
    }

    [RelayCommand]
    private async Task OpenSelectedRemoteFolderAsync()
    {
        if (SelectedRemoteBrowserItem is null || !SelectedRemoteBrowserItem.IsDirectory)
        {
            RemoteBrowserStatus = "Choose a remote folder to open.";
            return;
        }

        RemoteBrowserPath = SelectedRemoteBrowserItem.FullPath;
        await RefreshRemoteBrowserAsync();
    }

    [RelayCommand]
    private async Task BrowseRemoteParentAsync()
    {
        if (SelectedFtpDestination is null)
        {
            return;
        }

        var basePath = NormalizeRemoteBrowserPath(SelectedFtpDestination.RemoteBasePath, string.Empty);
        var currentPath = NormalizeRemoteBrowserPath(RemoteBrowserPath, basePath);
        if (string.Equals(currentPath, basePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var parentPath = GetParentRemotePath(currentPath, basePath);
        RemoteBrowserPath = parentPath;
        await RefreshRemoteBrowserAsync();
    }

    [RelayCommand]
    private async Task DeleteSelectedRemoteItemAsync()
    {
        if (SelectedFtpDestination is null || SelectedRemoteBrowserItem is null)
        {
            RemoteBrowserStatus = "Choose a remote item to delete.";
            return;
        }

        CurrentTask = $"Deleting remote {(SelectedRemoteBrowserItem.IsDirectory ? "folder" : "file")} {SelectedRemoteBrowserItem.Name}";
        var result = await _ftpService.DeleteRemoteItemAsync(
            SelectedFtpDestination.ToSettings(),
            SelectedRemoteBrowserItem.FullPath,
            SelectedRemoteBrowserItem.IsDirectory);
        AddActivity(result.Message);
        RemoteBrowserStatus = result.Message;
        CurrentTask = MonitoringStatus switch
        {
            "Running" => "Watching configured profiles for stable files and folders",
            "Stopped" => "Idle",
            _ => MonitoringStatus
        };

        if (result.Success)
        {
            await RefreshRemoteBrowserAsync();
        }
    }

    [RelayCommand]
    private async Task ManualProcessAsync()
    {
        if (SelectedManualWatchProfile is null)
        {
            AddActivity("Manual catch-up skipped because no watch profile is selected.");
            return;
        }

        var destination = FtpDestinations.FirstOrDefault(item =>
            string.Equals(item.Id, SelectedManualWatchProfile.FtpDestinationId, StringComparison.OrdinalIgnoreCase));

        if (destination is null)
        {
            AddActivity("Manual catch-up skipped because the selected watch profile has no FTP destination.");
            return;
        }

        var processingSetup = ProcessingSetups.FirstOrDefault(item =>
            string.Equals(item.Id, SelectedManualWatchProfile.ProcessingSetupId, StringComparison.OrdinalIgnoreCase));

        if (processingSetup is null)
        {
            AddActivity("Manual catch-up skipped because the selected watch profile has no processing setup.");
            return;
        }

        var processingSetupSettings = processingSetup.ToSettings();
        if (processingSetupSettings.Mode == ProcessingMode.ExternalScript &&
            string.IsNullOrWhiteSpace(processingSetupSettings.ScriptPath))
        {
            var message =
                $"Manual catch-up skipped because processing setup '{processingSetup.DisplayName}' is set to External Script but has no script path.";
            AddActivity(message);
            CurrentTask = message;
            LastProcessingSummary = message;
            return;
        }

        CurrentTask = $"Checking FTP for missing items in {SelectedManualWatchProfile.DisplayName}";
        var result = await _syncCoordinator.CatchUpMissingItemsAsync(
            SelectedManualWatchProfile.ToSettings(),
            destination.ToSettings(),
            processingSetupSettings);
        LastProcessingSummary = result.Message;
        CurrentTask = result.Message;

        if (result.Success &&
            SelectedFtpDestination is not null &&
            string.Equals(SelectedFtpDestination.Id, destination.Id, StringComparison.OrdinalIgnoreCase))
        {
            await RefreshRemoteBrowserAsync();
        }
    }

    private void ValidateCurrentSettings()
    {
        var validation = _validator.Validate(ToSettings());
        ApplyValidation(validation);
    }

    private void ApplyValidation(AppSettingsValidationResult validation)
    {
        ValidationErrors.Clear();
        foreach (var error in validation.Errors)
        {
            ValidationErrors.Add(error);
        }

        ValidationSummary = validation.IsValid
            ? "Settings look valid for monitoring."
            : $"{validation.Errors.Count} validation issue(s) need attention.";
    }

    private void Apply(AppSettings settings)
    {
        _isApplyingSettings = true;
        foreach (var destination in FtpDestinations)
        {
            destination.PropertyChanged -= OnFtpDestinationPropertyChanged;
        }

        foreach (var profile in WatchProfiles)
        {
            profile.PropertyChanged -= OnWatchProfilePropertyChanged;
        }

        foreach (var setup in ProcessingSetups)
        {
            setup.PropertyChanged -= OnProcessingSetupPropertyChanged;
        }

        LaunchAtLogin = settings.LaunchAtLogin;
        StartMinimized = settings.StartMinimized;

        FtpDestinations.Clear();
        foreach (var destination in settings.FtpDestinations)
        {
            FtpDestinations.Add(FtpDestinationItemViewModel.FromSettings(destination));
        }

        ProcessingSetups.Clear();
        foreach (var setup in settings.ProcessingSetups)
        {
            ProcessingSetups.Add(ProcessingSetupItemViewModel.FromSettings(setup));
        }

        WatchProfiles.Clear();
        foreach (var profile in settings.WatchProfiles)
        {
            WatchProfiles.Add(WatchProfileItemViewModel.FromSettings(profile));
        }

        SelectedFtpDestination = FtpDestinations.FirstOrDefault();
        SelectedProcessingSetup = ProcessingSetups.FirstOrDefault();
        SelectedWatchProfile = WatchProfiles.FirstOrDefault();
        SelectedManualWatchProfile = SelectedManualWatchProfile is not null
            ? WatchProfiles.FirstOrDefault(profile => profile.Id == SelectedManualWatchProfile.Id) ?? WatchProfiles.FirstOrDefault()
            : WatchProfiles.FirstOrDefault();
        ResetRemoteBrowserState(SelectedFtpDestination);
        _isApplyingSettings = false;
    }

    private AppSettings ToSettings() =>
        new()
        {
            LaunchAtLogin = LaunchAtLogin,
            StartMinimized = StartMinimized,
            FtpDestinations = FtpDestinations.Select(destination => destination.ToSettings()).ToList(),
            ProcessingSetups = ProcessingSetups.Select(setup => setup.ToSettings()).ToList(),
            WatchProfiles = WatchProfiles.Select(profile => profile.ToSettings()).ToList()
        };

    private void OnActivityLogged(ActivityLogEntry entry)
    {
        Dispatcher.UIThread.Post(() => ActivityItems.Insert(0, entry));
    }

    private void OnStatusChanged(string status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            MonitoringStatus = status;
            CurrentTask = status switch
            {
                "Running" => "Watching configured profiles for stable files and folders",
                "Stopped" => "Idle",
                _ => status
            };
        });
    }

    private void OnProcessingCompleted(ProcessingResult result)
    {
        Dispatcher.UIThread.Post(() => UpdateProcessingSummary(result));
    }

    private void UpdateProcessingSummary(ProcessingResult result)
    {
        LastProcessingSummary = result.Success
            ? $"{result.ProcessedFiles.Count} file(s) staged to {result.OutputPath}"
            : result.Message;
    }

    private void AddActivity(string message)
    {
        ActivityItems.Insert(0, new ActivityLogEntry
        {
            TimestampLocal = DateTime.Now,
            Message = message
        });
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        if (e.PropertyName is nameof(LaunchAtLogin) or nameof(StartMinimized))
        {
            RequestAutoSave();
        }
    }

    private void ResetRemoteBrowserState(FtpDestinationItemViewModel? destination)
    {
        RemoteBrowserItems.Clear();
        SelectedRemoteBrowserItem = null;
        RemoteBrowserPath = NormalizeRemoteBrowserPath(destination?.RemoteBasePath, "/");
        RemoteBrowserStatus = destination is null
            ? "Choose an FTP server and refresh to browse the remote path."
            : $"Remote browser is ready for {destination.DisplayName}.";
    }

    private static string NormalizeRemoteBrowserPath(string? path, string? fallbackPath)
    {
        var candidate = string.IsNullOrWhiteSpace(path) ? fallbackPath : path;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return "/";
        }

        var normalized = candidate.Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "/";
        }

        normalized = "/" + normalized.Trim('/');
        return normalized.Length == 0 ? "/" : normalized;
    }

    private static string GetParentRemotePath(string currentPath, string basePath)
    {
        var normalizedCurrent = NormalizeRemoteBrowserPath(currentPath, "/");
        var normalizedBase = NormalizeRemoteBrowserPath(basePath, "/");
        if (string.Equals(normalizedCurrent, normalizedBase, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedBase;
        }

        var trimmed = normalizedCurrent.TrimEnd('/');
        var lastSlashIndex = trimmed.LastIndexOf('/');
        if (lastSlashIndex <= 0)
        {
            return normalizedBase;
        }

        var parent = trimmed[..lastSlashIndex];
        if (string.IsNullOrWhiteSpace(parent))
        {
            parent = "/";
        }

        if (parent.Length < normalizedBase.Length)
        {
            return normalizedBase;
        }

        return parent;
    }

    private void OnWatchProfilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<WatchProfileItemViewModel>())
            {
                item.PropertyChanged -= OnWatchProfilePropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<WatchProfileItemViewModel>())
            {
                item.PropertyChanged += OnWatchProfilePropertyChanged;
            }
        }

        if (!_isApplyingSettings)
        {
            RequestAutoSave();
        }

        OnPropertyChanged(nameof(ActiveMonitoringProfilesSummary));
        if (SelectedManualWatchProfile is not null &&
            !WatchProfiles.Any(profile => profile.Id == SelectedManualWatchProfile.Id))
        {
            SelectedManualWatchProfile = WatchProfiles.FirstOrDefault();
        }
        else if (SelectedManualWatchProfile is null)
        {
            SelectedManualWatchProfile = WatchProfiles.FirstOrDefault();
        }

        OnPropertyChanged(nameof(ManualActionSummary));
    }

    private void OnFtpDestinationsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<FtpDestinationItemViewModel>())
            {
                item.PropertyChanged -= OnFtpDestinationPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<FtpDestinationItemViewModel>())
            {
                item.PropertyChanged += OnFtpDestinationPropertyChanged;
            }
        }

        if (!_isApplyingSettings)
        {
            RequestAutoSave();
        }
    }

    private void OnProcessingSetupsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<ProcessingSetupItemViewModel>())
            {
                item.PropertyChanged -= OnProcessingSetupPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<ProcessingSetupItemViewModel>())
            {
                item.PropertyChanged += OnProcessingSetupPropertyChanged;
            }
        }

        if (!_isApplyingSettings)
        {
            RequestAutoSave();
        }

        if (SelectedProcessingSetup is null)
        {
            SelectedProcessingSetup = ProcessingSetups.FirstOrDefault();
        }
    }

    private void OnWatchProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isApplyingSettings || e.PropertyName == nameof(WatchProfileItemViewModel.DisplayName))
        {
            return;
        }

        if (e.PropertyName is nameof(WatchProfileItemViewModel.Enabled) or nameof(WatchProfileItemViewModel.Name))
        {
            OnPropertyChanged(nameof(ActiveMonitoringProfilesSummary));
            OnPropertyChanged(nameof(ManualActionSummary));
        }

        RequestAutoSave();
    }

    private void OnFtpDestinationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isApplyingSettings || e.PropertyName == nameof(FtpDestinationItemViewModel.DisplayName))
        {
            return;
        }

        RequestAutoSave();
    }

    private void OnProcessingSetupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isApplyingSettings || e.PropertyName == nameof(ProcessingSetupItemViewModel.DisplayName))
        {
            return;
        }

        RequestAutoSave();
    }

    private void RequestAutoSave()
    {
        _ = SaveCurrentSettingsAsync();
    }

    private async Task SaveCurrentSettingsAsync()
    {
        try
        {
            await _saveLock.WaitAsync();
            var settings = ToSettings();
            await _settingsStore.SaveAsync(settings);
            await _loginStartupService.ApplyAsync(settings.LaunchAtLogin);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SaveMessage = $"Changes saved automatically at {DateTime.Now:t}";
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SaveMessage = $"Auto-save failed: {ex.Message}";
            });
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private sealed class DesignSettingsStore : IAppSettingsStore
    {
        public string SettingsFilePath => "~/.config/cbwss-sync/settings.json";
        public string ScriptsDirectoryPath => "~/.config/cbwss-sync/Scripts";

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(AppSettings.CreateDefault());

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class DesignLoginStartupService : ILoginStartupService
    {
        public Task ApplyAsync(bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static string ResolveAppVersion()
    {
        var assembly = typeof(MainWindowViewModel).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? "dev";
    }

    private async Task<AppUpdateResult> RunUpdateCheckAsync(bool silentIfCurrent, CancellationToken cancellationToken = default)
    {
        try
        {
            CurrentTask = "Checking for updates";
            var result = await _updateService.CheckForUpdatesAsync(cancellationToken);
            UpdateStatus = result.Message;
            CurrentTask = result.Message;
            UpdatePromptTitle = result.UpdateAvailable ? "Update Available" : "Software Updates";
            UpdatePromptMessage = result.Message;
            HasUpdatePrompt = result.UpdateAvailable || _updateService.CanApplyUpdate;

            if (!silentIfCurrent || result.UpdateAvailable || !result.Success)
            {
                AddActivity(result.Message);
            }

            OnPropertyChanged(nameof(CanApplyUpdate));
            DownloadAndRestartUpdateCommand.NotifyCanExecuteChanged();
            return result;
        }
        catch (Exception ex)
        {
            var message = $"Update check failed: {ex.Message}";
            UpdateStatus = message;
            UpdatePromptTitle = "Software Updates";
            UpdatePromptMessage = message;
            HasUpdatePrompt = false;
            AddActivity(message);
            CurrentTask = message;
            OnPropertyChanged(nameof(CanApplyUpdate));
            DownloadAndRestartUpdateCommand.NotifyCanExecuteChanged();
            return new AppUpdateResult(false, message);
        }
    }

    private sealed class DesignSyncCoordinator : ISyncCoordinator
    {
        public event Action<ActivityLogEntry>? ActivityLogged { add { } remove { } }
        public event Action<string>? StatusChanged { add { } remove { } }
        public event Action<ProcessingResult>? ProcessingCompleted { add { } remove { } }

        public bool IsRunning => false;

        public Task StartAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<ProcessingResult> ProcessPathAsync(
            string path,
            WatchProfileSettings profile,
            FtpDestinationSettings? destination,
            ProcessingSetupSettings processingSetup,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProcessingResult
            {
                Success = true,
                Message = "Design-time processing complete.",
                SourcePath = path,
                OutputPath = path,
                StartedAtUtc = DateTime.UtcNow,
                FinishedAtUtc = DateTime.UtcNow
            });

        public Task<(bool Success, string Message)> TestFtpAsync(FtpDestinationSettings destination, CancellationToken cancellationToken = default) =>
            Task.FromResult<(bool Success, string Message)>((true, "Design-time FTP test complete."));

        public Task<(bool Success, string Message)> CatchUpMissingItemsAsync(
            WatchProfileSettings profile,
            FtpDestinationSettings destination,
            ProcessingSetupSettings processingSetup,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<(bool Success, string Message)>((true, $"Design-time catch-up complete for {profile.Name}."));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DesignFtpService : IFtpService
    {
        public Task<(bool Success, string Message)> TestConnectionAsync(FtpDestinationSettings destination, CancellationToken cancellationToken = default) =>
            Task.FromResult<(bool Success, string Message)>((true, "Design-time FTP test complete."));

        public Task<(bool Success, string Message)> UploadDirectoryAsync(string localPath, FtpDestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default) =>
            Task.FromResult<(bool Success, string Message)>((true, "Design-time FTP upload complete."));

        public Task<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)> ListRootEntriesAsync(FtpDestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default) =>
            Task.FromResult<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)>(
                (true,
                    [
                        new RemoteEntryInfo { Name = "NC", FullPath = "/NC", IsDirectory = true },
                        new RemoteEntryInfo { Name = "demo.nc", FullPath = "/demo.nc", IsDirectory = false, SizeBytes = 4096 }
                    ],
                    "Design-time FTP browser listing complete."));

        public Task<(bool Exists, long? SizeBytes, string Message)> TryGetFileSizeAsync(FtpDestinationSettings destination, string remoteFilePath, CancellationToken cancellationToken = default) =>
            Task.FromResult((true, 4096L as long?, "Design-time remote file size complete."));

        public Task<(bool Success, string Message)> DeleteRemoteItemAsync(FtpDestinationSettings destination, string remotePath, bool isDirectory, CancellationToken cancellationToken = default) =>
            Task.FromResult<(bool Success, string Message)>((true, $"Design-time delete complete: {remotePath}"));
    }
}
