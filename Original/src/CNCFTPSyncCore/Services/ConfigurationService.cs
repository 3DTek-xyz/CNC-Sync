using CNCFTPSyncCore.Models;
using System.Text.Json;

namespace CNCFTPSyncCore.Services
{
    public interface IConfigurationService
    {
        SyncConfiguration LoadConfiguration();
        void SaveConfiguration(SyncConfiguration config);
        string ConfigurationFilePath { get; }
        SyncConfiguration GetDefaultConfiguration();
    }

    public class ConfigurationService : IConfigurationService
    {
        private readonly string _configDirectory;
        private readonly string _configFileName = "CNC-FTP-SYNC-Config.json";

        public string ConfigurationFilePath => Path.Combine(_configDirectory, _configFileName);

        public ConfigurationService()
        {
            // Store configuration in shared ProgramData folder accessible to both GUI and Service
            _configDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "CNC-FTP-SYNC"
            );

            // Ensure config directory exists
            if (!Directory.Exists(_configDirectory))
            {
                Directory.CreateDirectory(_configDirectory);
            }
        }

        public SyncConfiguration LoadConfiguration()
        {
            try
            {
                Console.WriteLine($"ConfigService.LoadConfiguration: Looking for config file at '{ConfigurationFilePath}'");
                Console.WriteLine($"ConfigService.LoadConfiguration: File exists = {File.Exists(ConfigurationFilePath)}");
                
                if (File.Exists(ConfigurationFilePath))
                {
                    var jsonString = File.ReadAllText(ConfigurationFilePath);
                    Console.WriteLine($"ConfigService.LoadConfiguration: File content length = {jsonString.Length}");
                    Console.WriteLine($"ConfigService.LoadConfiguration: FtpServer in JSON = {(jsonString.Contains("ftpServer") ? "FOUND" : "NOT FOUND")}");
                    
                    var options = new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    };
                    
                    var config = JsonSerializer.Deserialize<SyncConfiguration>(jsonString, options);
                    
                    if (config != null)
                    {
                        Console.WriteLine($"ConfigService.LoadConfiguration: Deserialized FtpServer = '{config.FtpServer}'");
                        
                        // Validate and set defaults for missing properties
                        ValidateConfiguration(config);
                        
                        Console.WriteLine($"ConfigService.LoadConfiguration: After validation FtpServer = '{config.FtpServer}'");
                        return config;
                    }
                }
                else
                {
                    // First run - create default configuration
                    Console.WriteLine($"ConfigService.LoadConfiguration: Config file doesn't exist, creating default configuration");
                    var defaultConfig = CreateDefaultConfiguration();
                    SaveConfiguration(defaultConfig);
                    Console.WriteLine($"ConfigService.LoadConfiguration: Default configuration saved to '{ConfigurationFilePath}'");
                    return defaultConfig;
                }
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                // If loading fails, create default configuration for first run
                Console.WriteLine($"Error loading configuration: {ex.Message}");
                Console.WriteLine($"Creating default configuration due to error");
                try
                {
                    var defaultConfig = CreateDefaultConfiguration();
                    SaveConfiguration(defaultConfig);
                    return defaultConfig;
                }
                catch (Exception saveEx)
                {
                    throw new InvalidOperationException($"Failed to create default configuration at '{ConfigurationFilePath}': {saveEx.Message}", saveEx);
                }
            }

            // If we get here, config was null after deserialization - create default
            Console.WriteLine("Config deserialization returned null, creating default configuration");
            var fallbackConfig = CreateDefaultConfiguration();
            SaveConfiguration(fallbackConfig);
            return fallbackConfig;
        }

        public void SaveConfiguration(SyncConfiguration config)
        {
            try
            {
                // Log before validation
                Console.WriteLine($"ConfigService.SaveConfiguration: Before validation - FtpServer = '{config.FtpServer}'");
                
                ValidateConfiguration(config);
                
                // Log after validation
                Console.WriteLine($"ConfigService.SaveConfiguration: After validation - FtpServer = '{config.FtpServer}'");
                Console.WriteLine($"ConfigService.SaveConfiguration: Saving to path = '{ConfigurationFilePath}'");
                
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var jsonString = JsonSerializer.Serialize(config, options);
                Console.WriteLine($"ConfigService.SaveConfiguration: JSON content preview - FtpServer portion: {(jsonString.Contains("ftpServer") ? "FOUND" : "NOT FOUND")}");
                
                File.WriteAllText(ConfigurationFilePath, jsonString);
                Console.WriteLine($"ConfigService.SaveConfiguration: File written successfully");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save configuration: {ex.Message}", ex);
            }
        }

        public SyncConfiguration GetDefaultConfiguration()
        {
            return new SyncConfiguration
            {
                WatchFolder = @"C:\CNC-FTP-SYNC\Watch",
                FtpUploadFolder = @"C:\CNC-FTP-SYNC\FtpUpload",
                FtpServer = "localhost",
                FtpPort = 21,
                UseAnonymousFtp = true,
                FtpUsername = "anonymous",
                FtpPassword = "anonymous@example.com",
                FileStabilityDelaySeconds = 30,
                FileStabilityCheckIntervalSeconds = 5,
                LogFilePath = Path.Combine(_configDirectory, "Logs", "CNCFTPSync.log"),
                EnableDetailedLogging = true,
                AutoUploadAfterProcessing = true
            };
        }

        private SyncConfiguration CreateDefaultConfiguration()
        {
            return GetDefaultConfiguration();
        }

        private void ValidateConfiguration(SyncConfiguration config)
        {
            // Set default values for missing or invalid properties
            if (string.IsNullOrEmpty(config.WatchFolder))
                config.WatchFolder = @"C:\CNC-FTP-SYNC\Watch";

            if (string.IsNullOrEmpty(config.FtpUploadFolder))
                config.FtpUploadFolder = @"C:\CNC-FTP-SYNC\FtpUpload";

            if (string.IsNullOrEmpty(config.FtpServer))
                config.FtpServer = "localhost";

            if (config.FtpPort <= 0 || config.FtpPort > 65535)
                config.FtpPort = 21;

            if (string.IsNullOrEmpty(config.FtpUsername))
                config.FtpUsername = "anonymous";

            if (string.IsNullOrEmpty(config.FtpPassword))
                config.FtpPassword = "anonymous@example.com";

            if (config.FileStabilityDelaySeconds <= 0)
                config.FileStabilityDelaySeconds = 30;

            if (config.FileStabilityCheckIntervalSeconds <= 0)
                config.FileStabilityCheckIntervalSeconds = 5;

            if (string.IsNullOrEmpty(config.LogFilePath))
                config.LogFilePath = Path.Combine(_configDirectory, "Logs", "GCodeSync.log");

            // Ensure log directory exists
            var logDirectory = Path.GetDirectoryName(config.LogFilePath);
            if (!string.IsNullOrEmpty(logDirectory) && !Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }
        }
    }
}