using System.Diagnostics;
using CNCSync.Core.Configuration;

namespace CNCSync.Infrastructure.Configuration;

public sealed class MacKeychainSecretStore : ISecretStore
{
    private const string ServiceName = "CNC Sync";
    private const string AccountPrefix = "destination-password:";

    public string? GetSecret(string key)
    {
        var result = RunSecurity(
            "find-generic-password",
            "-s", ServiceName,
            "-a", BuildAccount(key),
            "-w");

        return result.ExitCode == 0
            ? result.StandardOutput.TrimEnd('\r', '\n')
            : null;
    }

    public void SetSecret(string key, string secret)
    {
        var result = RunSecurity(
            "add-generic-password",
            "-U",
            "-s", ServiceName,
            "-a", BuildAccount(key),
            "-w", secret);

        if (result.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            throw new InvalidOperationException($"Could not store password in macOS Keychain: {message.Trim()}");
        }
    }

    public void DeleteSecret(string key)
    {
        var result = RunSecurity(
            "delete-generic-password",
            "-s", ServiceName,
            "-a", BuildAccount(key));

        if (result.ExitCode != 0 && !result.StandardError.Contains("could not be found", StringComparison.OrdinalIgnoreCase))
        {
            var message = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            throw new InvalidOperationException($"Could not remove password from macOS Keychain: {message.Trim()}");
        }
    }

    private static string BuildAccount(string key) => $"{AccountPrefix}{key}";

    private static ProcessResult RunSecurity(string command, params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/security",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(command);
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, standardOutput, standardError);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
