using System.Text;

namespace CNCSync.Infrastructure.Logging;

public static class DiagnosticLog
{
    private static readonly Lock FileLock = new();
    private static string? _logFilePath;

    public static string? LogFilePath => _logFilePath;

    public static void Initialize(string settingsFilePath)
    {
        var directory = Path.GetDirectoryName(settingsFilePath);
        var baseDirectory = string.IsNullOrWhiteSpace(directory) ? AppContext.BaseDirectory : directory;
        _logFilePath = Path.Combine(baseDirectory, "diagnostics.log");
    }

    public static void WriteInfo(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        WriteBlock("INFO", message);
    }

    public static void WriteException(string context, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(context))
        {
            builder.AppendLine(context);
        }

        builder.AppendLine(exception.ToString());
        WriteBlock("ERROR", builder.ToString().TrimEnd());
    }

    private static void WriteBlock(string level, string message)
    {
        try
        {
            var logFilePath = _logFilePath;
            if (string.IsNullOrWhiteSpace(logFilePath))
            {
                return;
            }

            var directory = Path.GetDirectoryName(logFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
            lock (FileLock)
            {
                File.AppendAllText(logFilePath, entry);
            }
        }
        catch
        {
            // Diagnostics must never crash the app.
        }
    }
}
