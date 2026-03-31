using CNCSync.Core.Configuration;
using CNCSync.Core.Processing;
using CNCSync.Core.Services;
using System.Diagnostics;
using System.Text;

namespace CNCSync.Infrastructure.Processing;

public sealed class StagingProjectProcessor : IProjectProcessor
{
    public async Task<ProcessingResult> ProcessAsync(
        string sourcePath,
        WatchProfileSettings profile,
        ProcessingSetupSettings processingSetup,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        var preparedOutputPath = string.Empty;

        try
        {
            Directory.CreateDirectory(profile.StagingFolder);

            var sourceName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                sourceName = "work-item";
            }

            var sourceIsDirectory = Directory.Exists(sourcePath);
            var sourceIsFile = File.Exists(sourcePath);
            var remoteFolderName = sourceIsDirectory ? sourceName : null;

            preparedOutputPath = Path.Combine(profile.StagingFolder, sourceName);
            DeletePathIfExists(preparedOutputPath);

            if (processingSetup.Mode == ProcessingMode.ExternalScript)
            {
                Directory.CreateDirectory(preparedOutputPath);
                return await RunExternalScriptAsync(sourcePath, preparedOutputPath, processingSetup, startedAt, remoteFolderName, cancellationToken);
            }

            List<string> processedFiles;
            if (sourceIsDirectory)
            {
                Directory.CreateDirectory(preparedOutputPath);
                processedFiles = CopyDirectory(sourcePath, preparedOutputPath, cancellationToken);
            }
            else if (sourceIsFile)
            {
                var fileName = Path.GetFileName(sourcePath);
                if (FileSystemItemFilter.ShouldIgnoreFileSystemItem(fileName))
                {
                    return new ProcessingResult
                    {
                        Success = true,
                        Message = $"Skipped ignored file: {fileName}",
                        SourcePath = sourcePath,
                        OutputPath = preparedOutputPath,
                        StartedAtUtc = startedAt,
                        FinishedAtUtc = DateTime.UtcNow
                    };
                }

                File.Copy(sourcePath, preparedOutputPath, overwrite: true);
                processedFiles = [fileName];
            }
            else
            {
                return new ProcessingResult
                {
                    Success = false,
                    Message = $"Source path does not exist: {sourcePath}",
                    SourcePath = sourcePath,
                    OutputPath = preparedOutputPath,
                    StartedAtUtc = startedAt,
                    FinishedAtUtc = DateTime.UtcNow,
                    Errors = [$"Source path does not exist: {sourcePath}"]
                };
            }

            return new ProcessingResult
            {
                Success = true,
                Message = $"Processed {processedFiles.Count} file(s) into {preparedOutputPath}",
                SourcePath = sourcePath,
                OutputPath = preparedOutputPath,
                RemoteFolderName = remoteFolderName,
                StartedAtUtc = startedAt,
                FinishedAtUtc = DateTime.UtcNow,
                ProcessedFiles = processedFiles
            };
        }
        catch (Exception ex)
        {
            return new ProcessingResult
            {
                Success = false,
                Message = $"Processing failed: {ex.Message}",
                SourcePath = sourcePath,
                OutputPath = preparedOutputPath,
                StartedAtUtc = startedAt,
                FinishedAtUtc = DateTime.UtcNow,
                Errors = [ex.ToString()]
            };
        }
    }

    private static void DeletePathIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
            return;
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static async Task<ProcessingResult> RunExternalScriptAsync(
        string sourcePath,
        string defaultOutputPath,
        ProcessingSetupSettings processingSetup,
        DateTime startedAt,
        string? remoteFolderName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(processingSetup.ScriptPath) || !File.Exists(processingSetup.ScriptPath))
        {
            return new ProcessingResult
            {
                Success = false,
                Message = "External processing failed because the script path is missing or invalid.",
                SourcePath = sourcePath,
                OutputPath = defaultOutputPath,
                RemoteFolderName = remoteFolderName,
                StartedAtUtc = startedAt,
                FinishedAtUtc = DateTime.UtcNow
            };
        }

        var (fileName, arguments) = BuildProcessStart(processingSetup, sourcePath, defaultOutputPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(processingSetup.ScriptPath) ?? Environment.CurrentDirectory
        };

        using var process = new Process { StartInfo = startInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                stdout.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                stderr.AppendLine(args.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        var scriptOutputPath = ParseOutputPath(stdout.ToString()) ?? defaultOutputPath;
        if (process.ExitCode != 0)
        {
            var errorText = stderr.ToString().Trim();
            var message = string.IsNullOrWhiteSpace(errorText)
                ? $"External script failed with exit code {process.ExitCode}."
                : $"External script failed with exit code {process.ExitCode}: {errorText}";

            return new ProcessingResult
            {
                Success = false,
                Message = message,
                SourcePath = sourcePath,
                OutputPath = scriptOutputPath,
                RemoteFolderName = remoteFolderName,
                StartedAtUtc = startedAt,
                FinishedAtUtc = DateTime.UtcNow,
                Errors = string.IsNullOrWhiteSpace(errorText) ? [] : [errorText]
            };
        }

        if (!Directory.Exists(scriptOutputPath))
        {
            return new ProcessingResult
            {
                Success = false,
                Message = $"External script succeeded but output folder was not found: {scriptOutputPath}",
                SourcePath = sourcePath,
                OutputPath = scriptOutputPath,
                RemoteFolderName = remoteFolderName,
                StartedAtUtc = startedAt,
                FinishedAtUtc = DateTime.UtcNow
            };
        }

        var processedFiles = FileSystemItemFilter.EnumerateIncludedFiles(scriptOutputPath)
            .Select(path => Path.GetRelativePath(scriptOutputPath, path))
            .ToList();

        return new ProcessingResult
        {
            Success = true,
            Message = $"External script prepared {processedFiles.Count} file(s) into {scriptOutputPath}",
            SourcePath = sourcePath,
            OutputPath = scriptOutputPath,
            RemoteFolderName = remoteFolderName,
            StartedAtUtc = startedAt,
            FinishedAtUtc = DateTime.UtcNow,
            ProcessedFiles = processedFiles
        };
    }

    private static (string FileName, string Arguments) BuildProcessStart(
        ProcessingSetupSettings processingSetup,
        string sourcePath,
        string outputPath)
    {
        var runnerMode = processingSetup.RunnerMode == ScriptRunnerMode.Auto
            ? DetectRunnerMode(processingSetup.ScriptPath)
            : processingSetup.RunnerMode;

        var resolvedArgs = (string.IsNullOrWhiteSpace(processingSetup.ArgumentsTemplate)
                ? "\"{sourcePath}\" \"{outputPath}\""
                : processingSetup.ArgumentsTemplate)
            .Replace("{sourcePath}", sourcePath)
            .Replace("{outputPath}", outputPath)
            .Replace("{scriptPath}", processingSetup.ScriptPath);

        return runnerMode switch
        {
            ScriptRunnerMode.PowerShell => (ResolvePowerShellExecutable(), $"-NoProfile -File \"{processingSetup.ScriptPath}\" {resolvedArgs}"),
            ScriptRunnerMode.Bash => ("bash", $"\"{processingSetup.ScriptPath}\" {resolvedArgs}"),
            ScriptRunnerMode.Python => ("python3", $"\"{processingSetup.ScriptPath}\" {resolvedArgs}"),
            ScriptRunnerMode.Command => (OperatingSystem.IsWindows() ? "cmd.exe" : "sh", OperatingSystem.IsWindows()
                ? $"/c \"\"{processingSetup.ScriptPath}\" {resolvedArgs}\""
                : $"\"{processingSetup.ScriptPath}\" {resolvedArgs}"),
            ScriptRunnerMode.Direct => (processingSetup.ScriptPath, resolvedArgs),
            _ => (processingSetup.ScriptPath, resolvedArgs)
        };
    }

    private static string ResolvePowerShellExecutable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "pwsh";
        }

        return CommandExistsOnPath("pwsh.exe") || CommandExistsOnPath("pwsh")
            ? "pwsh"
            : "powershell.exe";
    }

    private static bool CommandExistsOnPath(string commandName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return false;
        }

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidatePath = Path.Combine(directory, commandName);
                if (File.Exists(candidatePath))
                {
                    return true;
                }
            }
            catch
            {
                // Ignore malformed PATH entries and keep checking.
            }
        }

        return false;
    }

    private static ScriptRunnerMode DetectRunnerMode(string scriptPath)
    {
        return Path.GetExtension(scriptPath).ToLowerInvariant() switch
        {
            ".ps1" => ScriptRunnerMode.PowerShell,
            ".sh" => ScriptRunnerMode.Bash,
            ".py" => ScriptRunnerMode.Python,
            ".bat" or ".cmd" => ScriptRunnerMode.Command,
            _ => ScriptRunnerMode.Direct
        };
    }

    private static string? ParseOutputPath(string stdout)
    {
        var line = stdout
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(item => item.StartsWith("OUTPUT_PATH=", StringComparison.OrdinalIgnoreCase));
        return line is null ? null : line["OUTPUT_PATH=".Length..].Trim();
    }

    private static List<string> CopyDirectory(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken)
    {
        var files = new List<string>();

        foreach (var directory in FileSystemItemFilter.EnumerateIncludedDirectories(sourceDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativeDirectory = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativeDirectory));
        }

        foreach (var file in FileSystemItemFilter.EnumerateIncludedFiles(sourceDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
            files.Add(relativePath);
        }

        return files;
    }
}
