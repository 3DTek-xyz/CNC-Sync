using System.Runtime.Versioning;
using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace CBWSSSync.App.Services;

public sealed class LoginStartupService : ILoginStartupService
{
    private const string WindowsRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string WindowsRunValueName = "CNC Sync";
    private const string LinuxDesktopFileName = "cnc-sync.desktop";

    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows())
        {
            return Task.FromResult(IsWindowsEnabled());
        }

        if (OperatingSystem.IsMacOS())
        {
            return Task.FromResult(IsMacEnabled());
        }

        if (OperatingSystem.IsLinux())
        {
            return Task.FromResult(IsLinuxEnabled());
        }

        return Task.FromResult(false);
    }

    public Task ApplyAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows())
        {
            ApplyWindows(enabled);
            return Task.CompletedTask;
        }

        if (OperatingSystem.IsMacOS())
        {
            ApplyMac(enabled);
            return Task.CompletedTask;
        }

        if (OperatingSystem.IsLinux())
        {
            ApplyLinux(enabled);
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyWindows(bool enabled)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(WindowsRunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Unable to open the Windows startup registry key.");

        if (!enabled)
        {
            runKey.DeleteValue(WindowsRunValueName, throwOnMissingValue: false);
            return;
        }

        var processPath = RequireProcessPath();
        var startupCommand = $"\"{processPath}\" {AppLaunchArguments.LaunchAtLoginArgument}";
        runKey.SetValue(WindowsRunValueName, startupCommand, RegistryValueKind.String);
    }

    [SupportedOSPlatform("windows")]
    private static bool IsWindowsEnabled()
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(WindowsRunKeyPath, writable: false);
        var configuredValue = runKey?.GetValue(WindowsRunValueName) as string;
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return false;
        }

        return configuredValue.Contains(RequireProcessPath(), StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyMac(bool enabled)
    {
        var appPath = ResolveMacLoginItemPath();
        var escapedName = EscapeAppleScriptString("CNC Sync");
        var escapedPath = EscapeAppleScriptString(appPath);

        RunAppleScript(
            $"tell application \"System Events\" to delete every login item whose name is \"{escapedName}\"");

        if (!enabled)
        {
            return;
        }

        RunAppleScript(
            "tell application \"System Events\"",
            $"make login item at end with properties {{name:\"{escapedName}\", path:\"{escapedPath}\", hidden:false}}",
            "end tell");
    }

    private static bool IsMacEnabled()
    {
        var appPath = ResolveMacLoginItemPath();
        var escapedPath = EscapeAppleScriptString(appPath);
        var result = RunAppleScriptWithOutput(
            "tell application \"System Events\"",
            $"get the count of (every login item whose path is \"{escapedPath}\")",
            "end tell");

        return string.Equals(result.Trim(), "1", StringComparison.Ordinal);
    }

    private static void ApplyLinux(bool enabled)
    {
        var autostartDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Personal),
            ".config",
            "autostart");
        Directory.CreateDirectory(autostartDir);

        var desktopFilePath = Path.Combine(autostartDir, LinuxDesktopFileName);
        if (!enabled)
        {
            if (File.Exists(desktopFilePath))
            {
                File.Delete(desktopFilePath);
            }

            return;
        }

        var processPath = RequireProcessPath();
        var command = $"{EscapeDesktopExecArgument(processPath)} {EscapeDesktopExecArgument(AppLaunchArguments.LaunchAtLoginArgument)}";
        var desktopEntry = $$"""
[Desktop Entry]
Type=Application
Version=1.0
Name=CNC Sync
Comment=Launch CNC Sync when you log in
Exec={{command}}
Terminal=false
X-GNOME-Autostart-enabled=true
""";
        File.WriteAllText(desktopFilePath, desktopEntry, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static bool IsLinuxEnabled()
    {
        var autostartDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Personal),
            ".config",
            "autostart");
        var desktopFilePath = Path.Combine(autostartDir, LinuxDesktopFileName);
        if (!File.Exists(desktopFilePath))
        {
            return false;
        }

        var fileContents = File.ReadAllText(desktopFilePath);
        return fileContents.Contains(RequireProcessPath(), StringComparison.Ordinal);
    }

    private static string RequireProcessPath()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new InvalidOperationException("Unable to determine the current application path for startup registration.");
        }

        return processPath;
    }

    private static string ResolveMacLoginItemPath()
    {
        var processPath = RequireProcessPath();
        var directory = new DirectoryInfo(Path.GetDirectoryName(processPath) ?? processPath);
        while (directory is not null)
        {
            if (directory.Extension.Equals(".app", StringComparison.OrdinalIgnoreCase))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return processPath;
    }

    private static void RunAppleScript(params string[] scriptLines)
    {
        RunAppleScriptInternal(scriptLines, captureOutput: false);
    }

    private static string RunAppleScriptWithOutput(params string[] scriptLines)
    {
        return RunAppleScriptInternal(scriptLines, captureOutput: true);
    }

    private static string RunAppleScriptInternal(string[] scriptLines, bool captureOutput)
    {
        var startInfo = new ProcessStartInfo("/usr/bin/osascript")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        foreach (var line in scriptLines)
        {
            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add(line);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start osascript for macOS login item registration.");

        var standardOutput = captureOutput ? process.StandardOutput.ReadToEnd() : string.Empty;
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(standardError)
                    ? "macOS login item registration failed."
                    : $"macOS login item registration failed: {standardError.Trim()}");
        }

        return standardOutput;
    }

    private static string EscapeAppleScriptString(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string EscapeDesktopExecArgument(string value)
    {
        var escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("$", "\\$", StringComparison.Ordinal);

        return $"\"{escaped}\"";
    }
}
