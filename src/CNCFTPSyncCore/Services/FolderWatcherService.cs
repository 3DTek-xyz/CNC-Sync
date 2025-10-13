using CNCFTPSyncCore.Models;
using CNCFTPSyncCore.Services;

namespace CNCFTPSyncCore.Services
{
    public interface IFolderWatcher : IDisposable
    {
        event Action<string> FolderCreated;
        void Start();
        void Stop();
        bool IsRunning { get; }
    }

    public class FolderWatcherService : IFolderWatcher, IDisposable
    {
        private readonly ILogService _logger;
        private readonly SyncConfiguration _config;
        private FileSystemWatcher? _watcher;
        private readonly Dictionary<string, DateTime> _pendingFolders = new();
        private readonly System.Timers.Timer _stabilityTimer;
        private readonly object _lockObject = new();

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

                _watcher = new FileSystemWatcher(_config.WatchFolder)
                {
                    NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true,
                    IncludeSubdirectories = false
                };

                _watcher.Created += OnFolderCreated;
                _watcher.Error += OnWatcherError;

                _stabilityTimer.Start();
                IsRunning = true;

                _logger.LogInfo($"Folder watcher started monitoring: {_config.WatchFolder}");
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
                }

                IsRunning = false;
                _logger.LogInfo("Folder watcher stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error stopping folder watcher", ex);
            }
        }

        private void OnFolderCreated(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType == WatcherChangeTypes.Created && Directory.Exists(e.FullPath))
            {
                lock (_lockObject)
                {
                    _pendingFolders[e.FullPath] = DateTime.Now;
                }
                
                _logger.LogInfo($"New folder detected: {e.FullPath} - waiting for stability");
            }
        }

        private void CheckFolderStability(object? sender, System.Timers.ElapsedEventArgs e)
        {
            lock (_lockObject)
            {
                var stableFolders = new List<string>();
                var currentTime = DateTime.Now;

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

        public void Dispose()
        {
            Stop();
            _stabilityTimer?.Dispose();
        }
    }
}