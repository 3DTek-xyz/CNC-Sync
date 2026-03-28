using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading;
using Avalonia.Threading;
using CBWSSSync.Core.Configuration;
using CBWSSSync.Core.Processing;
using CBWSSSync.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CBWSSSync.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IAppSettingsStore _settingsStore;
    private readonly AppSettingsValidator _validator;
    private readonly ISyncCoordinator _syncCoordinator;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private bool _isApplyingSettings;

    [ObservableProperty]
    private string appTitle = "CNC Sync";

    [ObservableProperty]
    private string subtitle = "Cross-platform tray-first rebuild";

    [ObservableProperty]
    private string monitoringStatus = "Stopped";

    [ObservableProperty]
    private string currentTask = "Idle";

    [ObservableProperty]
    private string settingsPath = string.Empty;

    [ObservableProperty]
    private bool launchAtLogin;

    [ObservableProperty]
    private bool startMinimized = true;

    [ObservableProperty]
    private string saveMessage = "Changes save automatically.";

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

    public MainWindowViewModel(
        IAppSettingsStore settingsStore,
        AppSettingsValidator validator,
        ISyncCoordinator syncCoordinator,
        AppSettings initialSettings)
    {
        _settingsStore = settingsStore;
        _validator = validator;
        _syncCoordinator = syncCoordinator;
        PropertyChanged += OnViewModelPropertyChanged;
        WatchProfiles.CollectionChanged += OnWatchProfilesCollectionChanged;
        FtpDestinations.CollectionChanged += OnFtpDestinationsCollectionChanged;

        SettingsPath = _settingsStore.SettingsFilePath;
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
            AppSettings.CreateDefault())
    {
    }

    public ObservableCollection<ActivityLogEntry> ActivityItems { get; } = [];

    public ObservableCollection<string> ValidationErrors { get; } = [];

    public ObservableCollection<WatchProfileItemViewModel> WatchProfiles { get; } = [];

    public ObservableCollection<FtpDestinationItemViewModel> FtpDestinations { get; } = [];

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

    public bool HasSelectedWatchProfile => SelectedWatchProfile is not null;

    public bool HasSelectedFtpDestination => SelectedFtpDestination is not null;

    public bool CanStartMonitoring => !string.Equals(MonitoringStatus, "Running", StringComparison.OrdinalIgnoreCase);

    public bool CanStopMonitoring => string.Equals(MonitoringStatus, "Running", StringComparison.OrdinalIgnoreCase);

    partial void OnMonitoringStatusChanged(string value)
    {
        OnPropertyChanged(nameof(CanStartMonitoring));
        OnPropertyChanged(nameof(CanStopMonitoring));
        StartMonitoringCommand.NotifyCanExecuteChanged();
        StopMonitoringCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedWatchProfileChanged(WatchProfileItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedWatchProfile));
        OnPropertyChanged(nameof(SelectedProfileDestination));
    }

    partial void OnSelectedFtpDestinationChanged(FtpDestinationItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedFtpDestination));
    }

    partial void OnSelectedManualWatchProfileChanged(WatchProfileItemViewModel? value)
    {
        OnPropertyChanged(nameof(ManualActionSummary));
    }

    [RelayCommand]
    private void AddWatchProfile()
    {
        var firstDestination = FtpDestinations.FirstOrDefault();
        var profile = WatchProfileItemViewModel.FromSettings(
            WatchProfileSettings.CreateDefault($"Watch Profile {WatchProfiles.Count + 1}", firstDestination?.Id ?? string.Empty));
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

        CurrentTask = $"Checking FTP for missing items in {SelectedManualWatchProfile.DisplayName}";
        var result = await _syncCoordinator.CatchUpMissingItemsAsync(
            SelectedManualWatchProfile.ToSettings(),
            destination.ToSettings());
        LastProcessingSummary = result.Message;
        CurrentTask = result.Message;
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

        LaunchAtLogin = settings.LaunchAtLogin;
        StartMinimized = settings.StartMinimized;

        FtpDestinations.Clear();
        foreach (var destination in settings.FtpDestinations)
        {
            FtpDestinations.Add(FtpDestinationItemViewModel.FromSettings(destination));
        }

        WatchProfiles.Clear();
        foreach (var profile in settings.WatchProfiles)
        {
            WatchProfiles.Add(WatchProfileItemViewModel.FromSettings(profile));
        }

        SelectedFtpDestination = FtpDestinations.FirstOrDefault();
        SelectedWatchProfile = WatchProfiles.FirstOrDefault();
        SelectedManualWatchProfile = SelectedManualWatchProfile is not null
            ? WatchProfiles.FirstOrDefault(profile => profile.Id == SelectedManualWatchProfile.Id) ?? WatchProfiles.FirstOrDefault()
            : WatchProfiles.FirstOrDefault();
        _isApplyingSettings = false;
    }

    private AppSettings ToSettings() =>
        new()
        {
            LaunchAtLogin = LaunchAtLogin,
            StartMinimized = StartMinimized,
            FtpDestinations = FtpDestinations.Select(destination => destination.ToSettings()).ToList(),
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

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(AppSettings.CreateDefault());

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
            CancellationToken cancellationToken = default) =>
            Task.FromResult<(bool Success, string Message)>((true, $"Design-time catch-up complete for {profile.Name}."));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
