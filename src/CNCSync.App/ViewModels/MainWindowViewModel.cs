using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using Avalonia.Threading;
using CNCSync.Core.Configuration;
using CNCSync.Core.Processing;
using CNCSync.Core.Services;
using CNCSync.App.Services;
using CNCSync.Infrastructure.Logging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CNCSync.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private static readonly string ResolvedAppVersion = ResolveAppVersion();
    private readonly IAppSettingsStore _settingsStore;
    private readonly AppSettingsValidator _validator;
    private readonly ISyncCoordinator _syncCoordinator;
    private readonly IDestinationService _destinationService;
    private readonly IAppUpdateService _updateService;
    private readonly ILoginStartupService _loginStartupService;
    private readonly IVpnService _vpnService;
    private readonly IThemePreferenceService _themePreferenceService;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly Lock _activityLogFileLock = new();
    private readonly DispatcherTimer _scheduledCatchUpTimer = new();
    private CancellationTokenSource? _autoSaveCts;
    private bool _restartMonitoringAfterSave;
    private bool _isApplyingSettings;
    private bool _scheduledCatchUpInProgress;

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
    private string activityLogPath = string.Empty;

    [ObservableProperty]
    private string activityLogText = string.Empty;

    [ObservableProperty]
    private string scriptsPath = string.Empty;

    [ObservableProperty]
    private bool launchAtLogin;

    [ObservableProperty]
    private bool startMinimized = true;

    [ObservableProperty]
    private AppThemePreference themePreference = AppThemePreference.Light;

    [ObservableProperty]
    private bool scheduledCatchUpEnabled;

    [ObservableProperty]
    private int scheduledCatchUpIntervalMinutes = 10;

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
    private DestinationItemViewModel? selectedDestination;

    [ObservableProperty]
    private WatchProfileItemViewModel? selectedManualWatchProfile;

    [ObservableProperty]
    private ProcessingSetupItemViewModel? selectedProcessingSetup;

    [ObservableProperty]
    private string remoteBrowserPath = "/";

    [ObservableProperty]
    private string remoteBrowserStatus = "Choose a destination and refresh to browse the target path.";

    [ObservableProperty]
    private string destinationTestStatus = "Test the selected destination to verify access.";

    [ObservableProperty]
    private RemoteBrowserItemViewModel? selectedRemoteBrowserItem;

    public MainWindowViewModel(
        IAppSettingsStore settingsStore,
        AppSettingsValidator validator,
        ISyncCoordinator syncCoordinator,
        IDestinationService destinationService,
        IAppUpdateService updateService,
        ILoginStartupService loginStartupService,
        IVpnService vpnService,
        IThemePreferenceService themePreferenceService,
        AppSettings initialSettings)
    {
        _settingsStore = settingsStore;
        _validator = validator;
        _syncCoordinator = syncCoordinator;
        _destinationService = destinationService;
        _updateService = updateService;
        _loginStartupService = loginStartupService;
        _vpnService = vpnService;
        _themePreferenceService = themePreferenceService;
        _scheduledCatchUpTimer.Tick += OnScheduledCatchUpTimerTick;
        PropertyChanged += OnViewModelPropertyChanged;
        WatchProfiles.CollectionChanged += OnWatchProfilesCollectionChanged;
        Destinations.CollectionChanged += OnDestinationsCollectionChanged;
        ProcessingSetups.CollectionChanged += OnProcessingSetupsCollectionChanged;

        SettingsPath = _settingsStore.SettingsFilePath;
        ActivityLogPath = Path.Combine(Path.GetDirectoryName(SettingsPath) ?? AppContext.BaseDirectory, "activity.log");
        ScriptsPath = _settingsStore.ScriptsDirectoryPath;
        UpdateStatus = _updateService.IsSupported
            ? "Update checks are available for installed packaged releases from the public CNC Sync update feed."
            : "Automatic updates are only available on supported packaged desktop builds.";
        Apply(initialSettings);
        _ = SyncLaunchAtLoginStateAsync();
        ValidationSummary = "Run validation to check saved settings.";

        _syncCoordinator.ActivityLogged += OnActivityLogged;
        _syncCoordinator.StatusChanged += OnStatusChanged;
        _syncCoordinator.ProcessingCompleted += OnProcessingCompleted;

        AddActivity("App shell created.");
        AddActivity("Initial settings loaded.");
        UpdateScheduledCatchUpTimer();
        _ = RefreshVpnConnectionsCoreAsync(logResult: false);
    }

    public MainWindowViewModel()
        : this(
            new DesignSettingsStore(),
            new AppSettingsValidator(),
            new DesignSyncCoordinator(),
            new DesignDestinationService(),
            new DesignAppUpdateService(),
            new DesignLoginStartupService(),
            new DesignVpnService(),
            new DesignThemePreferenceService(),
            AppSettings.CreateDefault())
    {
    }

    public ObservableCollection<ActivityLogEntry> ActivityItems { get; } = [];

    public ObservableCollection<string> ValidationErrors { get; } = [];

    public ObservableCollection<WatchProfileItemViewModel> WatchProfiles { get; } = [];

    public ObservableCollection<DestinationItemViewModel> Destinations { get; } = [];

    public ObservableCollection<ProcessingSetupItemViewModel> ProcessingSetups { get; } = [];

    public ObservableCollection<RemoteBrowserItemViewModel> RemoteBrowserItems { get; } = [];
    public ObservableCollection<VpnConnectionOptionViewModel> AvailableVpnConnectionOptions { get; } =
        [new(string.Empty, "(None)")];
    public ObservableCollection<CatchUpIntervalOptionViewModel> AvailableCatchUpIntervals { get; } =
    [
        new(1, "1 minute"),
        new(5, "5 minutes"),
        new(10, "10 minutes"),
        new(30, "30 minutes"),
        new(60, "1 hour")
    ];

    public IReadOnlyList<ProcessingMode> AvailableProcessingModes { get; } = Enum.GetValues<ProcessingMode>();

    public IReadOnlyList<ScriptRunnerMode> AvailableRunnerModes { get; } = Enum.GetValues<ScriptRunnerMode>();
    public IReadOnlyList<DestinationType> AvailableDestinationTypes { get; } = Enum.GetValues<DestinationType>();
    public IReadOnlyList<AppThemePreference> AvailableThemePreferences { get; } = Enum.GetValues<AppThemePreference>();

    public string AppVersion => ResolvedAppVersion;
    public string ProjectSiteUrl => "https://3dtek-xyz.github.io/CNC-Sync/";
    public string ReleaseNotesUrl => "https://github.com/3DTek-xyz/CNC-Sync/releases";

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
        ? "Choose a watch profile to retry any staged items still waiting for delivery."
        : $"Catch-up will retry any pending staged items for '{SelectedManualWatchProfile.DisplayName}'. Successful uploads are removed from staging.";

    public string ScheduledCatchUpSummary => ScheduledCatchUpEnabled
        ? $"Scheduled catch-up is on and will retry staged items every {FormatMinutes(ScheduledCatchUpIntervalMinutes)}."
        : "Scheduled catch-up is off. Only manual catch-up will retry staged items left in staging.";

    public CatchUpIntervalOptionViewModel? SelectedCatchUpIntervalOption
    {
        get => AvailableCatchUpIntervals.FirstOrDefault(option => option.Minutes == ScheduledCatchUpIntervalMinutes)
               ?? AvailableCatchUpIntervals.FirstOrDefault();
        set
        {
            if (value is null)
            {
                return;
            }

            ScheduledCatchUpIntervalMinutes = value.Minutes;
            OnPropertyChanged();
        }
    }

    public DestinationItemViewModel? SelectedProfileDestination
    {
        get
        {
            if (SelectedWatchProfile is null)
            {
                return null;
            }

            return Destinations.FirstOrDefault(destination =>
                string.Equals(destination.Id, SelectedWatchProfile.DestinationId, StringComparison.OrdinalIgnoreCase));
        }
        set
        {
            if (SelectedWatchProfile is null)
            {
                return;
            }

            SelectedWatchProfile.DestinationId = value?.Id ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public VpnConnectionOptionViewModel? SelectedDestinationVpnOption
    {
        get
        {
            var selectedVpnName = SelectedDestination?.RequiredVpnConnectionName ?? string.Empty;
            return AvailableVpnConnectionOptions.FirstOrDefault(option =>
                       string.Equals(option.Name, selectedVpnName, StringComparison.OrdinalIgnoreCase))
                   ?? AvailableVpnConnectionOptions.FirstOrDefault();
        }
        set
        {
            if (SelectedDestination is null)
            {
                return;
            }

            SelectedDestination.RequiredVpnConnectionName = value?.Name ?? string.Empty;
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

    public bool HasSelectedDestination => SelectedDestination is not null;

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

    partial void OnSelectedDestinationChanged(DestinationItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedDestination));
        OnPropertyChanged(nameof(SelectedDestinationVpnOption));
        ResetRemoteBrowserState(value);
        DestinationTestStatus = value is null
            ? "Test the selected destination to verify access."
            : $"Ready to test {value.DisplayName}.";
    }

    partial void OnSelectedRemoteBrowserItemChanged(RemoteBrowserItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedRemoteBrowserItem));
    }

    partial void OnSelectedManualWatchProfileChanged(WatchProfileItemViewModel? value)
    {
        OnPropertyChanged(nameof(ManualActionSummary));
    }

    partial void OnScheduledCatchUpEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ScheduledCatchUpSummary));
        if (_isApplyingSettings)
        {
            return;
        }

        RequestAutoSave(restartMonitoringAfterSave: false);
        UpdateScheduledCatchUpTimer();
    }

    partial void OnScheduledCatchUpIntervalMinutesChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedCatchUpIntervalOption));
        OnPropertyChanged(nameof(ScheduledCatchUpSummary));
        if (_isApplyingSettings)
        {
            return;
        }

        RequestAutoSave(restartMonitoringAfterSave: false);
        UpdateScheduledCatchUpTimer();
    }

    partial void OnThemePreferenceChanged(AppThemePreference value)
    {
        _themePreferenceService.Apply(value);
        if (_isApplyingSettings)
        {
            return;
        }

        RequestAutoSave(restartMonitoringAfterSave: false);
    }

    [RelayCommand]
    private void AddWatchProfile()
    {
        var firstDestination = Destinations.FirstOrDefault();
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
    private void AddDestination()
    {
        var destination = DestinationItemViewModel.FromSettings(
            DestinationSettings.CreateDefault($"Destination {Destinations.Count + 1}"));
        Destinations.Add(destination);
        SelectedDestination = destination;

        if (SelectedWatchProfile is not null && string.IsNullOrWhiteSpace(SelectedWatchProfile.DestinationId))
        {
            SelectedWatchProfile.DestinationId = destination.Id;
            OnPropertyChanged(nameof(SelectedProfileDestination));
        }

        ValidationSummary = "Destination added. Run validation to check settings.";
    }

    [RelayCommand]
    private void RemoveDestination()
    {
        if (SelectedDestination is null)
        {
            return;
        }

        var removedId = SelectedDestination.Id;
        var index = Destinations.IndexOf(SelectedDestination);
        Destinations.Remove(SelectedDestination);

        foreach (var profile in WatchProfiles.Where(profile =>
                     string.Equals(profile.DestinationId, removedId, StringComparison.OrdinalIgnoreCase)))
        {
            profile.DestinationId = string.Empty;
        }

        SelectedDestination = Destinations.Count == 0
            ? null
            : Destinations[Math.Clamp(index, 0, Destinations.Count - 1)];

        OnPropertyChanged(nameof(SelectedProfileDestination));
        ValidationSummary = "Destination removed. Run validation to refresh issues.";
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

    public async Task ImportSettingsFromFileAsync(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        var imported = await JsonSerializer.DeserializeAsync<AppSettings>(stream);
        var normalized = (imported ?? AppSettings.CreateDefault()).Normalize();
        Apply(normalized);
        await SaveCurrentSettingsAsync(CancellationToken.None);
        SaveMessage = $"Settings imported from {filePath}.";
        ValidationSummary = "Imported settings applied. Run validation to confirm them.";
        AddActivity($"Imported settings from {filePath}");
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
            var summary = validation.Errors.Count == 1
                ? validation.Errors[0]
                : string.Join(" | ", validation.Errors);
            AddActivity($"Monitoring start blocked by validation errors: {summary}");
            CurrentTask = "Monitoring could not start. Go to App Settings and click Validate to review the issues.";
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
    private async Task TestDestinationAsync()
    {
        if (SelectedDestination is null)
        {
            AddActivity("Destination test skipped because no destination is selected.");
            return;
        }

        CurrentTask = $"Testing destination connectivity for {SelectedDestination.DisplayName}";
        var result = await _syncCoordinator.TestDestinationAsync(SelectedDestination.ToSettings());
        AddActivity(result.Message);
        DestinationTestStatus = result.Message;
        CurrentTask = result.Success ? "Destination test passed" : result.Message;

        if (result.Success)
        {
            await RefreshRemoteBrowserAsync();
        }
    }

    [RelayCommand]
    private async Task RefreshVpnConnectionsAsync()
    {
        await RefreshVpnConnectionsCoreAsync(logResult: true);
    }

    [RelayCommand]
    private async Task RefreshRemoteBrowserAsync()
    {
        if (SelectedDestination is null)
        {
            RemoteBrowserStatus = "Choose a destination first.";
            return;
        }

        var effectivePath = NormalizeRemoteBrowserPath(RemoteBrowserPath, SelectedDestination.RemoteBasePath);
        RemoteBrowserPath = effectivePath;
        var result = await _destinationService.ListRootEntriesAsync(SelectedDestination.ToSettings(), effectivePath);

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
        if (SelectedDestination is null)
        {
            return;
        }

        var basePath = NormalizeRemoteBrowserPath(SelectedDestination.RemoteBasePath, string.Empty);
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
        if (SelectedDestination is null || SelectedRemoteBrowserItem is null)
        {
            RemoteBrowserStatus = "Choose a remote item to delete.";
            return;
        }

        CurrentTask = $"Deleting remote {(SelectedRemoteBrowserItem.IsDirectory ? "folder" : "file")} {SelectedRemoteBrowserItem.Name}";
        var result = await _destinationService.DeleteRemoteItemAsync(
            SelectedDestination.ToSettings(),
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

        var destination = Destinations.FirstOrDefault(item =>
            string.Equals(item.Id, SelectedManualWatchProfile.DestinationId, StringComparison.OrdinalIgnoreCase));

        if (destination is null)
        {
            AddActivity("Manual catch-up skipped because the selected watch profile has no destination.");
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

        CurrentTask = $"Retrying staged items for {SelectedManualWatchProfile.DisplayName}";
        var result = await _syncCoordinator.CatchUpMissingItemsAsync(
            SelectedManualWatchProfile.ToSettings(),
            destination.ToSettings(),
            processingSetupSettings);
        LastProcessingSummary = result.Message;
        CurrentTask = result.Message;

        if (result.Success &&
            SelectedDestination is not null &&
            string.Equals(SelectedDestination.Id, destination.Id, StringComparison.OrdinalIgnoreCase))
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
        foreach (var destination in Destinations)
        {
            destination.PropertyChanged -= OnDestinationPropertyChanged;
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
        ThemePreference = settings.ThemePreference;
        ScheduledCatchUpEnabled = settings.ScheduledCatchUpEnabled;
        ScheduledCatchUpIntervalMinutes = settings.ScheduledCatchUpIntervalMinutes;

        Destinations.Clear();
        foreach (var destination in settings.Destinations)
        {
            Destinations.Add(DestinationItemViewModel.FromSettings(destination));
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

        SelectedDestination = Destinations.FirstOrDefault();
        SelectedProcessingSetup = ProcessingSetups.FirstOrDefault();
        SelectedWatchProfile = WatchProfiles.FirstOrDefault();
        SelectedManualWatchProfile = SelectedManualWatchProfile is not null
            ? WatchProfiles.FirstOrDefault(profile => profile.Id == SelectedManualWatchProfile.Id) ?? WatchProfiles.FirstOrDefault()
            : WatchProfiles.FirstOrDefault();
        ResetRemoteBrowserState(SelectedDestination);
        _isApplyingSettings = false;
        UpdateScheduledCatchUpTimer();
    }

    private AppSettings ToSettings() =>
        new()
        {
            LaunchAtLogin = LaunchAtLogin,
            StartMinimized = StartMinimized,
            ThemePreference = ThemePreference,
            ScheduledCatchUpEnabled = ScheduledCatchUpEnabled,
            ScheduledCatchUpIntervalMinutes = ScheduledCatchUpIntervalMinutes,
            Destinations = Destinations.Select(destination => destination.ToSettings()).ToList(),
            ProcessingSetups = ProcessingSetups.Select(setup => setup.ToSettings()).ToList(),
            WatchProfiles = WatchProfiles.Select(profile => profile.ToSettings()).ToList()
        };

    private void OnActivityLogged(ActivityLogEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            AddActivityEntry(entry, appendToFile: !entry.IsProgressUpdate);
        });
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
        var entry = new ActivityLogEntry
        {
            TimestampLocal = DateTime.Now,
            Message = message
        };

        AddActivityEntry(entry, appendToFile: true);
    }

    private void AddActivityEntry(ActivityLogEntry entry, bool appendToFile)
    {
        if (entry.IsProgressUpdate && !string.IsNullOrWhiteSpace(entry.ProgressKey))
        {
            var existingProgressEntry = ActivityItems.FirstOrDefault(item =>
                item.IsProgressUpdate &&
                string.Equals(item.ProgressKey, entry.ProgressKey, StringComparison.Ordinal));

            if (existingProgressEntry is not null)
            {
                ActivityItems.Remove(existingProgressEntry);
            }
        }

        ActivityItems.Insert(0, entry);
        ActivityLogText = string.Join(
            Environment.NewLine,
            ActivityItems
                .OrderByDescending(item => item.TimestampLocal)
                .Select(item =>
                {
                    var sourcePrefix = string.IsNullOrWhiteSpace(item.Source) ? string.Empty : $" [{item.Source}]";
                    return $"{item.TimestampLocal:yyyy-MM-dd HH:mm:ss.fff}{sourcePrefix} {item.Message}";
                }));

        if (appendToFile)
        {
            AppendActivityToFile(entry);
        }
    }

    private void AppendActivityToFile(ActivityLogEntry entry)
    {
        try
        {
            var directory = Path.GetDirectoryName(ActivityLogPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var sourcePrefix = string.IsNullOrWhiteSpace(entry.Source) ? string.Empty : $" [{entry.Source}]";
            var line = $"{entry.TimestampLocal:yyyy-MM-dd HH:mm:ss.fff}{sourcePrefix} {entry.Message}{Environment.NewLine}";

            lock (_activityLogFileLock)
            {
                File.AppendAllText(ActivityLogPath, line);
            }
        }
        catch
        {
            // Keep activity logging non-fatal during monitoring and UI updates.
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        if (e.PropertyName is nameof(LaunchAtLogin) or nameof(StartMinimized))
        {
            RequestAutoSave(restartMonitoringAfterSave: false);
        }
    }

    private async void OnScheduledCatchUpTimerTick(object? sender, EventArgs e)
    {
        if (_scheduledCatchUpInProgress)
        {
            return;
        }

        _scheduledCatchUpInProgress = true;
        try
        {
            await RunScheduledCatchUpAsync();
        }
        catch (Exception ex)
        {
            DiagnosticLog.WriteException("Scheduled catch-up failed.", ex);
            var message = $"Scheduled catch-up failed: {ex.Message}";
            AddActivity(message);
            CurrentTask = message;
        }
        finally
        {
            _scheduledCatchUpInProgress = false;
        }
    }

    private async Task RunScheduledCatchUpAsync()
    {
        var catchUpCandidates = WatchProfiles
            .Select(profile => new
            {
                Profile = profile,
                Destination = Destinations.FirstOrDefault(destination => string.Equals(destination.Id, profile.DestinationId, StringComparison.OrdinalIgnoreCase)),
                ProcessingSetup = ProcessingSetups.FirstOrDefault(setup => string.Equals(setup.Id, profile.ProcessingSetupId, StringComparison.OrdinalIgnoreCase))
            })
            .Where(item => item.Destination is not null && item.ProcessingSetup is not null)
            .ToList();

        foreach (var candidate in catchUpCandidates)
        {
            var profileSettings = candidate.Profile.ToSettings();
            if (!Directory.Exists(profileSettings.StagingFolder))
            {
                continue;
            }

            if (!Directory.EnumerateFileSystemEntries(profileSettings.StagingFolder, "*", SearchOption.TopDirectoryOnly)
                    .Any(path => !FileSystemItemFilter.ShouldIgnoreFileSystemItem(Path.GetFileName(path))))
            {
                continue;
            }

            var result = await _syncCoordinator.CatchUpMissingItemsAsync(
                profileSettings,
                candidate.Destination!.ToSettings(),
                candidate.ProcessingSetup!.ToSettings());

            LastProcessingSummary = result.Message;
            CurrentTask = result.Message;
        }
    }

    private void UpdateScheduledCatchUpTimer()
    {
        _scheduledCatchUpTimer.Stop();

        if (!ScheduledCatchUpEnabled)
        {
            return;
        }

        var minutes = ScheduledCatchUpIntervalMinutes <= 0 ? 10 : ScheduledCatchUpIntervalMinutes;
        _scheduledCatchUpTimer.Interval = TimeSpan.FromMinutes(minutes);
        _scheduledCatchUpTimer.Start();
    }

    private static string FormatMinutes(int minutes) => minutes switch
    {
        1 => "1 minute",
        60 => "1 hour",
        _ => $"{minutes} minutes"
    };

    private void ResetRemoteBrowserState(DestinationItemViewModel? destination)
    {
        RemoteBrowserItems.Clear();
        SelectedRemoteBrowserItem = null;
        RemoteBrowserPath = NormalizeRemoteBrowserPath(destination?.RemoteBasePath, "/");
        RemoteBrowserStatus = destination is null
            ? "Choose a destination and refresh to browse the target path."
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
            RequestAutoSave(restartMonitoringAfterSave: true);
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

    private void OnDestinationsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<DestinationItemViewModel>())
            {
                item.PropertyChanged -= OnDestinationPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<DestinationItemViewModel>())
            {
                item.PropertyChanged += OnDestinationPropertyChanged;
            }
        }

        if (!_isApplyingSettings)
        {
            RequestAutoSave(restartMonitoringAfterSave: true);
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
            RequestAutoSave(restartMonitoringAfterSave: true);
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

        RequestAutoSave(restartMonitoringAfterSave: true);
    }

    private void OnDestinationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isApplyingSettings || e.PropertyName == nameof(DestinationItemViewModel.DisplayName))
        {
            return;
        }

        if (ReferenceEquals(sender, SelectedDestination) &&
            e.PropertyName == nameof(DestinationItemViewModel.RequiredVpnConnectionName))
        {
            OnPropertyChanged(nameof(SelectedDestinationVpnOption));
        }

        RequestAutoSave(restartMonitoringAfterSave: true);
    }

    private void OnProcessingSetupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isApplyingSettings || e.PropertyName == nameof(ProcessingSetupItemViewModel.DisplayName))
        {
            return;
        }

        RequestAutoSave(restartMonitoringAfterSave: true);
    }

    private void RequestAutoSave(bool restartMonitoringAfterSave)
    {
        if (restartMonitoringAfterSave)
        {
            _restartMonitoringAfterSave = true;
        }

        _autoSaveCts?.Cancel();
        _autoSaveCts?.Dispose();
        _autoSaveCts = new CancellationTokenSource();
        var cancellationToken = _autoSaveCts.Token;
        _ = SaveCurrentSettingsAsync(cancellationToken);
    }

    private async Task SaveCurrentSettingsAsync(CancellationToken cancellationToken)
    {
        var lockAcquired = false;
        try
        {
            await Task.Delay(500, cancellationToken);
            await _saveLock.WaitAsync();
            lockAcquired = true;
            var settings = ToSettings();
            await _settingsStore.SaveAsync(settings);
            await _loginStartupService.ApplyAsync(settings.LaunchAtLogin);

            if (_restartMonitoringAfterSave &&
                string.Equals(MonitoringStatus, "Running", StringComparison.OrdinalIgnoreCase))
            {
                await _syncCoordinator.StopAsync(cancellationToken);
                await _syncCoordinator.StartAsync(settings, cancellationToken);
                _restartMonitoringAfterSave = false;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    AddActivity("Monitoring reloaded to apply configuration changes.");
                });
            }
            else if (!string.Equals(MonitoringStatus, "Running", StringComparison.OrdinalIgnoreCase))
            {
                _restartMonitoringAfterSave = false;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SaveMessage = $"Changes saved automatically at {DateTime.Now:t}";
            });
        }
        catch (OperationCanceledException)
        {
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
            if (lockAcquired)
            {
                _saveLock.Release();
            }
        }
    }

    private sealed class DesignSettingsStore : IAppSettingsStore
    {
        public string SettingsFilePath => "~/.config/cnc-sync/settings.json";
        public string ScriptsDirectoryPath => "~/.config/cnc-sync/Scripts";

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(AppSettings.CreateDefault());

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class DesignLoginStartupService : ILoginStartupService
    {
        public Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task ApplyAsync(bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class DesignVpnService : IVpnService
    {
        public Task<IReadOnlyList<VpnConnectionInfo>> ListConnectionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VpnConnectionInfo>>(
                [
                    new VpnConnectionInfo { Name = "Office VPN", Identifier = "office-vpn", IsConnected = true },
                    new VpnConnectionInfo { Name = "Tailscale", Identifier = "tailscale", IsConnected = false }
                ]);

        public Task<VpnConnectionEnsureResult> EnsureConnectedAsync(string connectionName, CancellationToken cancellationToken = default) =>
            Task.FromResult(new VpnConnectionEnsureResult
            {
                Success = true,
                ConnectedNow = true,
                ConnectionStateChanged = true,
                Message = $"Connected required VPN '{connectionName}'."
            });

        public Task<(bool Success, string Message)> DisconnectAsync(string connectionName, CancellationToken cancellationToken = default) =>
            Task.FromResult<(bool Success, string Message)>((true, $"Disconnected required VPN '{connectionName}'."));
    }

    private sealed class DesignThemePreferenceService : IThemePreferenceService
    {
        public void Apply(AppThemePreference preference)
        {
        }
    }

    private static string ResolveAppVersion()
    {
        var assembly = typeof(MainWindowViewModel).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? "dev";
    }

    private async Task SyncLaunchAtLoginStateAsync()
    {
        try
        {
            var actualState = await _loginStartupService.IsEnabledAsync();
            if (actualState == LaunchAtLogin)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _isApplyingSettings = true;
                LaunchAtLogin = actualState;
                _isApplyingSettings = false;
                SaveMessage = actualState
                    ? "Launch At Login was enabled by the operating system."
                    : "Launch At Login was not registered on this machine, so the saved setting was corrected.";
            });

            await SaveCurrentSettingsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SaveMessage = $"Unable to verify Launch At Login state: {ex.Message}";
            });
        }
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

    private async Task RefreshVpnConnectionsCoreAsync(bool logResult)
    {
        try
        {
            var connections = await _vpnService.ListConnectionsAsync();
            var preservedNames = Destinations
                .Select(destination => destination.RequiredVpnConnectionName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            AvailableVpnConnectionOptions.Clear();
            AvailableVpnConnectionOptions.Add(new VpnConnectionOptionViewModel(string.Empty, "(None)"));

            var detectedNames = connections
                .Select(connection => connection.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var name in detectedNames
                         .Concat(preservedNames)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                var displayName = detectedNames.Contains(name)
                    ? name
                    : $"{name} (not found on this machine)";
                AvailableVpnConnectionOptions.Add(new VpnConnectionOptionViewModel(name, displayName));
            }

            OnPropertyChanged(nameof(SelectedDestinationVpnOption));

            if (logResult)
            {
                AddActivity($"Loaded {connections.Count} system VPN connection(s).");
            }
        }
        catch (Exception ex)
        {
            if (logResult)
            {
                AddActivity($"Could not list system VPN connections: {ex.Message}");
            }
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
            DestinationSettings? destination,
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

        public Task<(bool Success, string Message)> TestDestinationAsync(DestinationSettings destination, CancellationToken cancellationToken = default) =>
            Task.FromResult<(bool Success, string Message)>((true, "Design-time destination test complete."));

        public Task<(bool Success, string Message)> CatchUpMissingItemsAsync(
            WatchProfileSettings profile,
            DestinationSettings destination,
            ProcessingSetupSettings processingSetup,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<(bool Success, string Message)>((true, $"Design-time catch-up complete for {profile.Name}."));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DesignDestinationService : IDestinationService
    {
        public Task<(bool Success, string Message)> TestConnectionAsync(DestinationSettings destination, CancellationToken cancellationToken = default) =>
            Task.FromResult<(bool Success, string Message)>((true, "Design-time destination test complete."));

        public Task<(bool Success, string Message)> UploadFileSystemItemAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<(bool Success, string Message)>((true, "Design-time destination upload complete."));

        public Task<(bool Success, string Message)> UploadDirectoryAsync(string localPath, DestinationSettings destination, string remoteDirectoryPath, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<(bool Success, string Message)>((true, "Design-time destination upload complete."));

        public Task<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)> ListRootEntriesAsync(DestinationSettings destination, string remoteDirectoryPath, CancellationToken cancellationToken = default) =>
            Task.FromResult<(bool Success, IReadOnlyList<RemoteEntryInfo> Entries, string Message)>(
                (true,
                    [
                        new RemoteEntryInfo { Name = "NC", FullPath = "/NC", IsDirectory = true },
                        new RemoteEntryInfo { Name = "demo.nc", FullPath = "/demo.nc", IsDirectory = false, SizeBytes = 4096 }
                    ],
                    "Design-time destination browser listing complete."));

        public Task<(bool Exists, long? SizeBytes, string Message)> TryGetFileSizeAsync(DestinationSettings destination, string remoteFilePath, CancellationToken cancellationToken = default) =>
            Task.FromResult((true, 4096L as long?, "Design-time remote file size complete."));

        public Task<(bool Success, string Message)> DeleteRemoteItemAsync(DestinationSettings destination, string remotePath, bool isDirectory, CancellationToken cancellationToken = default) =>
            Task.FromResult<(bool Success, string Message)>((true, $"Design-time delete complete: {remotePath}"));
    }

}
