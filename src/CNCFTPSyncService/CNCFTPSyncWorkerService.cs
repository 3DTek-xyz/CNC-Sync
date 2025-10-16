using CNCFTPSyncCore.Models;
using CNCFTPSyncCore.Services;
using Microsoft.Extensions.Hosting;
using NLog;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO.Pipes;
using System.Text;

namespace CNCFTPSyncService
{
    public class CNCFTPSyncWorkerService : BackgroundService
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly IHostApplicationLifetime _hostApplicationLifetime;
        private ISyncOrchestrator? _orchestrator;
        private ILogService? _logService;
        private readonly CancellationTokenSource _pipeServerCancellation;
        private Task? _pipeServerTask;

        public CNCFTPSyncWorkerService(IHostApplicationLifetime hostApplicationLifetime)
        {
            _hostApplicationLifetime = hostApplicationLifetime;
            _pipeServerCancellation = new CancellationTokenSource();
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
                
                // Set up NLog configuration to use same shared directory as GUI
                var sharedDataDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "CNC-FTP-SYNC"
                );
                var logDirectory = Path.Combine(sharedDataDirectory, "Logs");
                Directory.CreateDirectory(logDirectory);
                
                if (LogManager.Configuration?.Variables != null)
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
                
                // Start the named pipe server for GUI communication
                _pipeServerTask = StartPipeServerAsync(_pipeServerCancellation.Token);
                
                // Launch GUI if not already running for current user
                LaunchGUIIfNotRunning();

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
                
                // Stop the pipe server
                _pipeServerCancellation.Cancel();
                if (_pipeServerTask != null)
                {
                    try
                    {
                        await _pipeServerTask;
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when cancelling
                    }
                }
                
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
            finally
            {
                _pipeServerCancellation.Dispose();
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

        private void LaunchGUIIfNotRunning()
        {
            try
            {
                // Check if GUI is already running
                var existingProcesses = Process.GetProcessesByName("CNCFTPSyncGUI");
                if (existingProcesses.Length > 0)
                {
                    _logService?.LogInfo("GUI is already running, skipping launch");
                    return;
                }

                // Get the service executable path and find GUI in same directory
                var servicePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(servicePath))
                {
                    _logService?.LogWarning("Could not determine service path for GUI launch");
                    return;
                }

                var serviceDirectory = Path.GetDirectoryName(servicePath);
                var guiPath = Path.Combine(serviceDirectory ?? "", "CNCFTPSyncGUI.exe");

                if (!File.Exists(guiPath))
                {
                    _logService?.LogWarning($"GUI executable not found at: {guiPath}");
                    return;
                }

                // Launch GUI for current user session
                var startInfo = new ProcessStartInfo
                {
                    FileName = guiPath,
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal
                };

                var process = Process.Start(startInfo);
                if (process != null)
                {
                    _logService?.LogInfo($"Successfully launched GUI: {guiPath}");
                }
                else
                {
                    _logService?.LogWarning("Failed to launch GUI process");
                }
            }
            catch (Exception ex)
            {
                _logService?.LogError("Error launching GUI from service", ex);
            }
        }

        private async Task StartPipeServerAsync(CancellationToken cancellationToken)
        {
            const string pipeName = "CNCFTPSync-Control";
            
            try
            {
                _logService?.LogInfo("Starting named pipe server for GUI communication");
                
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        using (var pipeServer = new NamedPipeServerStream(pipeName, PipeDirection.Out, 10, PipeTransmissionMode.Message))
                        {
                            _logService?.LogDebug("Waiting for GUI connection on pipe: " + pipeName);
                            
                            // Wait for a client to connect
                            await pipeServer.WaitForConnectionAsync(cancellationToken);
                            
                            _logService?.LogInfo("GUI connected to pipe server");
                            
                            // Send STOP_STANDALONE command to GUI
                            var command = "STOP_STANDALONE";
                            var commandBytes = Encoding.UTF8.GetBytes(command);
                            
                            await pipeServer.WriteAsync(commandBytes, 0, commandBytes.Length, cancellationToken);
                            await pipeServer.FlushAsync(cancellationToken);
                            
                            _logService?.LogInfo($"Sent command to GUI: {command}");
                            
                            // Keep connection open briefly to ensure message is received
                            await Task.Delay(1000, cancellationToken);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logService?.LogError("Error in pipe server connection", ex);
                        
                        // Wait before retrying to avoid rapid failures
                        try
                        {
                            await Task.Delay(5000, cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logService?.LogInfo("Named pipe server cancelled");
            }
            catch (Exception ex)
            {
                _logService?.LogError("Fatal error in named pipe server", ex);
            }
        }
    }
}