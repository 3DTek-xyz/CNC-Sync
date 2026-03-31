using System.Runtime.Versioning;
using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CNCSync.App.Services;

public sealed class LoginStartupService : ILoginStartupService
{
    private const string WindowsRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string WindowsRunValueName = "CNC Sync";
    private const string LinuxDesktopFileName = "cnc-sync.desktop";
    private const string MacLoginItemBridgeName = "libcncsync-login-item-bridge.dylib";

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
        var code = enabled ? MacLoginItemBridge.Enable() : MacLoginItemBridge.Disable();
        if (code is not 1 and not 0)
        {
            throw new InvalidOperationException(GetMacBridgeErrorMessage(code));
        }
    }

    private static bool IsMacEnabled()
    {
        return MacLoginItemBridge.Status() == 1;
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

        var processPath = RequireLinuxStartupPath();
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
        if (!fileContents.Contains(AppLaunchArguments.LaunchAtLoginArgument, StringComparison.Ordinal))
        {
            return false;
        }

        var configuredStartupPath = TryExtractDesktopExecPath(fileContents);
        if (string.IsNullOrWhiteSpace(configuredStartupPath))
        {
            return false;
        }

        return File.Exists(configuredStartupPath);
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

    private static string RequireLinuxStartupPath()
    {
        var appImagePath = Environment.GetEnvironmentVariable("APPIMAGE");
        if (!string.IsNullOrWhiteSpace(appImagePath) && File.Exists(appImagePath))
        {
            return appImagePath;
        }

        return RequireProcessPath();
    }

    private static string EscapeDesktopExecArgument(string value)
    {
        var escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("$", "\\$", StringComparison.Ordinal);

        return $"\"{escaped}\"";
    }

    private static string? TryExtractDesktopExecPath(string desktopEntryContents)
    {
        using var reader = new StringReader(desktopEntryContents);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!line.StartsWith("Exec=", StringComparison.Ordinal))
            {
                continue;
            }

            var execValue = line["Exec=".Length..].Trim();
            if (execValue.Length == 0)
            {
                return null;
            }

            if (execValue[0] == '"')
            {
                var closingQuoteIndex = FindClosingQuote(execValue, 1);
                if (closingQuoteIndex <= 1)
                {
                    return null;
                }

                return UnescapeDesktopExecArgument(execValue[1..closingQuoteIndex]);
            }

            var firstSpaceIndex = execValue.IndexOf(' ');
            var rawPath = firstSpaceIndex >= 0 ? execValue[..firstSpaceIndex] : execValue;
            return UnescapeDesktopExecArgument(rawPath);
        }

        return null;
    }

    private static int FindClosingQuote(string value, int startIndex)
    {
        for (var i = startIndex; i < value.Length; i++)
        {
            if (value[i] == '"' && value[i - 1] != '\\')
            {
                return i;
            }
        }

        return -1;
    }

    private static string UnescapeDesktopExecArgument(string value)
    {
        return value
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\$", "$", StringComparison.Ordinal)
            .Replace("\\`", "`", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    private static string GetMacBridgeErrorMessage(int code)
    {
        var detail = MacLoginItemBridge.CopyLastError();
        return code switch
        {
            -2 => string.IsNullOrWhiteSpace(detail)
                ? "macOS login items require macOS 13 or later."
                : detail,
            2 => "macOS requires approval before CNC Sync can be enabled as a login item.",
            _ => string.IsNullOrWhiteSpace(detail)
                ? "macOS login item registration failed."
                : $"macOS login item registration failed: {detail}"
        };
    }

    private static class MacLoginItemBridge
    {
        private delegate int StatusDelegate();
        private delegate int ToggleDelegate();
        private delegate IntPtr CopyLastErrorDelegate();
        private delegate void FreeStringDelegate(IntPtr pointer);

        private static readonly object Sync = new();
        private static IntPtr _libraryHandle;
        private static StatusDelegate? _status;
        private static ToggleDelegate? _enable;
        private static ToggleDelegate? _disable;
        private static CopyLastErrorDelegate? _copyLastError;
        private static FreeStringDelegate? _freeString;

        public static int Status()
        {
            EnsureLoaded();
            return _status!();
        }

        public static int Enable()
        {
            EnsureLoaded();
            return _enable!();
        }

        public static int Disable()
        {
            EnsureLoaded();
            return _disable!();
        }

        public static string? CopyLastError()
        {
            EnsureLoaded();
            var pointer = _copyLastError!();
            if (pointer == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringAnsi(pointer);
            }
            finally
            {
                _freeString!(pointer);
            }
        }

        private static void EnsureLoaded()
        {
            lock (Sync)
            {
                if (_libraryHandle != IntPtr.Zero)
                {
                    return;
                }

                var processPath = RequireProcessPath();
                var processDirectory = Path.GetDirectoryName(processPath);
                if (string.IsNullOrWhiteSpace(processDirectory))
                {
                    throw new InvalidOperationException("Unable to determine the app directory for macOS login item registration.");
                }

                var bridgePath = Path.Combine(processDirectory, MacLoginItemBridgeName);
                if (!File.Exists(bridgePath))
                {
                    throw new InvalidOperationException($"macOS login item bridge was not found at {bridgePath}.");
                }

                _libraryHandle = NativeLibrary.Load(bridgePath);
                _status = GetDelegate<StatusDelegate>("cnc_sync_login_item_status");
                _enable = GetDelegate<ToggleDelegate>("cnc_sync_login_item_enable");
                _disable = GetDelegate<ToggleDelegate>("cnc_sync_login_item_disable");
                _copyLastError = GetDelegate<CopyLastErrorDelegate>("cnc_sync_login_item_copy_last_error");
                _freeString = GetDelegate<FreeStringDelegate>("cnc_sync_login_item_free_string");
            }
        }

        private static T GetDelegate<T>(string exportName) where T : Delegate
        {
            var symbol = NativeLibrary.GetExport(_libraryHandle, exportName);
            return Marshal.GetDelegateForFunctionPointer<T>(symbol);
        }
    }
}
