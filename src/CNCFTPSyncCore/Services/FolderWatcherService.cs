using CNCFTPSyncCore.Models;
using CNCFTPSyncCore.Services;

namespace CNCFTPSyncCore.Services
{
    public interface IFolderWatcher : IDisposable
    {
        event Action<string> FolderCreated;
        void Start();
        void Stop();
        void Restart(); // Add restart method for config changes
        void UpdateConfiguration(SyncConfiguration newConfig); // Add method to update config  
        bool IsRunning { get; }
        DateTime? GetRootFolderTimestamp(string folderPath);
        void ClearRootFolderTimestamp(string folderPath);
    }

    public class FolderWatcherService : IFolderWatcher, IDisposable
    {
        private readonly ILogService _logger;
        private SyncConfiguration _config; // Remove readonly to allow updates
        private FileSystemWatcher? _watcher;
        private readonly Dictionary<string, DateTime> _pendingFolders = new(); // For folder-based processing (Mozaik modes)
        private readonly Dictionary<string, DateTime> _rootFolderTimestamps = new(); // For root-level timestamp tracking (Simple FTP)
        private readonly Dictionary<string, DateTime> _processingFolderTimestamps = new(); // Store timestamps for folders being processed
        private readonly System.Timers.Timer _stabilityTimer;
        private readonly object _lockObject = new();
        private bool _isSimpleFtpMode;

        public event Action<string>? FolderCreated;
        public bool IsRunning { get; private set; }

        public FolderWatcherService(ILogService logger, SyncConfiguration config)
        {
            _logger = logger;
            _config = config;
            _stabilityTimer = new System.Timers.Timer(_config.FileStabilityCheckIntervalSeconds * 1000);
            _stabilityTimer.Elapsed += CheckFolderStability;
        }

        public void Start()
        {
            if (IsRunning)
                return;

            try
            {
                if (string.IsNullOrEmpty(_config.WatchFolder) || !Directory.Exists(_config.WatchFolder))
                {
                    throw new InvalidOperationException($"Watch folder does not exist: {_config.WatchFolder}");
                }

                // Check if we're using Simple FTP Upload mode for file watching
                _isSimpleFtpMode = _config.InternalProcessingType == "Simple FTP Upload";
                
                _logger.LogInfo($"=== FOLDER WATCHER DEBUG ===");
                _logger.LogInfo($"Internal Processing Type: '{_config.InternalProcessingType}'");
                _logger.LogInfo($"Is Simple FTP Mode: {_isSimpleFtpMode}");
                _logger.LogInfo($"Expected Simple FTP string: 'Simple FTP Upload'");
                _logger.LogInfo($"String comparison result: {_config.InternalProcessingType == "Simple FTP Upload"}");
                _logger.LogInfo($"===============================");
                
                var notifyFilter = _isSimpleFtpMode 
                    ? NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite
                    : NotifyFilters.DirectoryName | NotifyFilters.CreationTime;
                
                _watcher = new FileSystemWatcher(_config.WatchFolder)
                {
                    NotifyFilter = notifyFilter,
                    EnableRaisingEvents = true,
                    IncludeSubdirectories = _isSimpleFtpMode, // Only monitor subdirectories in Simple FTP mode
                    InternalBufferSize = 8192 * 16 // Increase buffer size from default 8KB to 128KB
                };
                
                _logger.LogInfo($"FileSystemWatcher NotifyFilter: {notifyFilter}");
                _logger.LogInfo($"FileSystemWatcher IncludeSubdirectories: {_isSimpleFtpMode}");

                _watcher.Created += OnItemCreated;
                _watcher.Error += OnWatcherError;

                _stabilityTimer.Start();
                IsRunning = true;

                _logger.LogInfo($"Folder watcher started monitoring: {_config.WatchFolder}");
                _logger.LogInfo($"Simple FTP mode enabled: {_isSimpleFtpMode}");
                _logger.LogInfo($"NotifyFilter settings: {_watcher.NotifyFilter}");
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to start folder watcher", ex);
                throw;
            }
        }

        public void Stop()
        {
            if (!IsRunning)
                return;

            try
            {
                _stabilityTimer?.Stop();
                _watcher?.Dispose();
                _watcher = null;
                
                lock (_lockObject)
                {
                    _pendingFolders.Clear();
                    _rootFolderTimestamps.Clear();
                }

                IsRunning = false;
                _logger.LogInfo("Folder watcher stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error stopping folder watcher", ex);
            }
        }

        private void OnItemCreated(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType != WatcherChangeTypes.Created)
                return;

            if (_isSimpleFtpMode)
            {
                // Minimal processing for Simple FTP mode - just capture timestamp
                lock (_lockObject)
                {
                    var rootFolder = GetRootLevelFolder(e.FullPath);
                    
                    // Only set timestamp if this is the first activity in this root folder
                    if (!_rootFolderTimestamps.ContainsKey(rootFolder))
                    {
                        _rootFolderTimestamps[rootFolder] = DateTime.Now;
                        _logger.LogInfo($"Simple FTP: Root folder activity started: {rootFolder}");
                    }
                }
                return;
            }

            // Full processing for Mozaik modes
            _logger.LogInfo($"=== FILE SYSTEM EVENT DEBUG ===");
            _logger.LogInfo($"Event Type: {e.ChangeType}");
            _logger.LogInfo($"Event Path: {e.FullPath}");
            _logger.LogInfo($"Simple FTP Mode: {_isSimpleFtpMode}");
            
            // Check what type of item this is
            bool isDirectory = Directory.Exists(e.FullPath);
            bool isFile = File.Exists(e.FullPath);
            
            _logger.LogInfo($"Path Analysis:");
            _logger.LogInfo($"  - Directory.Exists: {isDirectory}");
            _logger.LogInfo($"  - File.Exists: {isFile}");
            _logger.LogInfo($"  - Path exists at all: {isDirectory || isFile}");
            
            // Add a small delay and recheck if neither exists initially
            if (!isDirectory && !isFile)
            {
                _logger.LogInfo($"Neither directory nor file detected initially, waiting 100ms and rechecking...");
                Thread.Sleep(100);
                isDirectory = Directory.Exists(e.FullPath);
                isFile = File.Exists(e.FullPath);
                _logger.LogInfo($"After delay - Directory: {isDirectory}, File: {isFile}");
            }

            lock (_lockObject)
            {
                if (isDirectory)
                {
                    _logger.LogInfo($"Processing as DIRECTORY");
                    // For Mozaik modes, use the existing folder-based processing
                    _pendingFolders[e.FullPath] = DateTime.Now;
                    _logger.LogInfo($"New folder detected (Mozaik mode): {e.FullPath} - waiting for stability");
                }
                else if (isFile)
                {
                    _logger.LogInfo($"Processing as FILE");
                    _logger.LogInfo($"File creation ignored (not in Simple FTP mode): {e.FullPath}");
                }
                else
                {
                    _logger.LogWarning($"Path exists but is neither directory nor file: {e.FullPath}");
                }
            }
            
            _logger.LogInfo($"=== END FILE SYSTEM EVENT DEBUG ===");
        }

        private void CheckFolderStability(object? sender, System.Timers.ElapsedEventArgs e)
        {
            lock (_lockObject)
            {
                var currentTime = DateTime.Now;

                if (_isSimpleFtpMode)
                {
                    // Handle root-level timestamp tracking for Simple FTP mode
                    var stableRootFolders = new List<string>();

                    // Check each root folder for stability (last activity + stability delay)
                    foreach (var kvp in _rootFolderTimestamps.ToList())
                    {
                        var rootFolderPath = kvp.Key;
                        var firstActivityTime = kvp.Value;

                        // For stability, we need to check if there's been no recent activity
                        // We'll use a simple approach: if enough time has passed since the first activity
                        // and the folder still exists, consider it stable
                        if (currentTime.Subtract(firstActivityTime).TotalSeconds >= _config.FileStabilityDelaySeconds)
                        {
                            if (Directory.Exists(rootFolderPath))
                            {
                                stableRootFolders.Add(rootFolderPath);
                            }
                            else
                            {
                                // Root folder was deleted, remove from tracking
                                _rootFolderTimestamps.Remove(rootFolderPath);
                                _logger.LogWarning($"Root folder was deleted: {rootFolderPath}");
                            }
                        }
                    }

                    // Process stable root folders
                    foreach (var stableRootFolder in stableRootFolders)
                    {
                        var timestamp = _rootFolderTimestamps[stableRootFolder];
                        
                        // Move timestamp to processing dictionary and remove from active tracking
                        // This prevents multiple processing events for the same folder
                        _processingFolderTimestamps[stableRootFolder] = timestamp;
                        _rootFolderTimestamps.Remove(stableRootFolder);

                        _logger.LogInfo($"Root folder is stable and ready for timestamp-based processing: {stableRootFolder} (files since {timestamp})");
                        
                        // Pass the root folder to processor - it will scan for files created since timestamp
                        Task.Run(() => FolderCreated?.Invoke(stableRootFolder));
                    }
                }
                else
                {
                    // Handle folder-based processing for Mozaik modes (existing logic)
                    var stableFolders = new List<string>();

                    foreach (var kvp in _pendingFolders.ToList())
                    {
                        var folderPath = kvp.Key;
                        var detectionTime = kvp.Value;

                        // Check if enough time has passed
                        if (currentTime.Subtract(detectionTime).TotalSeconds >= _config.FileStabilityDelaySeconds)
                        {
                            // Check if folder still exists and appears stable
                            if (Directory.Exists(folderPath) && IsFolderStable(folderPath))
                            {
                                stableFolders.Add(folderPath);
                            }
                            else if (!Directory.Exists(folderPath))
                            {
                                // Folder was deleted, remove from pending
                                _pendingFolders.Remove(folderPath);
                                _logger.LogWarning($"Pending folder was deleted: {folderPath}");
                            }
                        }
                    }

                    // Process stable folders
                    foreach (var stableFolder in stableFolders)
                    {
                        _pendingFolders.Remove(stableFolder);
                        _logger.LogInfo($"Folder is stable and ready for processing: {stableFolder}");
                        
                        // Trigger processing on a background thread
                        Task.Run(() => FolderCreated?.Invoke(stableFolder));
                    }
                }
            }
        }

        private bool IsFolderStable(string folderPath)
        {
            try
            {
                // Get current file count and sizes
                var files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);
                var currentState = new
                {
                    FileCount = files.Length,
                    TotalSize = files.Sum(f =>
                    {
                        try { return new FileInfo(f).Length; }
                        catch { return 0; }
                    })
                };

                // Wait a short time and check again
                Thread.Sleep(1000);

                var filesAfter = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);
                var laterState = new
                {
                    FileCount = filesAfter.Length,
                    TotalSize = filesAfter.Sum(f =>
                    {
                        try { return new FileInfo(f).Length; }
                        catch { return 0; }
                    })
                };

                // Folder is stable if file count and total size haven't changed
                return currentState.FileCount == laterState.FileCount && 
                       currentState.TotalSize == laterState.TotalSize;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error checking folder stability for {folderPath}: {ex.Message}");
                return false;
            }
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            _logger.LogError("FileSystemWatcher error occurred", e.GetException());
            
            // Try to restart the watcher
            Task.Run(() =>
            {
                Thread.Sleep(5000); // Wait 5 seconds before restart
                try
                {
                    Stop();
                    Start();
                    _logger.LogInfo("FileSystemWatcher restarted after error");
                }
                catch (Exception ex)
                {
                    _logger.LogError("Failed to restart FileSystemWatcher", ex);
                }
            });
        }



        public DateTime? GetRootFolderTimestamp(string folderPath)
        {
            lock (_lockObject)
            {
                // First check if folder is actively being processed
                if (_processingFolderTimestamps.TryGetValue(folderPath, out var processingTimestamp))
                {
                    return processingTimestamp;
                }
                
                // Fall back to checking if folder is still in initial tracking
                return _rootFolderTimestamps.TryGetValue(folderPath, out var timestamp) ? timestamp : null;
            }
        }

        public void ClearRootFolderTimestamp(string folderPath)
        {
            lock (_lockObject)
            {
                _rootFolderTimestamps.Remove(folderPath);
                _processingFolderTimestamps.Remove(folderPath);
            }
        }

        private string GetRootLevelFolder(string path)
        {
            // Find the immediate child folder of the watch directory
            var relativePath = Path.GetRelativePath(_config.WatchFolder, path);
            
            // If the path is directly in the watch folder, return the watch folder itself
            // (This handles files created directly in the root watch directory)
            if (!relativePath.Contains(Path.DirectorySeparatorChar))
            {
                return _config.WatchFolder;
            }
            
            // Otherwise, return the root-level folder
            var rootFolderName = relativePath.Split(Path.DirectorySeparatorChar)[0];
            return Path.Combine(_config.WatchFolder, rootFolderName);
        }

        public void Restart()
        {
            _logger.LogInfo("Restarting folder watcher due to configuration change");
            Stop();
            Start();
        }

        public void UpdateConfiguration(SyncConfiguration newConfig)
        {
            _config = newConfig;
            _logger.LogInfo("Updated folder watcher configuration");
        }

        public void Dispose()
        {
            Stop();
            _stabilityTimer?.Dispose();
        }
    }
}