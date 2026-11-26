using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using CNCSync.Core.Configuration;

namespace CNCSync.Infrastructure.Configuration;

internal sealed class LinuxPrintStationSecretStore : ISecretStore
{
    private const string SecretTool = "/usr/bin/secret-tool";
    private const string Application = "procutsuite-desktop-print";
    private const string Label = "ProCut Suite Desktop Print Sharing";
    private readonly Func<IReadOnlyList<string>, string?, CommandResult> _run;

    public LinuxPrintStationSecretStore(Func<IReadOnlyList<string>, string?, CommandResult>? run = null)
    {
        _run = run ?? ((arguments, input) => RunAsync(arguments, input).GetAwaiter().GetResult());
    }

    public string? GetSecret(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        var result = _run(["lookup", "application", Application, "account", key], null);
        if (result.ExitCode == 0)
        {
            return result.Output.TrimEnd('\r', '\n');
        }

        return result.IsMissing ? null : throw Unavailable();
    }

    public void SetSecret(string key, string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentException.ThrowIfNullOrEmpty(secret);
        var result = _run(["store", $"--label={Label}", "application", Application, "account", key], secret);
        if (result.ExitCode != 0)
        {
            throw Unavailable();
        }
    }

    public void DeleteSecret(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        var result = _run(["clear", "application", Application, "account", key], null);
        if (result.ExitCode != 0 && !result.IsMissing)
        {
            throw Unavailable();
        }
    }

    private static async Task<CommandResult> RunAsync(IReadOnlyList<string> arguments, string? input)
    {
        var startInfo = new ProcessStartInfo(SecretTool)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw Unavailable();
            }
        }
        catch (Win32Exception)
        {
            throw Unavailable();
        }

        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            if (input is not null)
            {
                await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token).ConfigureAwait(false);
                await process.StandardInput.FlushAsync(timeout.Token).ConfigureAwait(false);
            }
            process.StandardInput.Close();

            await Task.WhenAll(process.WaitForExitAsync(timeout.Token), output, error).ConfigureAwait(false);
            return new(process.ExitCode, output.Result, error.Result);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Exception stopError) when (stopError is Win32Exception or InvalidOperationException)
            {
            }

            throw Unavailable();
        }
    }

    private static InvalidOperationException Unavailable() =>
        new("Could not access the Linux desktop keyring. Print Sharing requires Secret Service and secret-tool for automatic certificates.");

    internal sealed class CommandResult(int exitCode, string output, string error)
    {
        internal int ExitCode { get; } = exitCode;
        internal string Output { get; } = output;
        internal bool IsMissing =>
            ExitCode == 1 && string.IsNullOrWhiteSpace(Output) && string.IsNullOrWhiteSpace(error);
    }
}
