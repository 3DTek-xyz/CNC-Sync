using CNCSync.Core.Configuration;
using CNCSync.Core.Processing;
using CNCSync.Core.Services;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CNCSync.Infrastructure.Processing;

public sealed class StagingProjectProcessor : IProjectProcessor
{
    private readonly HttpClient _httpClient;

    public StagingProjectProcessor(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
    }

    public Task<ProcessingResult> ProcessAsync(
        string sourcePath,
        WatchProfileSettings profile,
        ProcessingSetupSettings processingSetup,
        ProCutApiSettings? proCutApi = null,
        CancellationToken cancellationToken = default) =>
        ProcessCoreAsync(sourcePath, profile, processingSetup, proCutApi, null, cancellationToken);

    public Task<ProcessingResult> ProcessQueuedAsync(
        string sourcePath,
        WatchProfileSettings profile,
        ProcessingSetupSettings processingSetup,
        ProCutApiSettings? proCutApi,
        string sourceFingerprint,
        CancellationToken cancellationToken) =>
        ProcessCoreAsync(sourcePath, profile, processingSetup, proCutApi, sourceFingerprint, cancellationToken);

    private async Task<ProcessingResult> ProcessCoreAsync(
        string sourcePath,
        WatchProfileSettings profile,
        ProcessingSetupSettings processingSetup,
        ProCutApiSettings? proCutApi,
        string? sourceFingerprint,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        string? jobDirectory = null;
        var published = false;
        var activityMessages = new List<string>();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            sourcePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePath));
            var sourceName = Path.GetFileName(sourcePath);
            var sourceIsDirectory = Directory.Exists(sourcePath);
            if (!sourceIsDirectory && !File.Exists(sourcePath))
            {
                throw new InvalidOperationException($"Source path does not exist: {sourcePath}");
            }

            if (!sourceIsDirectory && FileSystemItemFilter.ShouldIgnoreFileSystemItem(sourceName))
            {
                return new ProcessingResult
                {
                    Success = true,
                    Message = $"Skipped ignored file: {sourceName}",
                    SourcePath = sourcePath,
                    StartedAtUtc = startedAt,
                    FinishedAtUtc = DateTime.UtcNow
                };
            }

            var stagingRoot = Path.GetFullPath(profile.StagingFolder);
            if (sourceIsDirectory && IsSameOrWithinDirectory(stagingRoot, sourcePath))
            {
                throw new InvalidOperationException("The staging folder cannot be inside the source folder.");
            }

            sourceFingerprint ??= WorkItemFingerprint.Capture(sourcePath, includeContent: true, cancellationToken);
            jobDirectory = CompletedStagedOutput.CreateJobDirectory(stagingRoot);
            var payloadDirectory = Path.Combine(jobDirectory, "payload");
            Directory.CreateDirectory(payloadDirectory);
            var outputPath = Path.Combine(payloadDirectory, string.IsNullOrWhiteSpace(sourceName) ? "work-item" : sourceName);
            var remoteFolderName = ResolveRemoteFolderName(sourcePath, sourceName, sourceIsDirectory, profile);

            switch (processingSetup.Mode)
            {
                case ProcessingMode.ExternalScript:
                    Directory.CreateDirectory(outputPath);
                    var scriptPath = await RunExternalScriptAsync(sourcePath, outputPath, processingSetup, cancellationToken);
                    if (!IsSamePath(scriptPath, outputPath))
                    {
                        var snapshotPath = Path.Combine(jobDirectory, "script-output");
                        if (IsSameOrWithinDirectory(snapshotPath, scriptPath))
                        {
                            throw new InvalidOperationException("The script output folder contains its staging destination.");
                        }

                        Directory.CreateDirectory(snapshotPath);
                        CopyDirectory(scriptPath, snapshotPath, cancellationToken);
                        outputPath = snapshotPath;
                    }
                    break;
                case ProcessingMode.ProCutApi:
                    outputPath = await RunProCutApiAsync(sourcePath, outputPath, processingSetup, proCutApi,
                        sourceIsDirectory, activityMessages, cancellationToken);
                    break;
                default:
                    if (sourceIsDirectory)
                    {
                        Directory.CreateDirectory(outputPath);
                        CopyDirectory(sourcePath, outputPath, cancellationToken);
                    }
                    else
                    {
                        File.Copy(sourcePath, outputPath);
                    }
                    break;
            }

            if (!string.Equals(sourceFingerprint, WorkItemFingerprint.Capture(sourcePath, includeContent: true, cancellationToken), StringComparison.Ordinal))
            {
                throw new IOException("Source changed during processing; no staged output was published.");
            }

            var completed = CompletedStagedOutput.Publish(jobDirectory, profile.Id, sourcePath, outputPath,
                remoteFolderName, sourceFingerprint, cancellationToken,
                remoteRelativeParent: !sourceIsDirectory && processingSetup.Mode == ProcessingMode.ProCutApi
                    ? ResolveRemoteRelativeParent(sourcePath, profile.WatchFolder) : null);
            published = true;
            return new ProcessingResult
            {
                Success = true,
                Message = $"Processed {completed.ProcessedFiles.Count} file(s) into {outputPath}",
                SourcePath = sourcePath,
                OutputPath = outputPath,
                RemoteFolderName = remoteFolderName,
                StartedAtUtc = startedAt,
                FinishedAtUtc = completed.CompletedAtUtc,
                ProcessedFiles = completed.ProcessedFiles,
                ActivityMessages = activityMessages,
                CompletedOutput = completed
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var apiFailure = ex as ProCutOutputException;
            return new ProcessingResult
            {
                Success = false,
                Message = $"Processing failed: {ex.Message}",
                SourcePath = sourcePath,
                StartedAtUtc = startedAt,
                FinishedAtUtc = DateTime.UtcNow,
                ActivityMessages = activityMessages,
                Errors = [ex.Message],
                IsRetryable = apiFailure?.IsRetryable ?? (ex is HttpRequestException or IOException or OperationCanceledException),
                RetryAfter = apiFailure?.RetryAfter
            };
        }
        finally
        {
            if (!published && jobDirectory is not null)
            {
                TryDiscardJobDirectory(jobDirectory);
            }
        }
    }

    private async Task<string> RunProCutApiAsync(
        string sourcePath,
        string defaultOutputPath,
        ProcessingSetupSettings processingSetup,
        ProCutApiSettings? proCutApi,
        bool sourceIsDirectory,
        List<string> activityMessages,
        CancellationToken cancellationToken)
    {
        if (proCutApi is null || string.IsNullOrWhiteSpace(proCutApi.ApiKey))
        {
            throw new InvalidOperationException("ProCut Suite API processing requires a saved API key.");
        }

        if (!TryBuildProCutApiUri(proCutApi, processingSetup, out var endpointUri, out var uriError))
        {
            throw new InvalidOperationException($"ProCut Suite API processing failed: {uriError}");
        }

        activityMessages.Add($"ProCut Suite API endpoint: {endpointUri!.GetLeftPart(UriPartial.Path)}");
        activityMessages.Add($"ProCut Suite API tools: {string.Join(", ", BuildProCutToolTypeList(processingSetup))}");

        if (!sourceIsDirectory)
        {
            var outputDirectory = Path.GetDirectoryName(defaultOutputPath)!;
            var outputName = await ProcessFileWithProCutApiAsync(sourcePath, outputDirectory, endpointUri,
                proCutApi.ApiKey, processingSetup, activityMessages, cancellationToken);
            return Path.Combine(outputDirectory, outputName);
        }

        Directory.CreateDirectory(defaultOutputPath);
        var sourceFiles = FileSystemItemFilter.EnumerateIncludedFiles(sourcePath).ToArray();
        foreach (var sourceFile in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativeDirectory = Path.GetDirectoryName(Path.GetRelativePath(sourcePath, sourceFile))!;
            var outputDirectory = Path.Combine(defaultOutputPath, relativeDirectory);
            Directory.CreateDirectory(outputDirectory);
            await ProcessFileWithProCutApiAsync(sourceFile, outputDirectory, endpointUri,
                proCutApi.ApiKey, processingSetup, activityMessages, cancellationToken);
        }

        return defaultOutputPath;
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

        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(_httpClient.Timeout);
        var requestToken = requestTimeout.Token;
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken);
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            var retryable = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || status >= 500;
            var retryHeader = response.Headers.RetryAfter;
            var retryAfter = retryHeader?.Delta ?? (retryHeader?.Date - DateTimeOffset.UtcNow);
            throw new ProCutOutputException($"ProCut Suite API returned HTTP {status} ({response.StatusCode}).", retryable,
                retryable && retryAfter > TimeSpan.Zero ? retryAfter : null);
        }

        var outputFileName = ResolveProCutOutputFileName(response.Content.Headers.ContentDisposition, sourceFileName);
        var outputPath = Path.Combine(outputDirectory, outputFileName);
        if (File.Exists(outputPath) || Directory.Exists(outputPath))
        {
            throw new ProCutOutputException($"Multiple ProCut Suite API outputs use the filename {outputFileName}.");
        }

        await ProCutResponseOutput.SaveAsync(response, outputPath, requestToken);
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
        var endpoint = string.IsNullOrWhiteSpace(processingSetup.ProCutApiEndpoint)
            ? "/api/external/gcode/process"
            : processingSetup.ProCutApiEndpoint.Trim();
        var resolved = ProCutApiUri.TryResolveEndpoint(proCutApi.BaseUrl, endpoint, out endpointUri);
        error = resolved ? string.Empty : "the API endpoint must use the configured HTTPS origin.";
        return resolved;
    }

    private static string ResolveProCutOutputFileName(ContentDispositionHeaderValue? contentDisposition, string fallbackFileName)
    {
        var fileName = contentDisposition?.FileNameStar;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = contentDisposition?.FileName;
        }

        fileName = string.IsNullOrWhiteSpace(fileName) ? fallbackFileName : fileName.Trim().Trim('"');
        if (FileSystemItemFilter.ShouldIgnoreFileSystemItem(fileName) || fileName.EndsWith('.') || fileName.EndsWith(' ') ||
            fileName.IndexOfAny(['/', '\\', ':', '<', '>', '"', '|', '?', '*']) >= 0 || fileName.Any(char.IsControl) ||
            IsWindowsDeviceName(fileName))
        {
            throw new ProCutOutputException("The ProCut Suite API returned an invalid output filename.");
        }

        return fileName;
    }

    private static bool IsWindowsDeviceName(string fileName)
    {
        var dot = fileName.IndexOf('.');
        var stem = (dot >= 0 ? fileName[..dot] : fileName).TrimEnd().ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                (stem[3] is >= '1' and <= '9' or '\u00B9' or '\u00B2' or '\u00B3'));
    }

    private static string? ResolveRemoteFolderName(string sourcePath, string sourceName, bool isDirectory, WatchProfileSettings profile) =>
        isDirectory && !string.IsNullOrWhiteSpace(sourceName) &&
        (string.IsNullOrWhiteSpace(profile.WatchFolder) || !IsSamePath(sourcePath, profile.WatchFolder))
            ? sourceName
            : null;

    private static string? ResolveRemoteRelativeParent(string sourcePath, string watchFolder)
    {
        if (string.IsNullOrWhiteSpace(watchFolder) || !IsSameOrWithinDirectory(sourcePath, watchFolder))
        {
            return null;
        }

        var parent = Path.GetDirectoryName(Path.GetRelativePath(watchFolder, sourcePath));
        return string.IsNullOrEmpty(parent) ? null : parent.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static bool IsSamePath(string first, string second) =>
        Path.GetRelativePath(Path.GetFullPath(first), Path.GetFullPath(second)) == ".";

    private static bool IsSameOrWithinDirectory(string path, string directory)
    {
        var relative = Path.GetRelativePath(directory, path);
        return !Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static void TryDiscardJobDirectory(string jobDirectory)
    {
        try
        {
            Directory.Delete(jobDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Could not remove unpublished staged job {jobDirectory}: {ex.Message}");
        }
    }

    private static async Task<string> RunExternalScriptAsync(
        string sourcePath,
        string defaultOutputPath,
        ProcessingSetupSettings processingSetup,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(processingSetup.ScriptPath) || !File.Exists(processingSetup.ScriptPath))
        {
            throw new InvalidOperationException("External processing failed because the script path is missing or invalid.");
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
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

        var scriptOutputPath = ParseOutputPath(stdout.ToString()) ?? defaultOutputPath;
        if (process.ExitCode != 0)
        {
            var errorText = stderr.ToString().Trim();
            var message = string.IsNullOrWhiteSpace(errorText)
                ? $"External script failed with exit code {process.ExitCode}."
                : $"External script failed with exit code {process.ExitCode}: {errorText}";

            throw new InvalidOperationException(message);
        }

        if (!Directory.Exists(scriptOutputPath))
        {
            throw new InvalidOperationException($"External script succeeded but output folder was not found: {scriptOutputPath}");
        }

        return scriptOutputPath;
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

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken)
    {
        var pending = new Stack<(string Source, string Destination)>();
        pending.Push((sourceDirectory, destinationDirectory));
        while (pending.TryPop(out var item))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((File.GetAttributes(item.Source) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"Staging cannot copy a symbolic link: {item.Source}");
            }

            if (!Directory.Exists(item.Source))
            {
                File.Copy(item.Source, item.Destination);
                continue;
            }

            Directory.CreateDirectory(item.Destination);
            foreach (var entry in Directory.EnumerateFileSystemEntries(item.Source))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(entry);
                if (!FileSystemItemFilter.ShouldIgnoreFileSystemItem(name))
                {
                    pending.Push((entry, Path.Combine(item.Destination, name)));
                }
            }
        }
    }
}
