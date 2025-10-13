using CNCFTPSyncCore.Models;
using CNCFTPSyncCore.Services;
using Microsoft.Extensions.Hosting;
using NLog;

namespace GCodeSyncService
{
    public class GCodeSyncWorkerService : BackgroundService
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly IHostApplicationLifetime _hostApplicationLifetime;
        private ISyncOrchestrator? _orchestrator;
        private ILogService? _logService;

        public GCodeSyncWorkerService(IHostApplicationLifetime hostApplicationLifetime)
        {
            _hostApplicationLifetime = hostApplicationLifetime;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                Logger.Info("G-Code Sync Service is starting");

                // Initialize services
                var configService = new ConfigurationService();
                _logService = new LogService();
                
                SyncConfiguration config;
                try
                {
                    config = configService.LoadConfiguration();
                }
                catch (InvalidOperationException ex)
                {
                    Logger.Error(ex, "Service startup failed - configuration required");
                    throw new InvalidOperationException("G-Code Sync Service requires valid configuration. Please run the GUI application first to set up configuration.", ex);
                }
                
                // Set up NLog configuration
                var logDirectory = Path.GetDirectoryName(config.LogFilePath);
                if (!string.IsNullOrEmpty(logDirectory))
                {
                    LogManager.Configuration.Variables["logDirectory"] = logDirectory;
                }

                var gCodeProcessor = new GCodeProcessorService(_logService, config);
                var ftpService = new FtpService(_logService, config);
                
                _orchestrator = new SyncOrchestrator(configService, _logService, gCodeProcessor, ftpService);

                // Subscribe to events
                _orchestrator.ProcessingStarted += OnProcessingStarted;
                _orchestrator.ProcessingCompleted += OnProcessingCompleted;
                _orchestrator.StatusChanged += OnStatusChanged;

                // Start the orchestrator
                await _orchestrator.StartAsync();

                _logService.LogInfo("G-Code Sync Service started successfully");

                // Wait for cancellation
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(5000, stoppingToken);
                    
                    // Periodic health check
                    if (!_orchestrator.IsRunning)
                    {
                        _logService.LogWarning("Orchestrator stopped unexpectedly, attempting to restart...");
                        try
                        {
                            await _orchestrator.StartAsync();
                        }
                        catch (Exception ex)
                        {
                            _logService.LogError("Failed to restart orchestrator", ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Fatal error in G-Code Sync Service");
                _hostApplicationLifetime.StopApplication();
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                Logger.Info("G-Code Sync Service is stopping");
                
                if (_orchestrator != null)
                {
                    await _orchestrator.StopAsync();
                    _orchestrator.Dispose();
                }

                _logService?.LogInfo("G-Code Sync Service stopped");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error stopping G-Code Sync Service");
            }

            await base.StopAsync(cancellationToken);
        }

        private void OnProcessingStarted(string folderPath)
        {
            _logService?.LogInfo($"Service: Processing started for {Path.GetFileName(folderPath)}");
        }

        private void OnProcessingCompleted(string folderPath, ProcessingResult result)
        {
            var status = result.Success ? "completed successfully" : "failed";
            _logService?.LogInfo($"Service: Processing {status} for {Path.GetFileName(folderPath)} in {result.Duration.TotalSeconds:F1} seconds");
        }

        private void OnStatusChanged(string status)
        {
            Logger.Debug($"Service status: {status}");
        }
    }
}