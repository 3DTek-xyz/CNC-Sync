using System.Text;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace CBWSSSync.App.Services;

public sealed class LoginStartupService : ILoginStartupService
{
    private const string WindowsRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string WindowsRunValueName = "CNC Sync";
    private const string MacLaunchAgentId = "com.3dtek.cncsync";
    private const string LinuxDesktopFileName = "cnc-sync.desktop";

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

    private static void ApplyMac(bool enabled)
    {
        var launchAgentsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Personal),
            "Library",
            "LaunchAgents");
        Directory.CreateDirectory(launchAgentsDir);

        var plistPath = Path.Combine(launchAgentsDir, $"{MacLaunchAgentId}.plist");
        if (!enabled)
        {
            if (File.Exists(plistPath))
            {
                File.Delete(plistPath);
            }

            return;
        }

        var processPath = RequireProcessPath();
        var escapedProcessPath = EscapeXml(processPath);
        var escapedArgument = EscapeXml(AppLaunchArguments.LaunchAtLoginArgument);
        var plist = $$"""
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
  <dict>
    <key>Label</key>
    <string>{{MacLaunchAgentId}}</string>
    <key>ProgramArguments</key>
    <array>
      <string>{{escapedProcessPath}}</string>
      <string>{{escapedArgument}}</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
  </dict>
</plist>
""";
        File.WriteAllText(plistPath, plist, Encoding.UTF8);
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

    private static string RequireProcessPath()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new InvalidOperationException("Unable to determine the current application path for startup registration.");
        }

        return processPath;
    }

    private static string EscapeXml(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);

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
