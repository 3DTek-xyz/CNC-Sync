using System.Xml;

namespace CNCFTPSyncCore.Models
{
    public class ProcessingResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<string> ProcessedFiles { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
        /// <summary>
        /// Output path for processed files, used for FTP upload when external processors are used
        /// </summary>
        public string OutputPath { get; set; } = string.Empty;
    }

    public class ProjectInfo
    {
        public string ProjectPath { get; set; } = string.Empty;
        public string FtpWorkingPath { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string LatestRevision { get; set; } = string.Empty;
        public List<FileInfo> CycFiles { get; set; } = new();
        public List<FileInfo> NcFiles { get; set; } = new();
        public List<FileInfo> XmlFiles { get; set; } = new();
        public List<FileInfo> JpgFiles { get; set; } = new();
    }

    public class SyncConfiguration
    {
        public string WatchFolder { get; set; } = string.Empty;
        public string FtpUploadFolder { get; set; } = string.Empty;
        public string FtpServer { get; set; } = string.Empty;
        public int FtpPort { get; set; } = 21;
        public bool UseAnonymousFtp { get; set; } = true;
        public string FtpUsername { get; set; } = "anonymous";
        public string FtpPassword { get; set; } = "anonymous@example.com";
        public int FileStabilityDelaySeconds { get; set; } = 30;
        public int FileStabilityCheckIntervalSeconds { get; set; } = 5;
        public string LogFilePath { get; set; } = string.Empty;
        public bool EnableDetailedLogging { get; set; } = true;
        public bool AutoUploadAfterProcessing { get; set; } = true;
        public bool UseExternalProcessor { get; set; } = false;
        public string ExternalProcessorPath { get; set; } = string.Empty;
    }

    public class FtpFileInfo
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public DateTime ModifiedDate { get; set; }
        public string Type { get; set; } = string.Empty;
    }
}