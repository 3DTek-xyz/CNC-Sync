using CNCSync.Core.Configuration;
using CNCSync.Core.Processing;
using CNCSync.Core.Services;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CNCSync.Infrastructure.Processing;

public sealed class StagingProjectProcessor : IProjectProcessor
{
    private readonly HttpClient _httpClient;

    public StagingProjectProcessor(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
    }

    public async Task<ProcessingResult> ProcessAsync(
        string sourcePath,
        WatchProfileSettings profile,
        ProcessingSetupSettings processingSetup,
        ProCutApiSettings? proCutApi = null,
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

            if (processingSetup.Mode == ProcessingMode.ProCutApi)
            {
                return await RunProCutApiAsync(
                    sourcePath,
                    preparedOutputPath,
                    profile,
                    processingSetup,
                    proCutApi,
                    startedAt,
                    remoteFolderName,
                    sourceIsDirectory,
                    sourceIsFile,
                    cancellationToken);
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

    private async Task<ProcessingResult> RunProCutApiAsync(
        string sourcePath,
        string defaultOutputPath,
        WatchProfileSettings profile,
        ProcessingSetupSettings processingSetup,
        ProCutApiSettings? proCutApi,
        DateTime startedAt,
        string? remoteFolderName,
        bool sourceIsDirectory,
        bool sourceIsFile,
        CancellationToken cancellationToken)
    {
        if (proCutApi is null || string.IsNullOrWhiteSpace(proCutApi.ApiKey))
        {
            return new ProcessingResult
            {
                Success = false,
                Message = "ProCut Suite API processing failed because no API key is saved.",
                SourcePath = sourcePath,
                OutputPath = defaultOutputPath,
                RemoteFolderName = remoteFolderName,
                StartedAtUtc = startedAt,
                FinishedAtUtc = DateTime.UtcNow,
                Errors = ["No ProCut Suite API key is saved."]
            };
        }

        if (!TryBuildProCutApiUri(proCutApi, processingSetup, out var endpointUri, out var uriError))
        {
            return new ProcessingResult
            {
                Success = false,
                Message = $"ProCut Suite API processing failed: {uriError}",
                SourcePath = sourcePath,
                OutputPath = defaultOutputPath,
                RemoteFolderName = remoteFolderName,
                StartedAtUtc = startedAt,
                FinishedAtUtc = DateTime.UtcNow,
                Errors = [uriError]
            };
        }

        var activityMessages = new List<string>
        {
            $"ProCut Suite API endpoint: {endpointUri}",
            $"ProCut Suite API tools: {string.Join(", ", BuildProCutToolTypeList(processingSetup))}"
        };

        if (sourceIsDirectory)
        {
            Directory.CreateDirectory(defaultOutputPath);
            var processedFiles = new List<string>();
            foreach (var sourceFile in FileSystemItemFilter.EnumerateIncludedFiles(sourcePath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(sourcePath, sourceFile);
                var relativeDirectory = Path.GetDirectoryName(relativePath);
                var outputDirectory = string.IsNullOrWhiteSpace(relativeDirectory)
                    ? defaultOutputPath
                    : Path.Combine(defaultOutputPath, relativeDirectory);
                Directory.CreateDirectory(outputDirectory);

                string processedFileName;
                try
                {
                    processedFileName = await ProcessFileWithProCutApiAsync(sourceFile, outputDirectory, endpointUri!, proCutApi.ApiKey, processingSetup, activityMessages, cancellationToken);
                }
                catch (Exception ex)
                {
                    activityMessages.Add($"ProCut Suite API processing failed for {Path.GetFileName(sourceFile)}: {ex.Message}");
                    return new ProcessingResult
                    {
                        Success = false,
                        Message = $"ProCut Suite API processing failed: {ex.Message}",
                        SourcePath = sourcePath,
                        OutputPath = defaultOutputPath,
                        RemoteFolderName = remoteFolderName,
                        StartedAtUtc = startedAt,
                        FinishedAtUtc = DateTime.UtcNow,
                        ActivityMessages = activityMessages,
                        Errors = [ex.ToString()]
                    };
                }

                var processedRelativePath = string.IsNullOrWhiteSpace(relativeDirectory)
                    ? processedFileName
                    : Path.Combine(relativeDirectory, processedFileName);
                processedFiles.Add(processedRelativePath);
            }

            return new ProcessingResult
            {
                Success = true,
                Message = $"ProCut Suite API processed {processedFiles.Count} file(s) into {defaultOutputPath}",
                SourcePath = sourcePath,
                OutputPath = defaultOutputPath,
                RemoteFolderName = remoteFolderName,
                StartedAtUtc = startedAt,
                FinishedAtUtc = DateTime.UtcNow,
                ActivityMessages = activityMessages,
                ProcessedFiles = processedFiles
            };
        }

        if (!sourceIsFile)
        {
            return new ProcessingResult
            {
                Success = false,
                Message = $"Source path does not exist: {sourcePath}",
                SourcePath = sourcePath,
                OutputPath = defaultOutputPath,
                RemoteFolderName = remoteFolderName,
                StartedAtUtc = startedAt,
                FinishedAtUtc = DateTime.UtcNow,
                Errors = [$"Source path does not exist: {sourcePath}"]
            };
        }

        var fileName = Path.GetFileName(sourcePath);
        if (FileSystemItemFilter.ShouldIgnoreFileSystemItem(fileName))
        {
            return new ProcessingResult
            {
                Success = true,
                Message = $"Skipped ignored file: {fileName}",
                SourcePath = sourcePath,
                OutputPath = defaultOutputPath,
                RemoteFolderName = remoteFolderName,
                StartedAtUtc = startedAt,
                FinishedAtUtc = DateTime.UtcNow
            };
        }

        var outputFolder = Path.GetDirectoryName(defaultOutputPath) ?? profile.StagingFolder;
        Directory.CreateDirectory(outputFolder);
        string outputFileName;
        try
        {
            outputFileName = await ProcessFileWithProCutApiAsync(sourcePath, outputFolder, endpointUri!, proCutApi.ApiKey, processingSetup, activityMessages, cancellationToken);
        }
        catch (Exception ex)
        {
            activityMessages.Add($"ProCut Suite API processing failed for {fileName}: {ex.Message}");
            return new ProcessingResult
            {
                Success = false,
                Message = $"ProCut Suite API processing failed: {ex.Message}",
                SourcePath = sourcePath,
                OutputPath = defaultOutputPath,
                RemoteFolderName = remoteFolderName,
                StartedAtUtc = startedAt,
                FinishedAtUtc = DateTime.UtcNow,
                ActivityMessages = activityMessages,
                Errors = [ex.ToString()]
            };
        }

        var outputPath = Path.Combine(outputFolder, outputFileName);

        return new ProcessingResult
        {
            Success = true,
            Message = $"ProCut Suite API processed 1 file into {outputPath}",
            SourcePath = sourcePath,
            OutputPath = outputPath,
            RemoteFolderName = remoteFolderName,
            StartedAtUtc = startedAt,
            FinishedAtUtc = DateTime.UtcNow,
            ActivityMessages = activityMessages,
            ProcessedFiles = [outputFileName]
        };
    }

    private async Task<string> ProcessFileWithProCutApiAsync(
        string sourceFile,
        string outputDirectory,
        Uri endpointUri,
        string apiKey,
        ProcessingSetupSettings processingSetup,
        List<string> activityMessages,
        CancellationToken cancellationToken)
    {
        var sourceFileName = Path.GetFileName(sourceFile);
        var toolsJson = BuildProCutToolsJson(processingSetup);
        activityMessages.Add($"ProCut Suite API upload starting: {sourceFileName}");

        await using var fileStream = File.OpenRead(sourceFile);
        using var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        using var content = new MultipartFormDataContent();
        content.Add(fileContent, "file", sourceFileName);
        content.Add(new StringContent(toolsJson, Encoding.UTF8, "application/json"), "tools");

        using var request = new HttpRequestMessage(HttpMethod.Post, endpointUri)
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var detail = string.IsNullOrWhiteSpace(errorBody)
                ? response.ReasonPhrase
                : errorBody.Trim();
            activityMessages.Add($"ProCut Suite API error {(int)response.StatusCode} {response.StatusCode}: {detail}");
            throw new InvalidOperationException($"ProCut Suite API returned {(int)response.StatusCode} {response.StatusCode}: {detail}");
        }

        var outputFileName = ResolveProCutOutputFileName(response.Content.Headers.ContentDisposition, sourceFileName);
        var outputPath = Path.Combine(outputDirectory, outputFileName);
        await using var outputStream = File.Create(outputPath);
        await response.Content.CopyToAsync(outputStream, cancellationToken);
        activityMessages.Add($"ProCut Suite API response received: {sourceFileName} -> {outputFileName}");
        return outputFileName;
    }

    private static IReadOnlyList<string> BuildProCutToolTypeList(ProcessingSetupSettings processingSetup)
    {
        var tools = new List<string>();

        if (processingSetup.ProCutArcFittingEnabled &&
            ProCutGcodeToolAvailability.IsAvailable("arc_fitting", schemaEnabled: null))
        {
            tools.Add("arc_fitting");
        }

        if (processingSetup.ProCutLineJoinerEnabled)
        {
            tools.Add("line_joiner");
        }

        if (processingSetup.ProCutArcJoinerEnabled &&
            ProCutGcodeToolAvailability.IsAvailable("arc_joiner", schemaEnabled: null))
        {
            tools.Add("arc_joiner");
        }

        if (processingSetup.ProCutCornerSmoothEnabled)
        {
            tools.Add("corner_smooth");
        }

        return tools;
    }

    private static string BuildProCutToolsJson(ProcessingSetupSettings processingSetup)
    {
        var tools = new List<object>();

        if (processingSetup.ProCutArcFittingEnabled &&
            ProCutGcodeToolAvailability.IsAvailable("arc_fitting", schemaEnabled: null))
        {
            tools.Add(new
            {
                type = "arc_fitting",
                options = new
                {
                    toleranceMm = processingSetup.ProCutArcFittingToleranceMm,
                    minSegments = processingSetup.ProCutArcFittingMinSegments,
                    maxSegments = processingSetup.ProCutArcFittingMaxSegments
                }
            });
        }

        if (processingSetup.ProCutLineJoinerEnabled)
        {
            tools.Add(new
            {
                type = "line_joiner",
                options = new
                {
                    preserveFeedBoundaries = processingSetup.ProCutLineJoinerPreserveFeedBoundaries
                }
            });
        }

        if (processingSetup.ProCutArcJoinerEnabled &&
            ProCutGcodeToolAvailability.IsAvailable("arc_joiner", schemaEnabled: null))
        {
            tools.Add(new
            {
                type = "arc_joiner",
                options = new
                {
                    maxCombinedAngleDeg = processingSetup.ProCutArcJoinerMaxCombinedAngleDeg
                }
            });
        }

        if (processingSetup.ProCutCornerSmoothEnabled)
        {
            tools.Add(new
            {
                type = "corner_smooth",
                options = new
                {
                    angleThresholdDeg = processingSetup.ProCutCornerSmoothAngleThresholdDeg,
                    slowdownDistanceMm = processingSetup.ProCutCornerSmoothSlowdownDistanceMm,
                    slowdownFeedrateMmMin = processingSetup.ProCutCornerSmoothSlowdownFeedrateMmMin,
                    smallArcThresholdMm = processingSetup.ProCutCornerSmoothSmallArcThresholdMm,
                    includeCircularRamps = processingSetup.ProCutCornerSmoothIncludeCircularRamps
                }
            });
        }

        if (tools.Count == 0)
        {
            throw new InvalidOperationException("At least one ProCut Suite G-code processing tool must be enabled.");
        }

        return JsonSerializer.Serialize(tools);
    }

    private static bool TryBuildProCutApiUri(
        ProCutApiSettings proCutApi,
        ProcessingSetupSettings processingSetup,
        out Uri? endpointUri,
        out string error)
    {
        endpointUri = null;
        error = string.Empty;

        var endpoint = string.IsNullOrWhiteSpace(processingSetup.ProCutApiEndpoint)
            ? "/api/external/gcode/process"
            : processingSetup.ProCutApiEndpoint.Trim();
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out endpointUri) &&
            (string.Equals(endpointUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(endpointUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!Uri.TryCreate(proCutApi.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            error = "the ProCut Suite API base URL is invalid.";
            return false;
        }

        endpointUri = new Uri(baseUri, endpoint.TrimStart('/'));
        return true;
    }

    private static string ResolveProCutOutputFileName(ContentDispositionHeaderValue? contentDisposition, string fallbackFileName)
    {
        var fileName = contentDisposition?.FileNameStar;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = contentDisposition?.FileName;
        }

        fileName = fileName?.Trim().Trim('"');
        return string.IsNullOrWhiteSpace(fileName)
            ? fallbackFileName
            : Path.GetFileName(fileName);
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
