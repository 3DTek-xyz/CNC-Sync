using NLog;
using System.Text;

namespace GCodeSyncCore.Services
{
    public interface ILogService
    {
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message, Exception? exception = null);
        void LogDebug(string message);
        List<string> GetRecentLogs(int count = 100);
        event Action<string, string> LogEntryAdded; // level, message
    }

    public class LogService : ILogService
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly List<(DateTime timestamp, string level, string message)> _recentLogs = new();
        private readonly object _lockObject = new();
        private const int MaxRecentLogs = 1000;

        public event Action<string, string>? LogEntryAdded;

        public void LogInfo(string message)
        {
            Logger.Info(message);
            AddToRecentLogs("INFO", message);
            LogEntryAdded?.Invoke("INFO", message);
        }

        public void LogWarning(string message)
        {
            Logger.Warn(message);
            AddToRecentLogs("WARN", message);
            LogEntryAdded?.Invoke("WARN", message);
        }

        public void LogError(string message, Exception? exception = null)
        {
            if (exception != null)
            {
                Logger.Error(exception, message);
                message = $"{message} - Exception: {exception.Message}";
            }
            else
            {
                Logger.Error(message);
            }
            
            AddToRecentLogs("ERROR", message);
            LogEntryAdded?.Invoke("ERROR", message);
        }

        public void LogDebug(string message)
        {
            Logger.Debug(message);
            AddToRecentLogs("DEBUG", message);
            LogEntryAdded?.Invoke("DEBUG", message);
        }

        private void AddToRecentLogs(string level, string message)
        {
            lock (_lockObject)
            {
                _recentLogs.Add((DateTime.Now, level, message));
                
                // Keep only the most recent logs
                if (_recentLogs.Count > MaxRecentLogs)
                {
                    _recentLogs.RemoveAt(0);
                }
            }
        }

        public List<string> GetRecentLogs(int count = 100)
        {
            lock (_lockObject)
            {
                return _recentLogs
                    .TakeLast(count)
                    .Select(log => $"{log.timestamp:yyyy-MM-dd HH:mm:ss} [{log.level}] {log.message}")
                    .ToList();
            }
        }
    }
}