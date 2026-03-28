using CBWSSSync.Core.Configuration;
using CBWSSSync.Core.Processing;
using CBWSSSync.Core.Services;

namespace CBWSSSync.Infrastructure.Processing;

public sealed class StagingProjectProcessor : IProjectProcessor
{
    public Task<ProcessingResult> ProcessAsync(string sourcePath, WatchProfileSettings profile, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;

        try
        {
            Directory.CreateDirectory(profile.StagingFolder);

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var sourceName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                sourceName = "work-item";
            }

            var outputPath = Path.Combine(profile.StagingFolder, $"{sourceName}-{stamp}");
            Directory.CreateDirectory(outputPath);

            List<string> processedFiles;
            if (Directory.Exists(sourcePath))
            {
                processedFiles = CopyDirectory(sourcePath, outputPath, cancellationToken);
            }
            else if (File.Exists(sourcePath))
            {
                var fileName = Path.GetFileName(sourcePath);
                File.Copy(sourcePath, Path.Combine(outputPath, fileName), overwrite: true);
                processedFiles = [fileName];
            }
            else
            {
                return Task.FromResult(new ProcessingResult
                {
                    Success = false,
                    Message = $"Source path does not exist: {sourcePath}",
                    SourcePath = sourcePath,
                    OutputPath = outputPath,
                    StartedAtUtc = startedAt,
                    FinishedAtUtc = DateTime.UtcNow,
                    Errors = [$"Source path does not exist: {sourcePath}"]
                });
            }

            return Task.FromResult(new ProcessingResult
            {
                Success = true,
                Message = $"Processed {processedFiles.Count} file(s) into {outputPath}",
                SourcePath = sourcePath,
                OutputPath = outputPath,
                StartedAtUtc = startedAt,
                FinishedAtUtc = DateTime.UtcNow,
                ProcessedFiles = processedFiles
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ProcessingResult
            {
                Success = false,
                Message = $"Processing failed: {ex.Message}",
                SourcePath = sourcePath,
                StartedAtUtc = startedAt,
                FinishedAtUtc = DateTime.UtcNow,
                Errors = [ex.ToString()]
            });
        }
    }

    private static List<string> CopyDirectory(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken)
    {
        var files = new List<string>();

        foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativeDirectory = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativeDirectory));
        }

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
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
