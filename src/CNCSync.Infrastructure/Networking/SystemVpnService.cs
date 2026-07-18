using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using CNCSync.Core.Services;

namespace CNCSync.Infrastructure.Networking;

public sealed partial class SystemVpnService : IVpnService
{
    private const int ConnectPollAttempts = 20;
    private static readonly TimeSpan ConnectPollInterval = TimeSpan.FromMilliseconds(500);

    public async Task<IReadOnlyList<VpnConnectionInfo>> ListConnectionsAsync(CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsMacOS())
        {
            return await ListMacConnectionsAsync(cancellationToken);
        }

        if (OperatingSystem.IsWindows())
        {
            return await ListWindowsConnectionsAsync(cancellationToken);
        }

        if (OperatingSystem.IsLinux())
        {
            return await ListLinuxConnectionsAsync(cancellationToken);
        }

        return [];
    }

    public async Task<VpnConnectionEnsureResult> EnsureConnectedAsync(string connectionName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionName))
        {
            return VpnConnectionEnsureResult.NoRequirement();
        }

        var connections = await ListConnectionsAsync(cancellationToken);
        var connection = connections.FirstOrDefault(item =>
            string.Equals(item.Name, connectionName, StringComparison.OrdinalIgnoreCase));
        if (connection is null)
        {
            return new VpnConnectionEnsureResult
            {
                Success = false,
                ConnectedNow = false,
                Message = $"Required VPN '{connectionName}' is not configured on this machine."
            };
        }

        if (connection.IsConnected)
        {
            return new VpnConnectionEnsureResult
            {
                Success = true,
                ConnectedNow = true
            };
        }

        var connectResult = OperatingSystem.IsMacOS()
            ? await RunProcessAsync("/usr/sbin/scutil", ["--nc", "start", connection.Identifier], cancellationToken)
            : OperatingSystem.IsWindows()
                ? await RunProcessAsync("rasdial", [connection.Name], cancellationToken)
                : await RunProcessAsync("nmcli", ["connection", "up", "id", connection.Name], cancellationToken);

        if (connectResult.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(connectResult.StandardError) ? connectResult.StandardOutput : connectResult.StandardError;
            return new VpnConnectionEnsureResult
            {
                Success = false,
                ConnectedNow = false,
                Message = $"Could not connect required VPN '{connection.Name}': {error.Trim()} VPN profiles used by ProCut Suite Desktop must be able to connect automatically without prompting for user interaction."
            };
        }

        for (var attempt = 0; attempt < ConnectPollAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(ConnectPollInterval, cancellationToken);
            var refreshedConnection = (await ListConnectionsAsync(cancellationToken)).FirstOrDefault(item =>
                string.Equals(item.Name, connection.Name, StringComparison.OrdinalIgnoreCase));
            if (refreshedConnection?.IsConnected == true)
            {
                return new VpnConnectionEnsureResult
                {
                    Success = true,
                    ConnectedNow = true,
                    ConnectionStateChanged = true,
                    Message = $"Connected required VPN '{connection.Name}'."
                };
            }
        }

        return new VpnConnectionEnsureResult
        {
            Success = false,
            ConnectedNow = false,
            Message = $"VPN '{connection.Name}' did not report as connected within {ConnectPollAttempts * ConnectPollInterval.TotalSeconds:0} seconds of the connect request. VPN profiles used by ProCut Suite Desktop must be able to connect automatically without prompting for user interaction."
        };
    }

    public async Task<(bool Success, string Message)> DisconnectAsync(string connectionName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionName))
        {
            return (true, string.Empty);
        }

        var connections = await ListConnectionsAsync(cancellationToken);
        var connection = connections.FirstOrDefault(item =>
            string.Equals(item.Name, connectionName, StringComparison.OrdinalIgnoreCase));
        if (connection is null)
        {
            return (false, $"Required VPN '{connectionName}' is not configured on this machine.");
        }

        if (!connection.IsConnected)
        {
            return (true, $"Required VPN '{connection.Name}' was already disconnected.");
        }

        var disconnectResult = OperatingSystem.IsMacOS()
            ? await RunProcessAsync("/usr/sbin/scutil", ["--nc", "stop", connection.Identifier], cancellationToken)
            : OperatingSystem.IsWindows()
                ? await RunProcessAsync("rasdial", [connection.Name, "/disconnect"], cancellationToken)
                : await RunProcessAsync("nmcli", ["connection", "down", "id", connection.Name], cancellationToken);

        if (disconnectResult.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(disconnectResult.StandardError) ? disconnectResult.StandardOutput : disconnectResult.StandardError;
            return (false, $"Could not disconnect required VPN '{connection.Name}': {error.Trim()}");
        }

        return (true, $"Disconnected required VPN '{connection.Name}'.");
    }

    private static async Task<IReadOnlyList<VpnConnectionInfo>> ListMacConnectionsAsync(CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync("/usr/sbin/scutil", ["--nc", "list"], cancellationToken);
        if (result.ExitCode != 0)
        {
            return [];
        }

        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseMacConnection)
            .Where(item => item is not null)
            .Cast<VpnConnectionInfo>()
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static VpnConnectionInfo? ParseMacConnection(string line)
    {
        var match = MacVpnListRegex().Match(line);
        if (!match.Success)
        {
            return null;
        }

        var status = match.Groups["status"].Value;
        return new VpnConnectionInfo
        {
            Identifier = match.Groups["id"].Value,
            Name = match.Groups["name"].Value,
            IsConnected = string.Equals(status.Trim(), "Connected", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static async Task<IReadOnlyList<VpnConnectionInfo>> ListWindowsConnectionsAsync(CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(
            "powershell",
            ["-NoProfile", "-Command", "Get-VpnConnection | Select-Object Name,ConnectionStatus | ConvertTo-Json -Compress"],
            cancellationToken);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return [];
        }

        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        var elements = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().ToList()
            : [root];

        return elements
            .Where(item => item.TryGetProperty("Name", out _))
            .Select(item =>
            {
                var name = item.GetProperty("Name").GetString() ?? string.Empty;
                var status = item.TryGetProperty("ConnectionStatus", out var statusElement)
                    ? statusElement.GetString() ?? string.Empty
                    : string.Empty;
                return new VpnConnectionInfo
                {
                    Identifier = name,
                    Name = name,
                    IsConnected = string.Equals(status, "Connected", StringComparison.OrdinalIgnoreCase)
                };
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<IReadOnlyList<VpnConnectionInfo>> ListLinuxConnectionsAsync(CancellationToken cancellationToken)
    {
        var configuredResult = await RunProcessAsync("nmcli", ["-t", "-f", "NAME,TYPE", "connection", "show"], cancellationToken);
        if (configuredResult.ExitCode != 0)
        {
            return [];
        }

        var activeResult = await RunProcessAsync("nmcli", ["-t", "-f", "NAME,TYPE", "connection", "show", "--active"], cancellationToken);
        var activeNames = ParseNmcliConnections(activeResult.StandardOutput)
            .Where(item => string.Equals(item.Type, "vpn", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return ParseNmcliConnections(configuredResult.StandardOutput)
            .Where(item => string.Equals(item.Type, "vpn", StringComparison.OrdinalIgnoreCase))
            .Select(item => new VpnConnectionInfo
            {
                Identifier = item.Name,
                Name = item.Name,
                IsConnected = activeNames.Contains(item.Name)
            })
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<(string Name, string Type)> ParseNmcliConnections(string output)
    {
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line =>
            {
                var separatorIndex = line.LastIndexOf(':');
                return separatorIndex <= 0
                    ? (Name: string.Empty, Type: string.Empty)
                    : (Name: line[..separatorIndex], Type: line[(separatorIndex + 1)..]);
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToList();
    }

    private static async Task<ProcessResult> RunProcessAsync(string fileName, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new ProcessResult(process.ExitCode, await standardOutputTask, await standardErrorTask);
    }

    [GeneratedRegex(@"^\*?\s*\((?<status>[^)]+)\)\s+(?<id>[A-Za-z0-9-]+)\s+.+?""(?<name>[^""]+)""", RegexOptions.Compiled)]
    private static partial Regex MacVpnListRegex();

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
