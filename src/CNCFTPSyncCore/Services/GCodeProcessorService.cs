using CNCFTPSyncCore.Models;
using CNCFTPSyncCore.Services;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Xml;

namespace CNCFTPSyncCore.Services
{
    public enum InternalProcessingType
    {
        MozaikSyntecLabelCNC,
        MozaikSyntecLabelCNCWithCYC,
        SimpleFtpUpload
    }
}

namespace CNCFTPSyncCore.Services
{
    public interface IGCodeProcessor
    {
        Task<ProcessingResult> ProcessProjectFolderAsync(string projectPath);
        ProjectInfo AnalyzeProject(string projectPath);
        void SetFolderWatcher(IFolderWatcher folderWatcher);
    }

    public class GCodeProcessorService : IGCodeProcessor
    {
        private readonly ILogService _logger;
        private readonly SyncConfiguration _config;
        private IFolderWatcher? _folderWatcher;

        public GCodeProcessorService(ILogService logger, SyncConfiguration config)
        {
            _logger = logger;
            _config = config;
        }

        public void SetFolderWatcher(IFolderWatcher folderWatcher)
        {
            _folderWatcher = folderWatcher;
        }

        private async Task<ProcessingResult> ProcessWithExternalScriptAsync(string projectPath, ProcessingResult result)
        {
            try
            {
                if (string.IsNullOrEmpty(_config.ExternalProcessorPath) || !File.Exists(_config.ExternalProcessorPath))
                {
                    result.Message = "External processor path not configured or file does not exist";
                    _logger.LogError(result.Message);
                    return result;
                }

                // Get the FTP upload directory (use local directory if not configured)
                var ftpUploadDirectory = string.IsNullOrEmpty(_config.FtpUploadFolder) 
                    ? Path.Combine(Path.GetTempPath(), "CNC-FTP-Upload")
                    : _config.FtpUploadFolder;

                // Ensure upload directory exists
                Directory.CreateDirectory(ftpUploadDirectory);

                // Get the current log file path (same pattern as used throughout the system)
                var sharedDataDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "CNC-FTP-SYNC"
                );
                var logDirectory = Path.Combine(sharedDataDirectory, "Logs");
                var currentLogFile = Path.Combine(logDirectory, $"cnc-ftp-sync-{DateTime.Now:yyyy-MM-dd}.log");

                _logger.LogInfo($"Calling external processor: {_config.ExternalProcessorPath}");
                _logger.LogInfo($"Arguments: \"{projectPath}\" \"{ftpUploadDirectory}\" \"{currentLogFile}\"");

                // Determine how to execute the script based on file extension
                var scriptExtension = Path.GetExtension(_config.ExternalProcessorPath).ToLowerInvariant();
                
                System.Diagnostics.ProcessStartInfo processInfo;
                
                switch (scriptExtension)
                {
                    case ".ps1":
                        // PowerShell script with enhanced output capture
                        processInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "powershell.exe",
                            Arguments = $"-ExecutionPolicy Bypass -NoProfile -NonInteractive -File \"{_config.ExternalProcessorPath}\" \"{projectPath}\" \"{ftpUploadDirectory}\" \"{currentLogFile}\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true,
                            StandardOutputEncoding = Encoding.UTF8,
                            StandardErrorEncoding = Encoding.UTF8
                        };
                        _logger.LogInfo($"Executing PowerShell script via: powershell.exe -ExecutionPolicy Bypass -NoProfile -NonInteractive -File \"{_config.ExternalProcessorPath}\"");
                        break;
                        
                    case ".bat":
                        // Batch file with enhanced output capture
                        processInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/c \"\"{_config.ExternalProcessorPath}\" \"{projectPath}\" \"{ftpUploadDirectory}\" \"{currentLogFile}\"\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true,
                            StandardOutputEncoding = Encoding.UTF8,
                            StandardErrorEncoding = Encoding.UTF8
                        };
                        _logger.LogInfo($"Executing batch file via: cmd.exe /c \"{_config.ExternalProcessorPath}\"");
                        break;
                        
                    default:
                        // Unsupported file type
                        result.Success = false;
                        result.Message = $"Unsupported script type: {scriptExtension}. Only .ps1 and .bat files are supported.";
                        _logger.LogError(result.Message);
                        return result;
                }

                // Execute external script with improved output capture
                using var process = new System.Diagnostics.Process { StartInfo = processInfo };
                
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();
                
                // Set up asynchronous output reading to prevent deadlocks
                process.OutputDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        outputBuilder.AppendLine(e.Data);
                        _logger.LogInfo($"[Script Output] {e.Data}");
                    }
                };
                
                process.ErrorDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        errorBuilder.AppendLine(e.Data);
                        _logger.LogError($"[Script Error] {e.Data}");
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                
                await process.WaitForExitAsync();
                
                var output = outputBuilder.ToString().Trim();
                var error = errorBuilder.ToString().Trim();

                if (process.ExitCode == 0)
                {
                    result.Success = true;
                    result.Message = $"External processor completed successfully";
                    
                    // Parse the output path from stdout - look for "Path=" prefix
                    var outputLines = output?.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    if (outputLines?.Length > 0)
                    {
                        foreach (var line in outputLines)
                        {
                            var trimmedLine = line.Trim();
                            if (trimmedLine.StartsWith("Path="))
                            {
                                var preparedPath = trimmedLine.Substring(5); // Remove "Path=" prefix
                                if (Directory.Exists(preparedPath))
                                {
                                    result.OutputPath = preparedPath;
                                    result.ProcessedFiles.Add($"External script prepared files at: {preparedPath}");
                                    _logger.LogInfo($"External processor prepared files at: {preparedPath}");
                                }
                                else
                                {
                                    _logger.LogWarning($"External processor output path does not exist: {preparedPath}");
                                }
                                break; // Found the path, no need to check other lines
                            }
                        }
                    }
                    
                    result.ProcessedFiles.Add($"External script processed: {projectPath}");
                    
                    if (!string.IsNullOrEmpty(output))
                    {
                        _logger.LogInfo($"External processor output: {output.Trim()}");
                    }
                    
                    if (!string.IsNullOrEmpty(error))
                    {
                        _logger.LogWarning($"External processor stderr: {error.Trim()}");
                    }
                }
                else
                {
                    result.Success = false;
                    result.Message = $"External processor failed with exit code: {process.ExitCode}";
                    result.Errors.Add($"Exit code: {process.ExitCode}");
                    
                    // Log both stdout and stderr for failed processes
                    if (!string.IsNullOrEmpty(output))
                    {
                        _logger.LogInfo($"External processor output (failed run): {output.Trim()}");
                    }
                    
                    if (!string.IsNullOrEmpty(error))
                    {
                        result.Errors.Add($"Error: {error.Trim()}");
                        _logger.LogError($"External processor error: {error.Trim()}");
                    }
                }

                result.EndTime = DateTime.Now;
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Error executing external processor: {ex.Message}";
                result.Errors.Add(ex.Message);
                _logger.LogError("External processor execution failed", ex);
                result.EndTime = DateTime.Now;
                return result;
            }
        }



        public async Task<ProcessingResult> ProcessProjectFolderAsync(string projectPath)
        {
            var result = new ProcessingResult
            {
                StartTime = DateTime.Now,
                Success = false
            };

            try
            {
                _logger.LogInfo($"Starting processing of project folder: {projectPath}");

                // Check if external processor should be used
                if (_config.UseExternalProcessor)
                {
                    return await ProcessWithExternalScriptAsync(projectPath, result);
                }

                // Determine internal processing type
                var processingType = GetInternalProcessingType(_config.InternalProcessingType);
                _logger.LogInfo($"Using internal processing type: {processingType}");

                // Route to appropriate internal processing method
                switch (processingType)
                {
                    case InternalProcessingType.MozaikSyntecLabelCNC:
                    case InternalProcessingType.MozaikSyntecLabelCNCWithCYC:
                        return await ProcessMozaikSyntecLabelCNCAsync(projectPath, result);
                    case InternalProcessingType.SimpleFtpUpload:
                        return await ProcessSimpleFtpUploadAsync(projectPath, result);
                    default:
                        result.Message = $"Unknown internal processing type: {processingType}";
                        _logger.LogError(result.Message);
                        return result;
                }
            }
            catch (Exception ex)
            {
                result.Message = $"Error processing project: {ex.Message}";
                result.Errors.Add(ex.ToString());
                _logger.LogError(result.Message, ex);
            }
            finally
            {
                result.EndTime = DateTime.Now;
            }

            return result;
        }

        private InternalProcessingType GetInternalProcessingType(string processingTypeName)
        {
            return processingTypeName switch
            {
                "Mozaik=>SyntecLabel+CNC" => InternalProcessingType.MozaikSyntecLabelCNC,
                "Mozaik=>SyntecLabel+CNC (CYC Coordinate Update)" => InternalProcessingType.MozaikSyntecLabelCNCWithCYC,
                "Simple FTP Upload" => InternalProcessingType.SimpleFtpUpload,
                _ => InternalProcessingType.MozaikSyntecLabelCNC // Default to first option
            };
        }

        private async Task<ProcessingResult> ProcessMozaikSyntecLabelCNCAsync(string projectPath, ProcessingResult result)
        {
            try
            {
                // Step 1: Analyze the project (using original path)
                var projectInfo = AnalyzeProject(projectPath);
                if (string.IsNullOrEmpty(projectInfo.LatestRevision))
                {
                    result.Message = "No CYC files found in project folder";
                    _logger.LogWarning(result.Message);
                    return result;
                }

                _logger.LogInfo($"Project: {projectInfo.ProjectName}, Latest Revision: {projectInfo.LatestRevision}");

                // Step 2: Copy entire project to FTP working area
                await CopyProjectToFtpWorkingAreaAsync(projectInfo);

                // Step 3: Re-analyze project in FTP working area to get correct file paths
                projectInfo = AnalyzeProject(projectInfo.FtpWorkingPath);
                projectInfo.ProjectName = Path.GetFileName(projectPath); // Preserve original project name

                // Step 4: Create and clean subdirectories (in FTP working area)
                await CreateAndCleanSubdirectoriesAsync(projectInfo);

                // Step 5: Move NC files (in FTP working area)
                await MoveNcFilesAsync(projectInfo);

                // Step 6: Move CYC files and process coordinates (in FTP working area)
                await MoveCycFilesAsync(projectInfo);

                // Step 7: Move JPG files (in FTP working area)
                await MoveJpgFilesAsync(projectInfo);

                // Step 8: Move XML files (in FTP working area)
                await MoveXmlFilesAsync(projectInfo);

                // Step 9: Process CYC coordinates and convert to UTF-8 (in FTP working area)
                // Only execute this step if using the CYC Coordinate Update option
                if (_config.InternalProcessingType == "Mozaik=>SyntecLabel+CNC (CYC Coordinate Update)")
                {
                    await ProcessCycCoordinatesAsync(projectInfo);
                }

                // Files are now ready in FTP working area - no additional copying needed

                result.Success = true;
                result.Message = $"Successfully processed project: {projectInfo.ProjectName}-{projectInfo.LatestRevision} (originals preserved)";
                _logger.LogInfo(result.Message);
            }
            catch (Exception ex)
            {
                result.Message = $"Error processing Mozaik=>SyntecLabel+CNC: {ex.Message}";
                result.Errors.Add(ex.ToString());
                _logger.LogError(result.Message, ex);
            }

            return result;
        }

        private async Task<ProcessingResult> ProcessSimpleFtpUploadAsync(string projectPath, ProcessingResult result)
        {
            try
            {
                _logger.LogInfo($"Processing Simple FTP Upload for: {projectPath}");

                // For Simple FTP Upload, just copy all files/folders to FTP Upload directory
                var sourceInfo = new DirectoryInfo(projectPath);
                var projectName = sourceInfo.Name;
                
                // Create destination path in FTP upload directory
                var ftpUploadDirectory = !string.IsNullOrEmpty(_config.FtpUploadFolder) 
                    ? _config.FtpUploadFolder 
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CNC-FTP-SYNC", "FtpUpload");

                var destinationPath = Path.Combine(ftpUploadDirectory, projectName);

                // Ensure FTP upload directory exists
                Directory.CreateDirectory(ftpUploadDirectory);

                // For Simple FTP Upload, always use timestamp-based file processing
                // This ensures only changed files are uploaded, not entire directories
                await ProcessIndividualFilesAsync(projectPath, ftpUploadDirectory);
                result.OutputPath = ftpUploadDirectory;

                result.Success = true;
                result.Message = $"Successfully processed Simple FTP Upload for: {projectPath}";
                result.OutputPath = result.OutputPath ?? ftpUploadDirectory;
                _logger.LogInfo(result.Message);
            }
            catch (Exception ex)
            {
                result.Message = $"Error processing Simple FTP Upload: {ex.Message}";
                result.Errors.Add(ex.ToString());
                _logger.LogError(result.Message, ex);
            }

            return result;
        }

        private async Task CopyDirectoryAsync(string sourceDir, string destDir)
        {
            // Create destination directory
            Directory.CreateDirectory(destDir);

            // Copy all files
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var fileName = Path.GetFileName(file);
                var destFile = Path.Combine(destDir, fileName);
                File.Copy(file, destFile, true);
                _logger.LogInfo($"Copied file: {fileName}");
            }

            // Copy all subdirectories recursively
            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                var dirName = Path.GetFileName(subDir);
                var destSubDir = Path.Combine(destDir, dirName);
                await CopyDirectoryAsync(subDir, destSubDir);
            }
        }

        public ProjectInfo AnalyzeProject(string projectPath)
        {
            var projectInfo = new ProjectInfo
            {
                ProjectPath = projectPath,
                ProjectName = Path.GetFileName(projectPath)
            };

            try
            {
                // Find all CYC files (excluding ORIGINAL_ files)
                var cycFiles = Directory.GetFiles(projectPath, "*.cyc", SearchOption.AllDirectories)
                    .Where(f => !Path.GetFileName(f).StartsWith("ORIGINAL_"))
                    .Select(f => new FileInfo(f))
                    .ToList();

                projectInfo.CycFiles = cycFiles;

                if (cycFiles.Any())
                {
                    // Extract revision numbers and find the latest
                    var revisions = new List<string>();
                    foreach (var file in cycFiles)
                    {
                        var fileName = file.Name;
                        var revIndex = fileName.LastIndexOf("R");
                        if (revIndex >= 0 && revIndex + 3 < fileName.Length)
                        {
                            var revision = fileName.Substring(revIndex + 1, 2);
                            revisions.Add(revision);
                        }
                    }

                    if (revisions.Any())
                    {
                        projectInfo.LatestRevision = revisions.OrderBy(r => r).Last();
                        _logger.LogInfo($"Found latest revision: {projectInfo.LatestRevision}");

                        // Find files for the latest revision
                        projectInfo.NcFiles = Directory.GetFiles(projectPath, $"*R{projectInfo.LatestRevision}.nc", SearchOption.AllDirectories)
                            .Select(f => new FileInfo(f)).ToList();

                        projectInfo.CycFiles = cycFiles.Where(f => f.Name.Contains($"R{projectInfo.LatestRevision}")).ToList();
                    }
                }

                // Find other file types
                projectInfo.XmlFiles = Directory.GetFiles(projectPath, "*.xml", SearchOption.AllDirectories)
                    .Select(f => new FileInfo(f)).ToList();

                projectInfo.JpgFiles = Directory.GetFiles(projectPath, "*.JPG", SearchOption.AllDirectories)
                    .Select(f => new FileInfo(f)).ToList();

                _logger.LogInfo($"Project analysis complete - NC: {projectInfo.NcFiles.Count}, " +
                              $"CYC: {projectInfo.CycFiles.Count}, XML: {projectInfo.XmlFiles.Count}, JPG: {projectInfo.JpgFiles.Count}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error analyzing project {projectPath}", ex);
            }

            return projectInfo;
        }

        private async Task CreateAndCleanSubdirectoriesAsync(ProjectInfo projectInfo)
        {
            var workingPath = string.IsNullOrEmpty(projectInfo.FtpWorkingPath) ? projectInfo.ProjectPath : projectInfo.FtpWorkingPath;
            var ncPath = Path.Combine(workingPath, "NC");
            var autoLabelPath = Path.Combine(workingPath, "AutoStickLabel");

            var paths = new[] { ncPath, autoLabelPath };

            foreach (var path in paths)
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                        _logger.LogInfo($"Deleted existing directory: {path}");
                    }

                    Directory.CreateDirectory(path);
                    _logger.LogInfo($"Created directory: {path}");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error managing directory {path}", ex);
                    throw;
                }
            }

            await Task.CompletedTask;
        }

        private async Task MoveNcFilesAsync(ProjectInfo projectInfo)
        {
            var workingPath = string.IsNullOrEmpty(projectInfo.FtpWorkingPath) ? projectInfo.ProjectPath : projectInfo.FtpWorkingPath;
            var ncPath = Path.Combine(workingPath, "NC");
            var moveCount = 0;

            // Search for NC files in working directory using revision pattern (like PowerShell)
            var searchPattern = $"*R{projectInfo.LatestRevision}.nc";
            var ncFiles = Directory.GetFiles(workingPath, searchPattern, SearchOption.AllDirectories);

            foreach (var ncFile in ncFiles)
            {
                try
                {
                    var fileName = Path.GetFileName(ncFile);
                    var destinationPath = Path.Combine(ncPath, fileName);
                    File.Move(ncFile, destinationPath);
                    _logger.LogInfo($"Moved NC file: {fileName}");
                    moveCount++;
                }
                catch (Exception ex)
                {
                    var fileName = Path.GetFileName(ncFile);
                    _logger.LogError($"Error moving NC file {fileName} - Exception: {ex.Message}");
                }
            }

            _logger.LogInfo($"Moved {moveCount} NC files to NC folder");
            await Task.CompletedTask;
        }

        private async Task MoveCycFilesAsync(ProjectInfo projectInfo)
        {
            var workingPath = string.IsNullOrEmpty(projectInfo.FtpWorkingPath) ? projectInfo.ProjectPath : projectInfo.FtpWorkingPath;
            var autoLabelPath = Path.Combine(workingPath, "AutoStickLabel");
            var moveCount = 0;

            // Search for CYC files in working directory using revision pattern (like PowerShell)
            var searchPattern = $"*R{projectInfo.LatestRevision}.cyc";
            var cycFiles = Directory.GetFiles(workingPath, searchPattern, SearchOption.AllDirectories);

            foreach (var cycFile in cycFiles)
            {
                try
                {
                    var fileName = Path.GetFileName(cycFile);
                    var destinationPath = Path.Combine(autoLabelPath, fileName);
                    File.Move(cycFile, destinationPath);
                    _logger.LogInfo($"Moved CYC file: {fileName}");
                    moveCount++;
                }
                catch (Exception ex)
                {
                    var fileName = Path.GetFileName(cycFile);
                    _logger.LogError($"Error moving CYC file {fileName} - Exception: {ex.Message}");
                }
            }

            _logger.LogInfo($"Moved {moveCount} CYC files to AutoStickLabel folder");
            await Task.CompletedTask;
        }

        private async Task MoveJpgFilesAsync(ProjectInfo projectInfo)
        {
            var workingPath = string.IsNullOrEmpty(projectInfo.FtpWorkingPath) ? projectInfo.ProjectPath : projectInfo.FtpWorkingPath;
            var autoLabelPath = Path.Combine(workingPath, "AutoStickLabel");
            var moveCount = 0;

            // Search for JPG files in working directory (like PowerShell)
            var jpgFiles = Directory.GetFiles(workingPath, "*.JPG", SearchOption.AllDirectories);

            foreach (var jpgFile in jpgFiles)
            {
                try
                {
                    var fileName = Path.GetFileName(jpgFile);
                    var destinationPath = Path.Combine(autoLabelPath, fileName);
                    File.Move(jpgFile, destinationPath);
                    _logger.LogInfo($"Moved JPG file: {fileName}");
                    moveCount++;
                }
                catch (Exception ex)
                {
                    var fileName = Path.GetFileName(jpgFile);
                    _logger.LogError($"Error moving JPG file {fileName} - Exception: {ex.Message}");
                }
            }

            _logger.LogInfo($"Moved {moveCount} JPG files to AutoStickLabel folder");
            await Task.CompletedTask;
        }

        private async Task MoveXmlFilesAsync(ProjectInfo projectInfo)
        {
            var workingPath = string.IsNullOrEmpty(projectInfo.FtpWorkingPath) ? projectInfo.ProjectPath : projectInfo.FtpWorkingPath;
            var autoLabelPath = Path.Combine(workingPath, "AutoStickLabel");
            var moveCount = 0;

            // Search for XML files in working directory (like PowerShell)
            var xmlFiles = Directory.GetFiles(workingPath, "*.xml", SearchOption.AllDirectories);

            foreach (var xmlFile in xmlFiles)
            {
                try
                {
                    var fileName = Path.GetFileName(xmlFile);
                    var destinationPath = Path.Combine(autoLabelPath, fileName);
                    File.Move(xmlFile, destinationPath);
                    _logger.LogInfo($"Moved XML file: {fileName}");
                    moveCount++;
                }
                catch (Exception ex)
                {
                    var fileName = Path.GetFileName(xmlFile);
                    _logger.LogError($"Error moving XML file {fileName} - Exception: {ex.Message}");
                }
            }

            _logger.LogInfo($"Moved {moveCount} XML files to AutoStickLabel folder");
            await Task.CompletedTask;
        }

        private async Task ProcessCycCoordinatesAsync(ProjectInfo projectInfo)
        {
            var workingPath = string.IsNullOrEmpty(projectInfo.FtpWorkingPath) ? projectInfo.ProjectPath : projectInfo.FtpWorkingPath;
            var autoLabelPath = Path.Combine(workingPath, "AutoStickLabel");
            var cycFiles = Directory.GetFiles(autoLabelPath, "*.cyc")
                .Where(f => !Path.GetFileName(f).StartsWith("ORIGINAL_"))
                .ToList();

            var processedCount = 0;

            foreach (var cycFile in cycFiles)
            {
                try
                {
                    _logger.LogInfo($"Processing CYC file: {Path.GetFileName(cycFile)}");

                    // Read file content and process with simple string replacement
                    var content = File.ReadAllText(cycFile);
                    var originalContent = content;
                    
                    // Replace negative Y values: look for <Field Name="Y" Value="-xxx" and remove the minus
                    var pattern = @"(<Field Name=""Y"" Value="")(-)([\d\.]+""[^>]*>)";
                    var replacement = "$1$3"; // Keep everything except the minus sign
                    
                    content = System.Text.RegularExpressions.Regex.Replace(content, pattern, replacement);
                    
                    var updatedFields = System.Text.RegularExpressions.Regex.Matches(originalContent, pattern).Count;
                    
                    // Write the updated content back to file as UTF-8
                    File.WriteAllText(cycFile, content, Encoding.UTF8);

                    _logger.LogInfo($"CYC file processed: {Path.GetFileName(cycFile)} - {updatedFields} Y coordinates updated, converted to UTF-8");
                    processedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error processing CYC file {cycFile}", ex);
                }
            }

            _logger.LogInfo($"Processed {processedCount} CYC files with coordinate updates and UTF-8 conversion");
            await Task.CompletedTask;
        }

        private async Task CopyProjectToFtpWorkingAreaAsync(ProjectInfo projectInfo)
        {
            try
            {
                var ftpFolderName = $"{projectInfo.ProjectName}-{projectInfo.LatestRevision}";
                var ftpWorkingPath = Path.Combine(_config.FtpUploadFolder, ftpFolderName);

                if (Directory.Exists(ftpWorkingPath))
                {
                    _logger.LogWarning($"FTP working folder already exists, cleaning: {ftpWorkingPath}");
                    Directory.Delete(ftpWorkingPath, true);
                }

                Directory.CreateDirectory(ftpWorkingPath);
                _logger.LogInfo($"Created FTP working folder: {ftpWorkingPath}");

                // Copy entire project to FTP working area
                CopyDirectory(projectInfo.ProjectPath, ftpWorkingPath, true);
                projectInfo.FtpWorkingPath = ftpWorkingPath;
                
                _logger.LogInfo($"Successfully copied entire project to FTP working area: {ftpFolderName}");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error copying project to FTP working area", ex);
                throw;
            }

            await Task.CompletedTask;
        }

        private async Task CreateFtpUploadFolderAsync(ProjectInfo projectInfo)
        {
            try
            {
                var ftpFolderName = $"{projectInfo.ProjectName}-{projectInfo.LatestRevision}";
                var ftpUploadPath = Path.Combine(_config.FtpUploadFolder, ftpFolderName);

                if (Directory.Exists(ftpUploadPath))
                {
                    _logger.LogWarning($"FTP upload folder already exists: {ftpUploadPath}");
                    return;
                }

                Directory.CreateDirectory(ftpUploadPath);
                _logger.LogInfo($"Created FTP upload folder: {ftpUploadPath}");

                // Copy NC and AutoStickLabel folders to FTP upload directory
                var ncSource = Path.Combine(projectInfo.ProjectPath, "NC");
                var autoLabelSource = Path.Combine(projectInfo.ProjectPath, "AutoStickLabel");

                if (Directory.Exists(ncSource))
                {
                    var ncDest = Path.Combine(ftpUploadPath, "NC");
                    CopyDirectory(ncSource, ncDest, true);
                    _logger.LogInfo($"Copied NC folder to FTP upload directory");
                }

                if (Directory.Exists(autoLabelSource))
                {
                    var autoLabelDest = Path.Combine(ftpUploadPath, "AutoStickLabel");
                    CopyDirectory(autoLabelSource, autoLabelDest, true);
                    _logger.LogInfo($"Copied AutoStickLabel folder to FTP upload directory");
                }

                _logger.LogInfo($"Successfully prepared project for FTP upload: {ftpFolderName}");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error creating FTP upload folder", ex);
                throw;
            }

            await Task.CompletedTask;
        }

        private static void CopyDirectory(string sourceDir, string destinationDir, bool recursive)
        {
            var dir = new DirectoryInfo(sourceDir);

            if (!dir.Exists)
                throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

            DirectoryInfo[] dirs = dir.GetDirectories();
            Directory.CreateDirectory(destinationDir);

            foreach (FileInfo file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath);
            }

            if (recursive)
            {
                foreach (DirectoryInfo subDir in dirs)
                {
                    string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                    CopyDirectory(subDir.FullName, newDestinationDir, true);
                }
            }
        }

        private async Task ProcessIndividualFilesAsync(string folderPath, string ftpUploadDirectory)
        {
            if (_folderWatcher == null)
            {
                _logger.LogWarning("FolderWatcher not available for individual file processing, processing entire folder");
                await ProcessEntireFolderAsync(folderPath, ftpUploadDirectory);
                return;
            }

            // Get the timestamp when activity started in this root folder
            var activityTimestamp = _folderWatcher.GetRootFolderTimestamp(folderPath);
            
            if (activityTimestamp == null)
            {
                _logger.LogWarning($"No activity timestamp found for folder: {folderPath}");
                return;
            }

            _logger.LogInfo($"Processing files in root folder: {folderPath} created since {activityTimestamp}");

            // Find all files in the folder tree that were created since the activity timestamp
            var recentFiles = GetFilesCreatedSince(folderPath, activityTimestamp.Value);
            
            _logger.LogInfo($"Found {recentFiles.Count} files created since {activityTimestamp}");

            if (recentFiles.Count == 0)
            {
                _logger.LogWarning($"No recent files found in folder: {folderPath}");
                _folderWatcher.ClearRootFolderTimestamp(folderPath);
                return;
            }

            // Process each recent file
            foreach (var filePath in recentFiles)
            {
                try
                {
                    if (!File.Exists(filePath))
                    {
                        _logger.LogWarning($"File no longer exists, skipping: {filePath}");
                        continue;
                    }

                    // Calculate the relative path from the watch folder to maintain structure
                    var relativePath = Path.GetRelativePath(_config.WatchFolder, filePath);
                    var destPath = Path.Combine(ftpUploadDirectory, relativePath);
                    
                    // Ensure destination directory exists
                    var destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }
                    
                    // Copy the specific file
                    File.Copy(filePath, destPath, true);
                    _logger.LogInfo($"Copied recent file: {filePath} -> {destPath}");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to copy file {filePath}: {ex.Message}");
                }
            }

            // Clear the timestamp for this folder since it's been processed
            _folderWatcher.ClearRootFolderTimestamp(folderPath);
            
            await Task.CompletedTask;
        }

        private async Task ProcessEntireFolderAsync(string folderPath, string ftpUploadDirectory)
        {
            // Fallback method for when folder watcher is not available
            _logger.LogInfo($"Processing entire folder: {folderPath}");
            
            var files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);
            
            foreach (var filePath in files)
            {
                try
                {
                    var relativePath = Path.GetRelativePath(_config.WatchFolder, filePath);
                    var destPath = Path.Combine(ftpUploadDirectory, relativePath);
                    
                    var destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }
                    
                    File.Copy(filePath, destPath, true);
                    _logger.LogInfo($"Copied file: {filePath} -> {destPath}");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to copy file {filePath}: {ex.Message}");
                }
            }
            
            await Task.CompletedTask;
        }

        private List<string> GetFilesCreatedSince(string folderPath, DateTime timestamp)
        {
            var recentFiles = new List<string>();
            
            try
            {
                // Get all files in the folder tree
                var allFiles = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);
                
                foreach (var filePath in allFiles)
                {
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        // Check if file was created since the timestamp (with small buffer for timing precision)
                        if (fileInfo.CreationTime >= timestamp.AddSeconds(-1) || fileInfo.LastWriteTime >= timestamp.AddSeconds(-1))
                        {
                            recentFiles.Add(filePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Could not check file timestamps for {filePath}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error scanning folder for recent files {folderPath}: {ex.Message}");
            }
            
            return recentFiles;
        }
    }
}