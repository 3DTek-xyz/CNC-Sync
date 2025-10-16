using CNCFTPSyncCore.Models;
using CNCFTPSyncCore.Services;
using System.Linq;
using System.Net;

namespace CNCFTPSyncCore.Services
{
    public interface IFtpService
    {
        Task<bool> UploadDirectoryAsync(string localPath, string remotePath = "");
        Task<bool> TestConnectionAsync();
        Task<bool> CreateDirectoryAsync(string remotePath);
        Task<bool> UploadFileAsync(string localFilePath, string remoteFilePath);
        Task<List<FtpFileInfo>> ListDirectoryAsync(string remotePath = "/");
        Task<bool> DeleteFileAsync(string remoteFilePath);
        Task<bool> DeleteDirectoryAsync(string remoteDirectoryPath);
        Task<bool> DownloadFileAsync(string remoteFilePath, string localFilePath);
    }

    public class FtpService : IFtpService
    {
        private readonly ILogService _logger;
        private readonly SyncConfiguration _config;

        public FtpService(ILogService logger, SyncConfiguration config)
        {
            _logger = logger;
            _config = config;
        }

        public async Task<bool> TestConnectionAsync()
        {
            var ftpStopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var connectionInfo = $"FTP Server: {_config.FtpServer}:{_config.FtpPort}, " +
                                   $"User: {(_config.UseAnonymousFtp ? "anonymous" : _config.FtpUsername)}, " +
                                   $"Mode: {(_config.UseAnonymousFtp ? "Anonymous" : "Authenticated")}";
                
                _logger.LogInfo($"STARTUP TIMING: Testing FTP connection started at {DateTime.Now:HH:mm:ss.fff} - {connectionInfo}");

                _logger.LogInfo($"STARTUP TIMING: Creating FTP request at {ftpStopwatch.ElapsedMilliseconds}ms");
                var request = CreateFtpRequest("/", WebRequestMethods.Ftp.ListDirectory);
                
                _logger.LogInfo($"STARTUP TIMING: Getting FTP response at {ftpStopwatch.ElapsedMilliseconds}ms");
                using var response = (FtpWebResponse)await request.GetResponseAsync();
                using var responseStream = response.GetResponseStream();
                using var reader = new StreamReader(responseStream);
                
                _logger.LogInfo($"STARTUP TIMING: Reading FTP response at {ftpStopwatch.ElapsedMilliseconds}ms");
                var result = await reader.ReadToEndAsync();
                
                _logger.LogInfo($"STARTUP TIMING: FTP connection test successful at {ftpStopwatch.ElapsedMilliseconds}ms - {connectionInfo}. Status: {response.StatusCode} - {response.StatusDescription}");
                return true;
            }
            catch (WebException webEx)
            {
                var connectionInfo = $"FTP Server: {_config.FtpServer}:{_config.FtpPort}, " +
                                   $"User: {(_config.UseAnonymousFtp ? "anonymous" : _config.FtpUsername)}, " +
                                   $"Mode: {(_config.UseAnonymousFtp ? "Anonymous" : "Authenticated")}";

                // Check if this is an FTP response with status code
                if (webEx.Response is FtpWebResponse ftpResponse)
                {
                    _logger.LogInfo($"FTP response received - Status: {ftpResponse.StatusCode} - {ftpResponse.StatusDescription}");
                    
                    // Use comprehensive FTP success code checker
                    if (IsFtpSuccessCode(ftpResponse.StatusCode))
                    {
                        _logger.LogInfo($"FTP connection successful (WebException with success status) - {connectionInfo}. Status: {ftpResponse.StatusCode}: {ftpResponse.StatusDescription}");
                        return true;
                    }
                    else
                    {
                        _logger.LogError($"FTP connection failed - {connectionInfo}. Status: {ftpResponse.StatusCode} - {ftpResponse.StatusDescription}");
                        return false;
                    }
                }
                else
                {
                    _logger.LogError($"FTP connection failed - {connectionInfo}. WebException: {webEx.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                var connectionInfo = $"FTP Server: {_config.FtpServer}:{_config.FtpPort}, " +
                                   $"User: {(_config.UseAnonymousFtp ? "anonymous" : _config.FtpUsername)}, " +
                                   $"Mode: {(_config.UseAnonymousFtp ? "Anonymous" : "Authenticated")}";
                
                _logger.LogError($"FTP connection test failed - {connectionInfo}. Error: {ex.Message}", ex);
                return false;
            }
        }

        public async Task<bool> UploadDirectoryAsync(string localPath, string remotePath = "")
        {
            try
            {
                if (!Directory.Exists(localPath))
                {
                    _logger.LogError($"Local directory does not exist: {localPath}");
                    return false;
                }

                var directoryName = Path.GetFileName(localPath);
                if (string.IsNullOrEmpty(remotePath))
                {
                    remotePath = directoryName;
                }

                _logger.LogInfo($"Starting FTP upload: {localPath} -> {remotePath}");

                // Create remote directory
                await CreateDirectoryAsync(remotePath);

                // Upload all files and subdirectories
                var success = await UploadDirectoryRecursiveAsync(localPath, remotePath);

                if (success)
                {
                    _logger.LogInfo($"FTP upload completed successfully: {remotePath}");
                }
                else
                {
                    _logger.LogError($"FTP upload completed with errors: {remotePath}");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during FTP upload of directory {localPath}", ex);
                return false;
            }
        }

        private async Task<bool> UploadDirectoryRecursiveAsync(string localPath, string remotePath)
        {
            var success = true;

            try
            {
                // Upload files in current directory
                var files = Directory.GetFiles(localPath);
                foreach (var filePath in files)
                {
                    var fileName = Path.GetFileName(filePath);
                    var remoteFilePath = $"{remotePath}/{fileName}";
                    
                    if (!await UploadFileAsync(filePath, remoteFilePath))
                    {
                        success = false;
                    }
                }

                // Upload subdirectories
                var directories = Directory.GetDirectories(localPath);
                foreach (var directoryPath in directories)
                {
                    var directoryName = Path.GetFileName(directoryPath);
                    var remoteDirectoryPath = $"{remotePath}/{directoryName}";
                    
                    await CreateDirectoryAsync(remoteDirectoryPath);
                    
                    if (!await UploadDirectoryRecursiveAsync(directoryPath, remoteDirectoryPath))
                    {
                        success = false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error uploading directory contents: {localPath}", ex);
                success = false;
            }

            return success;
        }

        public async Task<bool> UploadFileAsync(string localFilePath, string remoteFilePath)
        {
            try
            {
                if (!File.Exists(localFilePath))
                {
                    _logger.LogError($"Local file does not exist: {localFilePath}");
                    return false;
                }

                _logger.LogInfo($"Uploading file: {Path.GetFileName(localFilePath)} -> {remoteFilePath}");

                var request = CreateFtpRequest(remoteFilePath, WebRequestMethods.Ftp.UploadFile);
                
                // Upload file content
                using var fileStream = File.OpenRead(localFilePath);
                using var ftpStream = await request.GetRequestStreamAsync();
                
                await fileStream.CopyToAsync(ftpStream);

                // Get response
                using var response = (FtpWebResponse)await request.GetResponseAsync();
                
                _logger.LogInfo($"File uploaded successfully: {Path.GetFileName(localFilePath)} - {response.StatusDescription}");
                return true;
            }
            catch (WebException webEx)
            {
                if (webEx.Response is FtpWebResponse ftpResponse)
                {
                    // Use comprehensive FTP success code checker
                    if (IsFtpSuccessCode(ftpResponse.StatusCode))
                    {
                        _logger.LogInfo($"File uploaded successfully: {Path.GetFileName(localFilePath)} - {ftpResponse.StatusCode}: {ftpResponse.StatusDescription}");
                        return true;
                    }
                    else
                    {
                        _logger.LogError($"FTP file upload failed for {localFilePath}. Status: {ftpResponse.StatusCode} - {ftpResponse.StatusDescription}");
                        return false;
                    }
                }
                else
                {
                    _logger.LogError($"FTP file upload failed for {localFilePath}. WebException: {webEx.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error uploading file {localFilePath}", ex);
                return false;
            }
        }

        public async Task<bool> CreateDirectoryAsync(string remotePath)
        {
            try
            {
                // Check if directory already exists
                if (await DirectoryExistsAsync(remotePath))
                {
                    _logger.LogInfo($"Remote directory already exists: {remotePath}");
                    return true;
                }

                _logger.LogInfo($"Creating remote directory: {remotePath}");

                var request = CreateFtpRequest(remotePath, WebRequestMethods.Ftp.MakeDirectory);
                
                using var response = (FtpWebResponse)await request.GetResponseAsync();
                
                _logger.LogInfo($"Remote directory created: {remotePath} - {response.StatusDescription}");
                return true;
            }
            catch (WebException ex) when (ex.Response is FtpWebResponse ftpResponse)
            {
                // Use comprehensive FTP success code checker
                if (IsFtpSuccessCode(ftpResponse.StatusCode))
                {
                    _logger.LogInfo($"Remote directory created successfully: {remotePath} - {ftpResponse.StatusCode}: {ftpResponse.StatusDescription}");
                    return true;
                }
                else if (ftpResponse.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable)
                {
                    // Directory might already exist - treat as success
                    _logger.LogInfo($"Directory creation skipped (may already exist): {remotePath}");
                    return true;
                }
                else
                {
                    _logger.LogError($"FTP error creating directory {remotePath} - Status: {ftpResponse.StatusCode} - {ftpResponse.StatusDescription}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error creating remote directory {remotePath}", ex);
                return false;
            }
        }

        private async Task<bool> DirectoryExistsAsync(string remotePath)
        {
            try
            {
                // Get parent directory and target directory name
                var normalizedPath = remotePath.Replace('\\', '/').TrimEnd('/');
                var lastSlashIndex = normalizedPath.LastIndexOf('/');
                
                string parentPath;
                string targetDirName;
                
                if (lastSlashIndex <= 0)
                {
                    // Root level directory
                    parentPath = "/";
                    targetDirName = normalizedPath.TrimStart('/');
                }
                else
                {
                    parentPath = normalizedPath.Substring(0, lastSlashIndex);
                    targetDirName = normalizedPath.Substring(lastSlashIndex + 1);
                }
                
                if (string.IsNullOrEmpty(parentPath) || parentPath == "")
                {
                    parentPath = "/";
                }
                
                _logger.LogInfo($"Checking if directory exists - Parent: '{parentPath}', Target: '{targetDirName}'");
                
                // List parent directory contents
                var items = await ListDirectoryAsync(parentPath);
                
                // Look for target directory in the listing
                var directoryExists = items.Any(item => 
                    item.IsDirectory && 
                    string.Equals(item.Name, targetDirName, StringComparison.OrdinalIgnoreCase));
                
                _logger.LogInfo($"Directory exists check result: {directoryExists} for {remotePath}");
                return directoryExists;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error checking if directory exists: {remotePath}", ex);
                return false;
            }
        }

        private FtpWebRequest CreateFtpRequest(string remotePath, string method)
        {
            var uri = new Uri($"ftp://{_config.FtpServer}:{_config.FtpPort}/{remotePath.TrimStart('/')}");
#pragma warning disable SYSLIB0014 // WebRequest is obsolete but FtpWebRequest is still the standard for FTP
            var request = (FtpWebRequest)WebRequest.Create(uri);
#pragma warning restore SYSLIB0014
            
            request.Method = method;
            request.UseBinary = true;
            request.UsePassive = true;
            request.KeepAlive = false;
            
            if (_config.UseAnonymousFtp)
            {
                request.Credentials = new NetworkCredential("anonymous", "anonymous@example.com");
            }
            else
            {
                request.Credentials = new NetworkCredential(_config.FtpUsername, _config.FtpPassword);
            }

            return request;
        }

        private bool IsFtpSuccessCode(FtpStatusCode statusCode)
        {
            // Comprehensive FTP success code checker covering all standard 2xx success responses
            int code = (int)statusCode;
            
            // All 2xx codes are success codes
            if (code >= 200 && code < 300)
            {
                return true;
            }
            
            // Also check specific known success codes by enum value
            return statusCode switch
            {
                FtpStatusCode.CommandOK => true,                    // 200 - Command okay
                FtpStatusCode.CommandNotImplemented => true,        // 202 - Command not implemented, superfluous at this site
                FtpStatusCode.DirectoryStatus => true,              // 212 - Directory status
                FtpStatusCode.FileStatus => true,                   // 213 - File status
                FtpStatusCode.ClosingData => true,                  // 226 - Closing data connection. File action successful
                FtpStatusCode.EnteringPassive => true,              // 227 - Entering Passive Mode
                FtpStatusCode.LoggedInProceed => true,              // 230 - User logged in, proceed
                FtpStatusCode.FileActionOK => true,                 // 250 - Requested file action okay, completed
                FtpStatusCode.PathnameCreated => true,              // 257 - "PATHNAME" created
                _ => false
            };
        }

        public async Task<List<FtpFileInfo>> ListDirectoryAsync(string remotePath = "/")
        {
            var files = new List<FtpFileInfo>();
            
            try
            {
                _logger.LogInfo($"Listing FTP directory: {remotePath}");
                
                var request = CreateFtpRequest(remotePath, WebRequestMethods.Ftp.ListDirectoryDetails);
                
                using var response = (FtpWebResponse)await request.GetResponseAsync();
                using var responseStream = response.GetResponseStream();
                using var reader = new StreamReader(responseStream);
                
                string? line;
                while ((line = await reader.ReadLineAsync()) is not null)
                {
                    var fileInfo = ParseFtpListLine(line, remotePath);
                    if (fileInfo != null)
                    {
                        files.Add(fileInfo);
                    }
                    else
                    {
                        _logger.LogWarning($"Failed to parse FTP list line: '{line}'");
                    }
                }
                
                _logger.LogInfo($"Listed {files.Count} items from {remotePath}");
                return files;
            }
            catch (WebException webEx)
            {
                if (webEx.Response is FtpWebResponse ftpResponse)
                {
                    // Use comprehensive FTP success code checker
                    if (IsFtpSuccessCode(ftpResponse.StatusCode))
                    {
                        _logger.LogInfo($"FTP directory listing completed with success status: {remotePath} - {ftpResponse.StatusCode}: {ftpResponse.StatusDescription}");
                        // Note: If we got here via exception, the response stream was likely already consumed
                        // This is an edge case where FTP returns success via WebException
                        return files;
                    }
                    else
                    {
                        _logger.LogError($"FTP directory listing failed for {remotePath}. Status: {ftpResponse.StatusCode} - {ftpResponse.StatusDescription}");
                        return files; // Return empty list on error
                    }
                }
                else
                {
                    _logger.LogError($"FTP directory listing failed for {remotePath}. WebException: {webEx.Message}");
                    return files; // Return empty list on error
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"FTP directory listing failed for {remotePath}. Error: {ex.Message}", ex);
                return files; // Return empty list on error
            }
        }

        public async Task<bool> DeleteFileAsync(string remoteFilePath)
        {
            try
            {
                _logger.LogInfo($"Deleting FTP file: {remoteFilePath}");
                
                var request = CreateFtpRequest(remoteFilePath, WebRequestMethods.Ftp.DeleteFile);
                
                using var response = (FtpWebResponse)await request.GetResponseAsync();
                
                _logger.LogInfo($"Successfully deleted FTP file: {remoteFilePath}. Status: {response.StatusDescription}");
                return true;
            }
            catch (WebException webEx)
            {
                if (webEx.Response is FtpWebResponse ftpResponse)
                {
                    // Use comprehensive FTP success code checker
                    if (IsFtpSuccessCode(ftpResponse.StatusCode))
                    {
                        _logger.LogInfo($"Successfully deleted FTP file: {remoteFilePath} - {ftpResponse.StatusCode}: {ftpResponse.StatusDescription}");
                        return true;
                    }
                    else
                    {
                        _logger.LogError($"FTP file deletion failed for {remoteFilePath}. Status: {ftpResponse.StatusCode} - {ftpResponse.StatusDescription}");
                        return false;
                    }
                }
                else
                {
                    _logger.LogError($"FTP file deletion failed for {remoteFilePath}. WebException: {webEx.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"FTP file deletion failed for {remoteFilePath}. Error: {ex.Message}", ex);
                return false;
            }
        }

        public async Task<bool> DeleteDirectoryAsync(string remoteDirectoryPath)
        {
            try
            {
                _logger.LogInfo($"Deleting FTP directory: {remoteDirectoryPath}");
                
                // First, get the contents of the directory to delete files/subdirectories recursively
                var items = await ListDirectoryAsync(remoteDirectoryPath);
                
                // Delete all files and subdirectories first
                foreach (var item in items)
                {
                    if (item.IsDirectory)
                    {
                        // Recursively delete subdirectory
                        bool success = await DeleteDirectoryAsync(item.FullPath);
                        if (!success)
                        {
                            _logger.LogError($"Failed to delete subdirectory: {item.FullPath}");
                            return false;
                        }
                    }
                    else
                    {
                        // Delete file
                        bool success = await DeleteFileAsync(item.FullPath);
                        if (!success)
                        {
                            _logger.LogError($"Failed to delete file in directory: {item.FullPath}");
                            return false;
                        }
                    }
                }
                
                // Now delete the empty directory
                var request = CreateFtpRequest(remoteDirectoryPath, WebRequestMethods.Ftp.RemoveDirectory);
                
                using var response = (FtpWebResponse)await request.GetResponseAsync();
                
                _logger.LogInfo($"Successfully deleted FTP directory: {remoteDirectoryPath}");
                return true;
            }
            catch (WebException webEx)
            {
                if (webEx.Response is FtpWebResponse ftpResponse)
                {
                    // Use comprehensive FTP success code checker
                    if (IsFtpSuccessCode(ftpResponse.StatusCode))
                    {
                        _logger.LogInfo($"Successfully deleted FTP directory: {remoteDirectoryPath} - {ftpResponse.StatusCode}: {ftpResponse.StatusDescription}");
                        return true;
                    }
                    else
                    {
                        _logger.LogError($"FTP directory deletion failed for {remoteDirectoryPath}. Status: {ftpResponse.StatusCode} - {ftpResponse.StatusDescription}");
                        return false;
                    }
                }
                else
                {
                    _logger.LogError($"FTP directory deletion failed for {remoteDirectoryPath}. WebException: {webEx.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"FTP directory deletion failed for {remoteDirectoryPath}. Error: {ex.Message}", ex);
                return false;
            }
        }

        public async Task<bool> DownloadFileAsync(string remoteFilePath, string localFilePath)
        {
            try
            {
                _logger.LogInfo($"Downloading FTP file: {remoteFilePath} -> {localFilePath}");
                
                // Ensure local directory exists
                var localDir = Path.GetDirectoryName(localFilePath);
                if (!string.IsNullOrEmpty(localDir) && !Directory.Exists(localDir))
                {
                    Directory.CreateDirectory(localDir);
                }
                
                var request = CreateFtpRequest(remoteFilePath, WebRequestMethods.Ftp.DownloadFile);
                
                using var response = (FtpWebResponse)await request.GetResponseAsync();
                using var responseStream = response.GetResponseStream();
                using var fileStream = new FileStream(localFilePath, FileMode.Create, FileAccess.Write);
                
                await responseStream.CopyToAsync(fileStream);
                
                _logger.LogInfo($"Successfully downloaded FTP file: {remoteFilePath} -> {localFilePath}");
                return true;
            }
            catch (WebException webEx)
            {
                if (webEx.Response is FtpWebResponse ftpResponse)
                {
                    // Use comprehensive FTP success code checker
                    if (IsFtpSuccessCode(ftpResponse.StatusCode))
                    {
                        _logger.LogInfo($"Successfully downloaded FTP file: {remoteFilePath} -> {localFilePath} - {ftpResponse.StatusCode}: {ftpResponse.StatusDescription}");
                        return true;
                    }
                    else
                    {
                        _logger.LogError($"FTP file download failed: {remoteFilePath} -> {localFilePath}. Status: {ftpResponse.StatusCode} - {ftpResponse.StatusDescription}");
                        return false;
                    }
                }
                else
                {
                    _logger.LogError($"FTP file download failed: {remoteFilePath} -> {localFilePath}. WebException: {webEx.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"FTP file download failed: {remoteFilePath} -> {localFilePath}. Error: {ex.Message}", ex);
                return false;
            }
        }

        private FtpFileInfo? ParseFtpListLine(string line, string basePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(line)) return null;
                
                // Skip common non-content lines
                if (line.StartsWith("total ") || line.Trim() == "." || line.Trim() == "..")
                {
                    return null;
                }
                

                
                // Try different parsing approaches in order of likelihood
                
                // 1. Try Unix format first (most common on Linux/Unix FTP servers)
                // Format: "drwxrwxrwx   1 owner    group            0 Jan 01 12:00 filename"
                if (TryParseUnixFormat(line, basePath, out FtpFileInfo? unixFile) && unixFile != null)
                {
                    return unixFile;
                }
                
                // 2. Try DOS/Windows format (common with Windows FTP servers)
                // Format: "MM-dd-yy  HH:mmAM/PM       <DIR>          filename"
                // or:     "MM-dd-yy  HH:mmAM/PM            filesize filename"
                if (TryParseDosFormat(line, basePath, out FtpFileInfo? dosFile) && dosFile != null)
                {
                    return dosFile;
                }
                
                // 3. Try extended Unix format (some Unix servers with additional columns)
                if (TryParseExtendedUnixFormat(line, basePath, out FtpFileInfo? extUnixFile) && extUnixFile != null)
                {
                    return extUnixFile;
                }
                
                // 4. Try simple name-only format (minimal FTP servers)
                if (TryParseSimpleFormat(line, basePath, out FtpFileInfo? simpleFile) && simpleFile != null)
                {
                    return simpleFile;
                }
                
                _logger.LogWarning($"Could not parse FTP list line with any known format: '{line}'");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error parsing FTP list line '{line}': {ex.Message}");
                return null;
            }
        }

        private bool TryParseDosFormat(string line, string basePath, out FtpFileInfo? fileInfo)
        {
            fileInfo = null;
            try
            {
                // DOS format: "MM-dd-yy  HH:mmAM/PM       <DIR>          filename"
                // or:         "MM-dd-yy  HH:mmAM/PM            filesize filename"
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4) return false;
                
                fileInfo = new FtpFileInfo();
                
                // Parse date and time (first two parts)
                if (parts.Length >= 2)
                {
                    string dateStr = parts[0];
                    string timeStr = parts[1];
                    
                    // Try to parse DOS date format MM-dd-yy or MM/dd/yy
                    if (DateTime.TryParse($"{dateStr} {timeStr}", out DateTime parsedDate))
                    {
                        fileInfo.ModifiedDate = parsedDate;
                    }
                    else
                    {
                        fileInfo.ModifiedDate = DateTime.Now;
                    }
                }
                else
                {
                    fileInfo.ModifiedDate = DateTime.Now;
                }
                
                // Check for <DIR>
                int nameStartIndex = -1;
                
                for (int i = 2; i < parts.Length; i++)
                {
                    if (parts[i] == "<DIR>")
                    {
                        fileInfo.IsDirectory = true;
                        nameStartIndex = i + 1;
                        break;
                    }
                    else if (long.TryParse(parts[i], out long size))
                    {
                        fileInfo.Size = size;
                        nameStartIndex = i + 1;
                        break;
                    }
                }
                
                if (nameStartIndex < 0 || nameStartIndex >= parts.Length) return false;
                
                fileInfo.Name = string.Join(" ", parts.Skip(nameStartIndex));
                fileInfo.Type = fileInfo.IsDirectory ? "Folder" : "File";
                fileInfo.FullPath = basePath.TrimEnd('/') + "/" + fileInfo.Name;
                
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryParseUnixFormat(string line, string basePath, out FtpFileInfo? fileInfo)
        {
            fileInfo = null;
            try
            {
                if (line.Length < 10) return false;
                
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 9) return false;
                
                fileInfo = new FtpFileInfo();
                
                // Parse permissions and type (first character indicates file type)
                fileInfo.IsDirectory = line[0] == 'd';
                fileInfo.Type = fileInfo.IsDirectory ? "Folder" : "File";
                
                // Parse size (5th column in Unix format)
                if (long.TryParse(parts[4], out long size))
                {
                    fileInfo.Size = size;
                }
                else
                {
                    fileInfo.Size = 0; // Default for directories or unparseable sizes
                }
                
                // Parse date/time (columns 6-8: month, day, year/time)
                DateTime modifiedDate = DateTime.Now; // Default fallback
                if (parts.Length >= 8)
                {
                    try
                    {
                        string month = parts[5];
                        string day = parts[6];
                        string yearOrTime = parts[7];
                        
                        // Enhanced date parsing with multiple format support
                        if (yearOrTime.Contains(":"))
                        {
                            // Format: "Oct 16 01:15" - time for current year
                            int currentYear = DateTime.Now.Year;
                            
                            // Try multiple date format variations
                            string[] dateFormats = {
                                $"{month} {day} {currentYear} {yearOrTime}",
                                $"{day} {month} {currentYear} {yearOrTime}",
                                $"{month}/{day}/{currentYear} {yearOrTime}",
                                $"{day}/{month}/{currentYear} {yearOrTime}"
                            };
                            
                            bool parsed = false;
                            foreach (var format in dateFormats)
                            {
                                if (DateTime.TryParse(format, out DateTime parsedDate))
                                {
                                    modifiedDate = parsedDate;
                                    parsed = true;
                                    break;
                                }
                            }
                            
                            if (!parsed)
                            {
                                _logger.LogWarning($"Could not parse Unix FTP date format: '{month} {day} {yearOrTime}' - using current time");
                            }
                        }
                        else
                        {
                            // Format: "Oct 16 2023" - explicit year
                            string[] dateFormats = {
                                $"{month} {day} {yearOrTime}",
                                $"{day} {month} {yearOrTime}",
                                $"{month}/{day}/{yearOrTime}",
                                $"{day}/{month}/{yearOrTime}"
                            };
                            
                            bool parsed = false;
                            foreach (var format in dateFormats)
                            {
                                if (DateTime.TryParse(format, out DateTime parsedDate))
                                {
                                    modifiedDate = parsedDate;
                                    parsed = true;
                                    break;
                                }
                            }
                            
                            if (!parsed)
                            {
                                _logger.LogWarning($"Could not parse Unix FTP date format: '{month} {day} {yearOrTime}' - using current time");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Exception parsing Unix FTP date from '{parts[5]} {parts[6]} {parts[7]}': {ex.Message}");
                    }
                }
                
                fileInfo.ModifiedDate = modifiedDate;
                
                // Parse filename (everything from 9th column onwards, may contain spaces)
                var nameStartIndex = 8; // Start from 9th column for filename
                if (parts.Length > nameStartIndex)
                {
                    fileInfo.Name = string.Join(" ", parts.Skip(nameStartIndex));
                    
                    // Remove symbolic link info if present (indicated by "->")
                    var linkIndex = fileInfo.Name.IndexOf(" -> ");
                    if (linkIndex > 0)
                    {
                        fileInfo.Name = fileInfo.Name.Substring(0, linkIndex);
                    }
                }
                else
                {
                    _logger.LogWarning($"Unix FTP format: Could not extract filename from line: '{line}'");
                    return false;
                }
                
                fileInfo.FullPath = basePath.TrimEnd('/') + "/" + fileInfo.Name;
                

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error parsing Unix FTP format line '{line}': {ex.Message}");
                return false;
            }
        }

        private bool TryParseExtendedUnixFormat(string line, string basePath, out FtpFileInfo? fileInfo)
        {
            fileInfo = null;
            try
            {
                // Handle alternate Unix formats with different column layouts
                // Some servers might have different spacing or additional columns
                if (line.Length < 10) return false;
                
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 6) return false; // Minimum: permissions, links, owner, group, size, name
                
                // Must start with permissions (first char is file type)
                if (parts[0].Length < 10 || (parts[0][0] != 'd' && parts[0][0] != '-' && parts[0][0] != 'l'))
                {
                    return false;
                }
                
                fileInfo = new FtpFileInfo();
                
                // Parse file type from first character of permissions
                fileInfo.IsDirectory = parts[0][0] == 'd';
                fileInfo.Type = fileInfo.IsDirectory ? "Folder" : "File";
                
                // Find the size column (scan for a numeric value)
                int sizeIndex = -1;
                int nameStartIndex = -1;
                
                for (int i = 1; i < parts.Length; i++)
                {
                    if (long.TryParse(parts[i], out long size))
                    {
                        fileInfo.Size = size;
                        sizeIndex = i;
                        
                        // Look for date/time pattern after size
                        // Date typically follows pattern: month day time/year
                        if (i + 3 < parts.Length)
                        {
                            nameStartIndex = i + 4; // Skip size, month, day, time/year
                            
                            // Try to parse date
                            try
                            {
                                string month = parts[i + 1];
                                string day = parts[i + 2];
                                string yearOrTime = parts[i + 3];
                                
                                DateTime modifiedDate = DateTime.Now;
                                if (yearOrTime.Contains(":"))
                                {
                                    // Time format - current year
                                    string dateStr = $"{month} {day} {DateTime.Now.Year} {yearOrTime}";
                                    if (DateTime.TryParse(dateStr, out DateTime parsed))
                                    {
                                        modifiedDate = parsed;
                                    }
                                }
                                else
                                {
                                    // Year format
                                    string dateStr = $"{month} {day} {yearOrTime}";
                                    if (DateTime.TryParse(dateStr, out DateTime parsed))
                                    {
                                        modifiedDate = parsed;
                                    }
                                }
                                fileInfo.ModifiedDate = modifiedDate;
                            }
                            catch
                            {
                                fileInfo.ModifiedDate = DateTime.Now;
                            }
                        }
                        else
                        {
                            // Not enough columns for date, filename starts after size
                            nameStartIndex = i + 1;
                            fileInfo.ModifiedDate = DateTime.Now;
                        }
                        break;
                    }
                }
                
                // If we couldn't find size, try a different approach
                if (sizeIndex == -1)
                {
                    fileInfo.Size = 0;
                    // Assume filename starts from a reasonable position
                    nameStartIndex = Math.Min(5, parts.Length - 1);
                    fileInfo.ModifiedDate = DateTime.Now;
                }
                
                // Extract filename (join remaining parts)
                if (nameStartIndex > 0 && nameStartIndex < parts.Length)
                {
                    fileInfo.Name = string.Join(" ", parts.Skip(nameStartIndex));
                    
                    // Handle symbolic links
                    var linkIndex = fileInfo.Name.IndexOf(" -> ");
                    if (linkIndex > 0)
                    {
                        fileInfo.Name = fileInfo.Name.Substring(0, linkIndex);
                    }
                }
                else
                {
                    return false; // Couldn't extract filename
                }
                
                fileInfo.FullPath = basePath.TrimEnd('/') + "/" + fileInfo.Name;
                

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error parsing extended Unix FTP format line '{line}': {ex.Message}");
                return false;
            }
        }

        private bool TryParseSimpleFormat(string line, string basePath, out FtpFileInfo? fileInfo)
        {
            fileInfo = null;
            try
            {
                // Simple format - just filename
                var fileName = line.Trim();
                if (string.IsNullOrEmpty(fileName) || fileName.Contains('\t')) return false;
                
                fileInfo = new FtpFileInfo
                {
                    Name = fileName,
                    FullPath = basePath.TrimEnd('/') + "/" + fileName,
                    IsDirectory = false, // Assume file unless we can determine otherwise
                    Type = "File",
                    Size = 0,
                    ModifiedDate = DateTime.Now
                };
                
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}