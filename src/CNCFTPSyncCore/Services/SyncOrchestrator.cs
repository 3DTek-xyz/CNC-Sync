using CNCFTPSyncCore.Models;
using CNCFTPSyncCore.Services;

namespace CNCFTPSyncCore.Services
{
    public interface ISyncOrchestrator : IDisposable
    {
        event Action<string, ProcessingResult> ProcessingCompleted;
        event Action<string> ProcessingStarted;
        event Action<string> StatusChanged;
        
        Task StartAsync();
        Task StopAsync();
        Task<ProcessingResult> ProcessFolderManuallyAsync(string folderPath);
        void RefreshConfiguration(); // Add method to refresh config
        bool IsRunning { get; }
        string CurrentStatus { get; }
    }

    public class SyncOrchestrator : ISyncOrchestrator, IDisposable
    {
        private readonly IConfigurationService _configService;
        private readonly ILogService _logger;
        private readonly IGCodeProcessor _gCodeProcessor;
        private readonly IFtpService _ftpService;
        private IFolderWatcher? _folderWatcher;
        
        private SyncConfiguration _config;
        private string _currentStatus = "Stopped";

        public event Action<string, ProcessingResult>? ProcessingCompleted;
        public event Action<string>? ProcessingStarted;
        public event Action<string>? StatusChanged;

        public bool IsRunning => _folderWatcher?.IsRunning ?? false;
        public string CurrentStatus => _currentStatus;

        public SyncOrchestrator(
            IConfigurationService configService,
            ILogService logger,
            IGCodeProcessor gCodeProcessor,
            IFtpService ftpService)
        {
            _configService = configService;
            _logger = logger;
            _gCodeProcessor = gCodeProcessor;
            _ftpService = ftpService;
            _config = _configService.LoadConfiguration();
        }

        public async Task StartAsync()
        {
            var orchestratorStopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (IsRunning)
                {
                    _logger.LogWarning("Sync orchestrator is already running");
                    return;
                }

                _logger.LogInfo($"STARTUP TIMING: Starting G-Code Sync orchestrator at {DateTime.Now:HH:mm:ss.fff}");
                SetStatus("Starting...");

                // Reload configuration
                _logger.LogInfo($"STARTUP TIMING: Loading configuration at {orchestratorStopwatch.ElapsedMilliseconds}ms");
                _config = _configService.LoadConfiguration();
                _logger.LogInfo($"STARTUP TIMING: Configuration loaded at {orchestratorStopwatch.ElapsedMilliseconds}ms");

                // Ensure required directories exist
                _logger.LogInfo($"STARTUP TIMING: Ensuring directories exist at {orchestratorStopwatch.ElapsedMilliseconds}ms");
                EnsureDirectoriesExist();
                _logger.LogInfo($"STARTUP TIMING: Directory check completed at {orchestratorStopwatch.ElapsedMilliseconds}ms");

                // Test FTP connection
                _logger.LogInfo($"STARTUP TIMING: Starting FTP connection test at {orchestratorStopwatch.ElapsedMilliseconds}ms");
                SetStatus("Testing FTP connection...");
                if (!await _ftpService.TestConnectionAsync())
                {
                    throw new InvalidOperationException("FTP connection test failed. Please check configuration.");
                }
                _logger.LogInfo($"STARTUP TIMING: FTP connection test completed at {orchestratorStopwatch.ElapsedMilliseconds}ms");

                // Initialize and start folder watcher
                _logger.LogInfo($"STARTUP TIMING: Creating folder watcher at {orchestratorStopwatch.ElapsedMilliseconds}ms");
                _folderWatcher = new FolderWatcherService(_logger, _config);
                _folderWatcher.FolderCreated += OnFolderCreated;
                
                // Inject folder watcher into GCode processor for individual file tracking
                _gCodeProcessor.SetFolderWatcher(_folderWatcher);
                
                _logger.LogInfo($"STARTUP TIMING: Starting folder watcher at {orchestratorStopwatch.ElapsedMilliseconds}ms");
                _folderWatcher.Start();
                _logger.LogInfo($"STARTUP TIMING: Folder watcher started at {orchestratorStopwatch.ElapsedMilliseconds}ms");

                SetStatus("Running - Monitoring for new folders");
                _logger.LogInfo($"STARTUP TIMING: G-Code Sync orchestrator startup completed at {orchestratorStopwatch.ElapsedMilliseconds}ms. Monitoring: {_config.WatchFolder}");
            }
            catch (Exception ex)
            {
                SetStatus("Failed to start");
                _logger.LogError($"STARTUP TIMING: Failed to start sync orchestrator at {orchestratorStopwatch.ElapsedMilliseconds}ms", ex);
                throw;
            }
        }

        public async Task StopAsync()
        {
            try
            {
                if (!IsRunning)
                {
                    _logger.LogWarning("Sync orchestrator is not running");
                    return;
                }

                _logger.LogInfo("Stopping G-Code Sync orchestrator...");
                SetStatus("Stopping...");

                _folderWatcher?.Stop();
                _folderWatcher?.Dispose();
                _folderWatcher = null;

                SetStatus("Stopped");
                _logger.LogInfo("G-Code Sync orchestrator stopped");
            }
            catch (Exception ex)
            {
                SetStatus("Error during stop");
                _logger.LogError("Error stopping sync orchestrator", ex);
                throw;
            }
            
            await Task.CompletedTask;
        }

        public async Task<ProcessingResult> ProcessFolderManuallyAsync(string folderPath)
        {
            try
            {
                if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                {
                    var error = $"Invalid folder path: {folderPath}";
                    _logger.LogError(error);
                    return new ProcessingResult
                    {
                        Success = false,
                        Message = error,
                        StartTime = DateTime.Now,
                        EndTime = DateTime.Now
                    };
                }

                _logger.LogInfo($"Manual processing requested for: {folderPath}");
                return await ProcessFolderAsync(folderPath, isManual: true);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during manual folder processing: {folderPath}", ex);
                return new ProcessingResult
                {
                    Success = false,
                    Message = $"Manual processing failed: {ex.Message}",
                    Errors = { ex.ToString() },
                    StartTime = DateTime.Now,
                    EndTime = DateTime.Now
                };
            }
        }

        private async void OnFolderCreated(string folderPath)
        {
            try
            {
                await ProcessFolderAsync(folderPath, isManual: false);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing folder from watcher: {folderPath}", ex);
            }
        }

        private async Task<ProcessingResult> ProcessFolderAsync(string folderPath, bool isManual)
        {
            var processingType = isManual ? "Manual" : "Automatic";
            _logger.LogInfo($"{processingType} processing started for: {folderPath}");
            
            SetStatus($"Processing: {Path.GetFileName(folderPath)}");
            ProcessingStarted?.Invoke(folderPath);

            var result = await _gCodeProcessor.ProcessProjectFolderAsync(folderPath);

            if (result.Success && _config.AutoUploadAfterProcessing)
            {
                await UploadProcessedFolderAsync(folderPath, result);
            }

            SetStatus(IsRunning ? "Running - Monitoring for new folders" : "Stopped");
            ProcessingCompleted?.Invoke(folderPath, result);

            _logger.LogInfo($"{processingType} processing completed for: {folderPath} - Success: {result.Success}");
            
            return result;
        }

        private async Task UploadProcessedFolderAsync(string originalFolderPath, ProcessingResult processingResult)
        {
            try
            {
                SetStatus("Uploading to FTP...");
                _logger.LogInfo("Starting FTP upload...");

                string folderToUpload;
                string folderDisplayName;

                // Check if external script provided a specific output path
                if (!string.IsNullOrEmpty(processingResult.OutputPath) && Directory.Exists(processingResult.OutputPath))
                {
                    folderToUpload = processingResult.OutputPath;
                    folderDisplayName = Path.GetFileName(processingResult.OutputPath);
                    _logger.LogInfo($"Using external script output path for FTP upload: {folderToUpload}");
                }
                else
                {
                    // Standard processing: find the corresponding folder in the FTP upload directory
                    var projectName = Path.GetFileName(originalFolderPath);
                    var ftpFolders = Directory.GetDirectories(_config.FtpUploadFolder, $"{projectName}-*");

                    if (ftpFolders.Length == 0)
                    {
                        _logger.LogWarning($"No FTP upload folder found for project: {projectName}");
                        return;
                    }

                    // Upload the most recently created folder (should be the one we just processed)
                    var latestFolder = ftpFolders
                        .Select(f => new DirectoryInfo(f))
                        .OrderByDescending(d => d.CreationTime)
                        .First();
                    
                    folderToUpload = latestFolder.FullName;
                    folderDisplayName = latestFolder.Name;
                    _logger.LogInfo($"Using standard processing folder for FTP upload: {folderToUpload}");
                }

                var uploadSuccess = await _ftpService.UploadDirectoryAsync(folderToUpload);
                
                if (uploadSuccess)
                {
                    _logger.LogInfo($"FTP upload completed successfully: {folderDisplayName}");
                    
                    // Optionally, clean up the local FTP folder after successful upload
                    // Directory.Delete(folderToUpload, true);
                    // _logger.LogInfo($"Cleaned up local FTP folder: {folderDisplayName}");
                }
                else
                {
                    _logger.LogError($"FTP upload failed for: {folderDisplayName}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error during FTP upload", ex);
            }
        }

        private void EnsureDirectoriesExist()
        {
            var directories = new[] { _config.WatchFolder, _config.FtpUploadFolder };

            foreach (var directory in directories)
            {
                if (!Directory.Exists(directory))
                {
                    try
                    {
                        Directory.CreateDirectory(directory);
                        _logger.LogInfo($"Created directory: {directory}");
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException($"Failed to create directory: {directory}", ex);
                    }
                }
            }
        }

        private void SetStatus(string status)
        {
            _currentStatus = status;
            StatusChanged?.Invoke(status);
        }

        public void RefreshConfiguration()
        {
            _logger.LogInfo("Refreshing configuration and restarting folder watcher");
            
            // Reload configuration
            _config = _configService.LoadConfiguration();
            
            // Update folder watcher configuration and restart if it's running
            if (_folderWatcher?.IsRunning == true)
            {
                _folderWatcher.UpdateConfiguration(_config);
                _folderWatcher.Restart();
            }
        }

        public void Dispose()
        {
            _folderWatcher?.Dispose();
        }
    }
}