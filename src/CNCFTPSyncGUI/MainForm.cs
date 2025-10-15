using CNCFTPSyncCore.Models;
using CNCFTPSyncCore.Services;
using System.ServiceProcess;
using System.Diagnostics;
using AutoUpdaterDotNET;


namespace CNCFTPSyncGUI
{
    public partial class MainForm : Form
    {
        private readonly IConfigurationService _configService = null!;
        private readonly ILogService _logService = null!;
        private ISyncOrchestrator? _orchestrator;
        private NotifyIcon? _notifyIcon;
        private SyncConfiguration _config;
        private bool _allowVisible = false;
        private bool _showingExplicitly = false;
        private bool _isExiting = false;
        
        // FTP Browser fields
        private string _currentLocalPath = "";
        private string _currentRemotePath = "/";
        private FtpService? _ftpService;

              private void NavigateLocalTo(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    browserLocal?.NavigateTo(path);
                    _currentLocalPath = path;
                }
                WriteToLogFile($"Navigated local to: {path}");
            }
            catch (Exception ex)
            {
                WriteToLogFile($"Error navigating local to {path}: {ex.Message}");
                MessageBox.Show($"Error navigating to: {path}\n{ex.Message}", "Navigation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public MainForm()
        {
            WriteToLogFile("MainForm constructor started");
            
            try
            {
                WriteToLogFile("MainForm: InitializeComponent starting");
                InitializeComponent();
                WriteToLogFile("MainForm: InitializeComponent completed");
                
                WriteToLogFile("MainForm: Creating ConfigurationService");
                _configService = new ConfigurationService();
                WriteToLogFile("MainForm: ConfigurationService created");
                
                WriteToLogFile("MainForm: Creating LogService");
                _logService = new LogService();
                WriteToLogFile("MainForm: LogService created");
                
                WriteToLogFile("MainForm: Loading configuration");
                _config = _configService.LoadConfiguration();
                WriteToLogFile("MainForm: Configuration loaded");
            
                // Load the application icon using our dedicated method
                WriteToLogFile("MainForm: Loading application icon");
                try
                {
                    this.Icon = LoadIconFromFile("CNCFTPSync.ico");
                    _logService?.LogInfo("Application icon loaded successfully in constructor");
                    WriteToLogFile("MainForm: Application icon loaded successfully");
                }
                catch (Exception ex)
                {
                    _logService?.LogError($"Failed to load application icon in constructor: {ex.Message}");
                    WriteToLogFile($"MainForm: Failed to load application icon: {ex.Message}");
                }
                
                WriteToLogFile("MainForm: Initializing NotifyIcon");
                InitializeNotifyIcon();
                WriteToLogFile("MainForm: NotifyIcon initialized");
                
                WriteToLogFile("MainForm: Loading configuration");
                LoadConfiguration();
                WriteToLogFile("MainForm: Configuration loaded");
                
                WriteToLogFile("MainForm: Setting up event handlers");
                SetupEventHandlers();
                WriteToLogFile("MainForm: Event handlers setup complete");
                
                WriteToLogFile("MainForm: Initializing AutoUpdater");
                InitializeAutoUpdater();
                WriteToLogFile("MainForm: AutoUpdater initialized");
                
                WriteToLogFile("MainForm: Starting auto-detect task");
                // Auto-detect service status and start standalone if needed
                _ = Task.Run(AutoDetectAndStartMonitoring);
                WriteToLogFile("MainForm: Auto-detect task started");
                
                WriteToLogFile("MainForm: Setting window state");
                // Start minimized to system tray
                this.WindowState = FormWindowState.Minimized;
                this.ShowInTaskbar = false;
                WriteToLogFile("MainForm: Constructor completed successfully");
            }
            catch (Exception ex)
            {
                WriteToLogFile($"MainForm constructor FAILED: {ex.Message}");
                WriteToLogFile($"MainForm constructor STACK TRACE: {ex.StackTrace}");
                throw;
            }
        }

        private void InitializeNotifyIcon()
        {
            _notifyIcon = new NotifyIcon()
            {
                Text = "CNC-FTP-SYNC Tool",
                Visible = true,
                Icon = LoadIconFromFile("CNCFTPSync.ico")
            };

            // Create context menu for system tray
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Show", null, ShowForm_Click);
            contextMenu.Items.Add("Configuration", null, Configuration_Click);
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Start Service", null, StartService_Click);
            contextMenu.Items.Add("Stop Service", null, StopService_Click);
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Install Service", null, InstallService_Click);
            contextMenu.Items.Add("Uninstall Service", null, UninstallService_Click);
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Exit", null, Exit_Click);

            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.DoubleClick += ShowForm_Click;

            UpdateNotifyIconStatus("Stopped");
        }

        private void InitializeAutoUpdater()
        {
            try
            {
                _logService.LogInfo("=== AutoUpdater Initialization Starting ===");
                
                // Get current application version for comparison
                var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                var currentVersionString = currentVersion?.ToString() ?? "Unknown";
                _logService.LogInfo($"Current application version: {currentVersionString}");
                
                // Use custom update checker only (AutoUpdater.NET has .NET 9.0 compatibility issues)
                _logService.LogInfo("Using custom update checker - v1.0.17 test version");
                UseCustomUpdateChecker();
                
                _logService.LogInfo("=== AutoUpdater Initialization Complete ===");
            }
            catch (Exception ex)
            {
                _logService.LogError($"Failed to initialize update system: {ex.Message}");
                _logService.LogError($"Update system initialization exception: {ex}");
            }
        }
        
        private void UseCustomUpdateChecker()
        {
            var updateUrl = "https://3dtek-xyz.github.io/CNC-FTPSync/update.xml";
            _logService.LogInfo($"=== Custom Update Checker Configuration ===");
            _logService.LogInfo($"Update XML URL: {updateUrl}");
            _logService.LogInfo($"Expected GitHub Pages structure: /installer/[filename].msi");
            
            // Use custom update checker
            _ = Task.Run(async () =>
            {
                try
                {
                    _logService.LogInfo($"Starting custom update check for URL: {updateUrl}");
                    await CheckForUpdatesAsync(updateUrl, false); // Silent background check
                    _logService.LogInfo("Custom update check completed successfully");
                }
                catch (Exception ex)
                {
                    _logService.LogError($"Custom update check failed: {ex.Message}");
                }
            });
            
            _logService.LogInfo("=== Custom Update Checker Initialization Complete ===");
        }

        private async void AutoUpdater_ParseUpdateInfoEvent(ParseUpdateInfoEventArgs args)
        {
            try
            {
                _logService.LogInfo("=== AutoUpdater XML Parse Event ===");
                _logService.LogInfo($"📄 RemoteData length: {args.RemoteData?.Length ?? 0} bytes");
                
                if (!string.IsNullOrEmpty(args.RemoteData))
                {
                    _logService.LogInfo("✅ Update XML downloaded successfully");
                    _logService.LogInfo($"📄 Raw XML Content:\n{args.RemoteData}");
                    
                    // Try to manually parse the XML to identify any issues
                    try
                    {
                        var xmlDoc = new System.Xml.XmlDocument();
                        xmlDoc.LoadXml(args.RemoteData);
                        
                        var versionNode = xmlDoc.SelectSingleNode("//version");
                        var urlNode = xmlDoc.SelectSingleNode("//url");
                        var changelogNode = xmlDoc.SelectSingleNode("//changelog");
                        var mandatoryNode = xmlDoc.SelectSingleNode("//mandatory");
                        var argsNode = xmlDoc.SelectSingleNode("//args");
                        var checksumNode = xmlDoc.SelectSingleNode("//checksum");
                        var algorithmAttr = checksumNode?.Attributes?["algorithm"];
                        
                        _logService.LogInfo("=== XML Content Analysis ===");
                        _logService.LogInfo($"📦 Version: {versionNode?.InnerText ?? "❌ MISSING"}");
                        _logService.LogInfo($"🔗 Download URL: {urlNode?.InnerText ?? "❌ MISSING"}");
                        _logService.LogInfo($"📝 Changelog URL: {changelogNode?.InnerText ?? "❌ MISSING"}");
                        _logService.LogInfo($"⚠️  Mandatory: {mandatoryNode?.InnerText ?? "❌ MISSING"}");
                        _logService.LogInfo($"⚙️  Install Args: {argsNode?.InnerText ?? "❌ MISSING"}");
                        _logService.LogInfo($"🔐 Checksum: {checksumNode?.InnerText ?? "❌ MISSING"}");
                        _logService.LogInfo($"🔒 Hash Algorithm: {algorithmAttr?.Value ?? "❌ MISSING"}");
                        
                        // Validate download URL accessibility
                        if (urlNode != null && !string.IsNullOrEmpty(urlNode.InnerText))
                        {
                            var downloadUrl = urlNode.InnerText;
                            _logService.LogInfo($"🔍 Validating download URL: {downloadUrl}");
                            
                            // Test URL accessibility using modern HttpClient
                            try
                            {
                                using var httpClient = new System.Net.Http.HttpClient();
                                httpClient.Timeout = TimeSpan.FromSeconds(10);
                                
                                var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, downloadUrl);
                                using var response = await httpClient.SendAsync(request);
                                
                                _logService.LogInfo($"✅ Download URL accessible - Status: {response.StatusCode}");
                                if (response.Content.Headers.ContentLength.HasValue)
                                {
                                    _logService.LogInfo($"� Content Length: {response.Content.Headers.ContentLength} bytes");
                                }
                                if (response.Content.Headers.LastModified.HasValue)
                                {
                                    _logService.LogInfo($"📅 Last Modified: {response.Content.Headers.LastModified}");
                                }
                            }
                            catch (System.Net.Http.HttpRequestException httpEx)
                            {
                                _logService.LogError($"❌ Download URL NOT accessible: {httpEx.Message}");
                            }
                            catch (Exception urlEx)
                            {
                                _logService.LogError($"❌ Error testing download URL: {urlEx.Message}");
                            }
                        }
                        
                        // Validate XML structure
                        if (versionNode != null && urlNode != null)
                        {
                            _logService.LogInfo("✅ XML contains minimum required fields (version + url)");
                        }
                        else
                        {
                            _logService.LogError("❌ XML missing required fields - AutoUpdater may fail");
                        }
                    }
                    catch (Exception xmlEx)
                    {
                        _logService.LogError($"❌ XML parsing failed: {xmlEx.Message}");
                        _logService.LogError($"XML exception details: {xmlEx}");
                    }
                }
                else
                {
                    _logService.LogWarning("❌ AutoUpdater received empty or null update data");
                }
                
                _logService.LogInfo("=== XML Parse Event Complete ===");
            }
            catch (Exception ex)
            {
                _logService.LogError($"❌ Fatal error in AutoUpdater_ParseUpdateInfoEvent: {ex.Message}");
                _logService.LogError($"Parse exception details: {ex}");
            }
        }

        private void AutoUpdater_ApplicationExitEvent()
        {
            // Handle application exit for updates
            _logService.LogInfo("AutoUpdater requesting application exit for update");
            
            // Stop the service if it's running
            try
            {
                if (IsServiceInstalled("CNCFTPSyncService"))
                {
                    var currentStatus = GetServiceStatus("CNCFTPSyncService");
                    if (currentStatus == ServiceControllerStatus.Running)
                    {
                        _logService.LogInfo("Stopping service for update");
                        using var service = new ServiceController("CNCFTPSyncService");
                        service.Stop();
                        
                        // Wait for service to stop
                        VerifyServiceStatus("CNCFTPSyncService", ServiceControllerStatus.Stopped, 30);
                    }
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"Error stopping service for update: {ex.Message}");
            }
            
            // Exit the application
            Application.Exit();
        }

        private void AutoUpdater_CheckForUpdateEvent(UpdateInfoEventArgs args)
        {
            try
            {
                _logService.LogInfo("=== AutoUpdater Check Results ===");
                
                if (args.Error == null)
                {
                    _logService.LogInfo("✅ Update XML downloaded successfully");
                    
                    // Log version comparison details
                    _logService.LogInfo($"📋 Version Comparison:");
                    _logService.LogInfo($"   • Current (Installed): {args.InstalledVersion}");
                    _logService.LogInfo($"   • Available (XML): {args.CurrentVersion}");
                    _logService.LogInfo($"   • Update Available: {args.IsUpdateAvailable}");
                    
                    // Log additional update info
                    if (!string.IsNullOrEmpty(args.DownloadURL))
                    {
                        _logService.LogInfo($"🔗 Download URL: {args.DownloadURL}");
                    }
                    if (!string.IsNullOrEmpty(args.ChangelogURL))
                    {
                        _logService.LogInfo($"📝 Changelog URL: {args.ChangelogURL}");
                    }
                    // Note: Checksum and HashingAlgorithm properties not available in this version of AutoUpdater.NET
                    _logService.LogInfo($"🔐 Checksum: Not available in current AutoUpdater.NET version");
                    _logService.LogInfo($"🔒 Hash Algorithm: Not available in current AutoUpdater.NET version");
                    
                    if (args.IsUpdateAvailable)
                    {
                        _logService.LogInfo($"🎯 Update Available: Version {args.InstalledVersion} -> {args.CurrentVersion}");
                        
                        var result = MessageBox.Show(
                            $"A new version ({args.CurrentVersion}) is available!\n\n" +
                            $"Current Version: {args.InstalledVersion}\n" +
                            $"New Version: {args.CurrentVersion}\n\n" +
                            $"Release Notes:\n{args.ChangelogURL}\n\n" +
                            $"Would you like to download and install the update now?",
                            "CNC-FTP-SYNC Update Available",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Information);

                        if (result == DialogResult.Yes)
                        {
                            try
                            {
                                _logService.LogInfo($"🔄 Starting download from: {args.DownloadURL}");
                                if (AutoUpdater.DownloadUpdate(args))
                                {
                                    _logService.LogInfo("✅ Update downloaded successfully, exiting application");
                                    Application.Exit();
                                }
                                else
                                {
                                    _logService.LogWarning("❌ Update download returned false - download may have failed");
                                }
                            }
                            catch (Exception ex)
                            {
                                _logService.LogError($"❌ Error downloading update: {ex.Message}");
                                _logService.LogError($"Download exception details: {ex}");
                                MessageBox.Show($"Failed to download update: {ex.Message}", "Update Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                        else
                        {
                            _logService.LogInfo("❌ User declined update installation");
                        }
                    }
                    else
                    {
                        _logService.LogInfo("✅ No updates available - application is up to date");
                    }
                }
                else
                {
                    _logService.LogError("=== AutoUpdater Error Details ===");
                    _logService.LogError($"❌ Update check failed: {args.Error.Message}");
                    _logService.LogError($"🔍 Error Type: {args.Error.GetType().Name}");
                    _logService.LogError($"📄 Full Error Details: {args.Error}");
                    
                    if (args.Error.InnerException != null)
                    {
                        _logService.LogError($"🔗 Inner Exception: {args.Error.InnerException.Message}");
                        _logService.LogError($"📄 Inner Exception Details: {args.Error.InnerException}");
                    }
                    
                    // Try to extract more specific error information
                    if (args.Error is System.Net.WebException webEx)
                    {
                        _logService.LogError($"🌐 WebException Status: {webEx.Status}");
                        if (webEx.Response is System.Net.HttpWebResponse httpResponse)
                        {
                            _logService.LogError($"🌐 HTTP Status Code: {httpResponse.StatusCode}");
                            _logService.LogError($"🌐 HTTP Status Description: {httpResponse.StatusDescription}");
                        }
                    }
                }
                
                _logService.LogInfo("=== AutoUpdater Check Complete ===");
            }
            catch (Exception ex)
            {
                _logService.LogError($"❌ Fatal error during update check: {ex.Message}");
                _logService.LogError($"Fatal exception details: {ex}");
            }
        }

        private void UpdateNotifyIconStatus(string status)
        {
            if (_notifyIcon == null) return;

            // Update icon based on status
            var iconText = $"CNC-FTP-SYNC - {status}";
            _notifyIcon.Text = iconText.Length > 63 ? iconText.Substring(0, 63) : iconText;

            // You would normally set different icons here based on status
            // For now, we'll just update the text and balloon tip
            var color = status.ToLower() switch
            {
                var s when s.Contains("running") || s.Contains("monitoring") => "Green",
                var s when s.Contains("processing") => "Yellow",
                var s when s.Contains("error") || s.Contains("failed") => "Red",
                _ => "Gray"
            };

            lblStatus.Text = $"Status: {status}";
            lblStatus.ForeColor = Color.FromName(color);
        }

        private async Task AutoDetectAndStartMonitoring()
        {
            var startupStopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                WriteToLogFile("AutoDetectAndStartMonitoring: Method started");
                _logService.LogInfo($"STARTUP TIMING: AutoDetectAndStartMonitoring started at {DateTime.Now:HH:mm:ss.fff}");
                
                // Wait a bit for the UI to fully initialize
                WriteToLogFile("AutoDetectAndStartMonitoring: Starting UI delay");
                _logService.LogInfo($"STARTUP TIMING: Starting 2-second UI delay at {startupStopwatch.ElapsedMilliseconds}ms");
                await Task.Delay(2000);
                WriteToLogFile("AutoDetectAndStartMonitoring: UI delay completed");
                _logService.LogInfo($"STARTUP TIMING: UI delay completed at {startupStopwatch.ElapsedMilliseconds}ms");
                
                // Check if Windows Service is running
                WriteToLogFile("AutoDetectAndStartMonitoring: About to check service status");
                _logService.LogInfo($"STARTUP TIMING: Starting service status check at {startupStopwatch.ElapsedMilliseconds}ms");
                bool serviceRunning = false;
                bool serviceInstalled = false;
                try
                {
                    WriteToLogFile("AutoDetectAndStartMonitoring: Calling IsServiceInstalled");
                    serviceInstalled = IsServiceInstalled("CNCFTPSyncService");
                    WriteToLogFile($"AutoDetectAndStartMonitoring: IsServiceInstalled returned: {serviceInstalled}");
                    
                    if (serviceInstalled)
                    {
                        WriteToLogFile("AutoDetectAndStartMonitoring: Service installed, getting status");
                        var currentStatus = GetServiceStatus("CNCFTPSyncService");
                        WriteToLogFile($"AutoDetectAndStartMonitoring: GetServiceStatus returned: {currentStatus}");
                        serviceRunning = currentStatus == ServiceControllerStatus.Running;
                        _logService.LogInfo($"STARTUP TIMING: Service status check completed at {startupStopwatch.ElapsedMilliseconds}ms - Installed: {serviceInstalled}, Running: {serviceRunning}, Status: {currentStatus}");
                    }
                    else
                    {
                        WriteToLogFile("AutoDetectAndStartMonitoring: Service not installed");
                        _logService.LogInfo($"STARTUP TIMING: Service status check completed at {startupStopwatch.ElapsedMilliseconds}ms - Service not installed");
                    }
                }
                catch (Exception serviceEx)
                {
                    // Service not installed or accessible
                    serviceRunning = false;
                    serviceInstalled = false;
                    WriteToLogFile($"AutoDetectAndStartMonitoring: Service check FAILED: {serviceEx.Message}");
                    WriteToLogFile($"AutoDetectAndStartMonitoring: Service check STACK TRACE: {serviceEx.StackTrace}");
                    _logService.LogInfo($"STARTUP TIMING: Service status check failed at {startupStopwatch.ElapsedMilliseconds}ms - Service not installed or accessible: {serviceEx.Message}");
                }

                if (!serviceRunning)
                {
                    _logService.LogInfo($"STARTUP TIMING: Windows Service not running - starting standalone mode at {startupStopwatch.ElapsedMilliseconds}ms");
                    
                    // Start standalone mode on UI thread
                    if (InvokeRequired)
                    {
                        Invoke(new Action(() => StartStandaloneMode()));
                    }
                    else
                    {
                        StartStandaloneMode();
                    }
                    _logService.LogInfo($"STARTUP TIMING: Standalone mode start initiated at {startupStopwatch.ElapsedMilliseconds}ms");
                }
                else
                {
                    _logService.LogInfo($"STARTUP TIMING: Windows Service is running - monitoring active via service at {startupStopwatch.ElapsedMilliseconds}ms");
                    UpdateNotifyIconStatus("Running via Service");
                }
                
                _logService.LogInfo($"STARTUP TIMING: AutoDetectAndStartMonitoring completed at {startupStopwatch.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                _logService.LogError($"STARTUP TIMING: Error during auto-detection at {startupStopwatch.ElapsedMilliseconds}ms", ex);
            }
        }

        private void LoadConfiguration()
        {
            try
            {
                WriteToLogFile($"LoadConfiguration: Starting load process");
                WriteToLogFile($"LoadConfiguration: Config file path = {_configService.ConfigurationFilePath}");
                WriteToLogFile($"LoadConfiguration: Loaded FtpServer = '{_config.FtpServer}'");
                WriteToLogFile($"LoadConfiguration: Loaded FtpPort = {_config.FtpPort}");
                WriteToLogFile($"LoadConfiguration: Loaded WatchFolder = '{_config.WatchFolder}'");
                
                txtWatchFolder.Text = _config.WatchFolder;
                txtFtpUploadFolder.Text = _config.FtpUploadFolder;
                txtFtpServer.Text = _config.FtpServer;
                numFtpPort.Value = _config.FtpPort;
                chkAnonymousFtp.Checked = _config.UseAnonymousFtp;
                txtFtpUsername.Text = _config.FtpUsername;
                txtFtpPassword.Text = _config.FtpPassword;
                numStabilityDelay.Value = _config.FileStabilityDelaySeconds;
                chkAutoUpload.Checked = _config.AutoUploadAfterProcessing;
                chkUseExternalProcessor.Checked = _config.UseExternalProcessor;
                txtExternalProcessorPath.Text = _config.ExternalProcessorPath;

                WriteToLogFile($"LoadConfiguration: Set txtFtpServer.Text to '{txtFtpServer.Text}'");
                UpdateFtpCredentialsVisibility();
                
                // Initialize FTP browser
                InitializeFtpBrowser();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading configuration: {ex.Message}", "Configuration Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SaveConfiguration()
        {
            try
            {
                WriteToLogFile($"SaveConfiguration: Starting save process");
                WriteToLogFile($"SaveConfiguration: Config file path = {_configService.ConfigurationFilePath}");
                
                _config.WatchFolder = txtWatchFolder.Text;
                _config.FtpUploadFolder = txtFtpUploadFolder.Text;
                _config.FtpServer = txtFtpServer.Text;
                _config.FtpPort = (int)numFtpPort.Value;
                _config.UseAnonymousFtp = chkAnonymousFtp.Checked;
                _config.FtpUsername = txtFtpUsername.Text;
                _config.FtpPassword = txtFtpPassword.Text;
                _config.FileStabilityDelaySeconds = (int)numStabilityDelay.Value;
                _config.AutoUploadAfterProcessing = chkAutoUpload.Checked;
                _config.UseExternalProcessor = chkUseExternalProcessor.Checked;
                _config.ExternalProcessorPath = txtExternalProcessorPath.Text;

                WriteToLogFile($"SaveConfiguration: About to save FtpServer = '{_config.FtpServer}'");
                WriteToLogFile($"SaveConfiguration: About to save FtpPort = {_config.FtpPort}");
                WriteToLogFile($"SaveConfiguration: About to save WatchFolder = '{_config.WatchFolder}'");

                _configService.SaveConfiguration(_config);
                
                WriteToLogFile($"SaveConfiguration: Successfully saved configuration");
                MessageBox.Show("Configuration saved successfully.", "Configuration", 
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving configuration: {ex.Message}", "Configuration Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetupEventHandlers()
        {
            _logService.LogEntryAdded += OnLogEntryAdded;
            
            // FTP Browser event handlers
            btnRefreshLocal.Click += (s, e) => RefreshLocalExplorer();
            btnRefreshRemote.Click += (s, e) => RefreshRemoteExplorer();
            btnOpenLocalExternal.Click += (s, e) => OpenLocalExternal();
            btnOpenRemoteExternal.Click += (s, e) => OpenRemoteExternal();
            
            // Set up 50/50 split when container is resized
            splitFtp.Resize += (s, e) => 
            {
                if (splitFtp.Width > 0 && Math.Abs(splitFtp.SplitterDistance - splitFtp.Width / 2) > 10)
                {
                    splitFtp.SplitterDistance = splitFtp.Width / 2;
                }
                
                // Resize browser containers to fill available space
                ResizeBrowserContainers();
            };
        }

        private void OnLogEntryAdded(string level, string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => OnLogEntryAdded(level, message)));
                return;
            }

            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"[{timestamp}] {level}: {message}";
            
            // Write to debug.log file so it can be accessed by tools
            WriteToLogFile(logEntry);
            
            // Insert new entry at the top of main log
            txtLogs.Text = logEntry + Environment.NewLine + txtLogs.Text;
            
            // Keep only reasonable amount of text (approximately 1000 lines)
            var lines = txtLogs.Lines;
            if (lines.Length > 1000)
            {
                var keepLines = lines.Take(1000);
                txtLogs.Text = string.Join(Environment.NewLine, keepLines);
            }

            // Auto-scroll to top for new entries
            txtLogs.SelectionStart = 0;
            txtLogs.SelectionLength = 0;
            txtLogs.ScrollToCaret();
            
            // Update bottom log panel (last 20 lines)
            UpdateBottomLogPanel(logEntry);
        }

        private async void StartStandaloneMode()
        {
            var standaloneStopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                _logService.LogInfo($"STARTUP TIMING: StartStandaloneMode started at {DateTime.Now:HH:mm:ss.fff}");
                
                if (_orchestrator?.IsRunning == true)
                {
                    MessageBox.Show("Standalone mode is already running.", "Already Running", 
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                btnStartStandalone.Enabled = false;
                btnStopStandalone.Enabled = true;

                // Initialize orchestrator
                _logService.LogInfo($"STARTUP TIMING: Creating GCodeProcessorService at {standaloneStopwatch.ElapsedMilliseconds}ms");
                var gCodeProcessor = new GCodeProcessorService(_logService, _config);
                _logService.LogInfo($"STARTUP TIMING: GCodeProcessorService created at {standaloneStopwatch.ElapsedMilliseconds}ms");
                
                _logService.LogInfo($"STARTUP TIMING: Creating FtpService at {standaloneStopwatch.ElapsedMilliseconds}ms");
                var ftpService = new FtpService(_logService, _config);
                _logService.LogInfo($"STARTUP TIMING: FtpService created at {standaloneStopwatch.ElapsedMilliseconds}ms");
                
                _logService.LogInfo($"STARTUP TIMING: Creating SyncOrchestrator at {standaloneStopwatch.ElapsedMilliseconds}ms");
                _orchestrator = new SyncOrchestrator(_configService, _logService, gCodeProcessor, ftpService);
                _logService.LogInfo($"STARTUP TIMING: SyncOrchestrator created at {standaloneStopwatch.ElapsedMilliseconds}ms");

                _orchestrator.StatusChanged += (status) => UpdateNotifyIconStatus(status);
                
                _logService.LogInfo($"STARTUP TIMING: Starting orchestrator at {standaloneStopwatch.ElapsedMilliseconds}ms");
                await _orchestrator.StartAsync();
                _logService.LogInfo($"STARTUP TIMING: Orchestrator started at {standaloneStopwatch.ElapsedMilliseconds}ms");
                
                _logService.LogInfo($"STARTUP TIMING: Standalone mode completed at {standaloneStopwatch.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start standalone mode: {ex.Message}", "Start Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnStartStandalone.Enabled = true;
                btnStopStandalone.Enabled = false;
            }
        }

        private async void StopStandaloneMode()
        {
            try
            {
                if (_orchestrator != null)
                {
                    await _orchestrator.StopAsync();
                    _orchestrator.Dispose();
                    _orchestrator = null;
                }

                btnStartStandalone.Enabled = true;
                btnStopStandalone.Enabled = false;
                UpdateNotifyIconStatus("Stopped");
                
                _logService.LogInfo("Standalone mode stopped from GUI");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error stopping standalone mode: {ex.Message}", "Stop Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateFtpCredentialsVisibility()
        {
            bool showCredentials = !chkAnonymousFtp.Checked;
            txtFtpUsername.Enabled = showCredentials;
            txtFtpPassword.Enabled = showCredentials;
            lblFtpUsername.Enabled = showCredentials;
            lblFtpPassword.Enabled = showCredentials;
        }

        // Event Handlers
        private void ShowForm_Click(object? sender, EventArgs e)
        {
            try
            {
                WriteToLogFile("ShowForm_Click: Starting to show form");
                WriteToLogFile($"ShowForm_Click: Initial state - Location={this.Location}, Size={this.Size}, Bounds={this.Bounds}");
                
                _allowVisible = true; // Allow form to be visible
                _showingExplicitly = true; // We're explicitly showing the form
                
                // Set window state FIRST before making visible
                this.WindowState = FormWindowState.Normal;
                WriteToLogFile($"ShowForm_Click: Set WindowState to Normal");
                
                // Force proper size and position
                this.Size = new Size(800, 600);
                this.StartPosition = FormStartPosition.CenterScreen;
                this.Location = new Point((Screen.PrimaryScreen?.WorkingArea.Width ?? 1920 - this.Width) / 2, 
                                        (Screen.PrimaryScreen?.WorkingArea.Height ?? 1080 - this.Height) / 2);
                WriteToLogFile($"ShowForm_Click: Set size and position - Size={this.Size}, Location={this.Location}");
                
                this.ShowInTaskbar = true;
                this.Visible = true;
                this.Show();
                this.BringToFront();
                this.Activate();
                this.Focus();
                this.TopMost = true;  // Force to top
                this.TopMost = false; // Then reset
                
                WriteToLogFile($"ShowForm_Click: Final state - Visible={this.Visible}, WindowState={this.WindowState}");
                WriteToLogFile($"ShowForm_Click: Final position - Location={this.Location}, Size={this.Size}, Bounds={this.Bounds}");
                
                _showingExplicitly = false; // Reset flag
            }
            catch (Exception ex)
            {
                _showingExplicitly = false; // Reset flag on error too
                WriteToLogFile($"ShowForm_Click: Error = {ex.Message}");
                MessageBox.Show($"Error showing form: {ex.Message}");
            }
        }

        private void Configuration_Click(object? sender, EventArgs e)
        {
            ShowForm_Click(sender, e);
            tabControl.SelectedTab = tabConfiguration;
        }

        private void StartService_Click(object? sender, EventArgs e)
        {
            try
            {
                _logService.LogInfo("Attempting to start Windows Service...");
                
                if (!IsServiceInstalled("CNCFTPSyncService"))
                {
                    _logService.LogError("Service start failed - service not installed");
                    MessageBox.Show("Service is not installed. Please install the service first using 'Install Service' option.", 
                        "Service Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var currentStatus = GetServiceStatus("CNCFTPSyncService");
                _logService.LogInfo($"Current service status: {currentStatus}");
                
                if (currentStatus != ServiceControllerStatus.Running)
                {
                    if (currentStatus == ServiceControllerStatus.Stopped)
                    {
                        _logService.LogInfo("Starting service...");
                        
                        using var service = new ServiceController("CNCFTPSyncService");
                        service.Start();
                        
                        // Wait for service to start
                        service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                        
                        _logService.LogInfo("Windows Service started successfully");
                        MessageBox.Show("Service started successfully.", "Service Control", 
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        var statusMsg = $"Service cannot be started. Current status: {currentStatus}";
                        _logService.LogWarning(statusMsg);
                        MessageBox.Show(statusMsg, "Service Status", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                else
                {
                    _logService.LogInfo("Service is already running");
                    MessageBox.Show("Service is already running.", "Service Status", 
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"Service start failed: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Failed to start Windows Service: {ex.Message}\n\nMake sure you have administrative privileges and the service is properly installed.", 
                    "Service Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StopService_Click(object? sender, EventArgs e)
        {
            try
            {
                _logService.LogInfo("Attempting to stop Windows Service...");
                
                var currentStatus = GetServiceStatus("CNCFTPSyncService");
                _logService.LogInfo($"Current service status: {currentStatus}");
                
                if (currentStatus == ServiceControllerStatus.Running)
                {
                    _logService.LogInfo("Stopping service...");
                    
                    using var service = new ServiceController("CNCFTPSyncService");
                    service.Stop();
                    service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                    
                    _logService.LogInfo("Windows Service stopped successfully");
                    MessageBox.Show("Service stopped successfully.", "Service Control", 
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    var statusMsg = $"Service is not running. Current status: {currentStatus}";
                    _logService.LogInfo(statusMsg);
                    MessageBox.Show(statusMsg, "Service Status", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"Service stop failed: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Failed to stop Windows Service: {ex.Message}", "Service Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void InstallService_Click(object? sender, EventArgs e)
        {
            try
            {
                // Check if already installed
                if (IsServiceInstalled("CNCFTPSyncService"))
                {
                    MessageBox.Show("Service is already installed.", "Service Install", 
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var result = MessageBox.Show(
                    "This will install the G-Code Sync Windows Service.\n\n" +
                    "Administrative privileges are required.\n" +
                    "Continue with installation?", 
                    "Install Service", 
                    MessageBoxButtons.YesNo, 
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    InstallWindowsService();
                }
            }
            catch (Exception ex)
            {
                _logService?.LogError($"Service installation exception in InstallService_Click: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Failed to install service: {ex.Message}", "Service Install Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UninstallService_Click(object? sender, EventArgs e)
        {
            try
            {
                if (!IsServiceInstalled("CNCFTPSyncService"))
                {
                    MessageBox.Show("Service is not installed.", "Service Uninstall", 
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var result = MessageBox.Show(
                    "This will uninstall the G-Code Sync Windows Service.\n\n" +
                    "Administrative privileges are required.\n" +
                    "Continue with uninstallation?", 
                    "Uninstall Service", 
                    MessageBoxButtons.YesNo, 
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    UninstallWindowsService();
                }
            }
            catch (Exception ex)
            {
                _logService?.LogError($"Service uninstallation exception in UninstallService_Click: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Failed to uninstall service: {ex.Message}", "Service Uninstall Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool IsServiceInstalled(string serviceName)
        {
            try
            {
                WriteToLogFile($"IsServiceInstalled: Checking service '{serviceName}' using sc.exe");
                
                // Use sc.exe to check service status as fallback if ServiceController fails
                var startInfo = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"query \"{serviceName}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                WriteToLogFile($"IsServiceInstalled: Starting sc.exe process");
                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    WriteToLogFile($"IsServiceInstalled: Process started, waiting for exit");
                    process.WaitForExit(5000); // 5 second timeout
                    WriteToLogFile($"IsServiceInstalled: Process exited with code: {process.ExitCode}");
                    return process.ExitCode == 0; // 0 = service exists, non-zero = service not found
                }
                WriteToLogFile($"IsServiceInstalled: Failed to start process");
                return false;
            }
            catch (Exception ex)
            {
                WriteToLogFile($"IsServiceInstalled FAILED: {ex.Message}");
                WriteToLogFile($"IsServiceInstalled STACK TRACE: {ex.StackTrace}");
                _logService?.LogError($"IsServiceInstalled failed: {ex.Message}");
                return false;
            }
        }

        private ServiceControllerStatus VerifyServiceStatus(string serviceName, ServiceControllerStatus expectedStatus, int timeoutSeconds)
        {
            try
            {
                // Use sc.exe for service status checking
                var startTime = DateTime.Now;
                var timeout = TimeSpan.FromSeconds(timeoutSeconds);

                while (DateTime.Now - startTime < timeout)
                {
                    var currentStatus = GetServiceStatus(serviceName);
                    _logService.LogInfo($"Service status check: {currentStatus}");
                    
                    if (currentStatus == expectedStatus)
                    {
                        return currentStatus;
                    }
                    
                    if (currentStatus == ServiceControllerStatus.Stopped && expectedStatus == ServiceControllerStatus.Running)
                    {
                        // Service failed to start - don't wait anymore
                        _logService.LogWarning("Service failed to start - status is Stopped");
                        return currentStatus;
                    }
                    
                    System.Threading.Thread.Sleep(1000); // Wait 1 second before checking again
                }
                
                var finalStatus = GetServiceStatus(serviceName);
                _logService.LogWarning($"Service status verification timed out. Final status: {finalStatus}");
                return finalStatus;
            }
            catch (Exception ex)
            {
                _logService.LogError($"Error verifying service status: {ex.Message}");
                return ServiceControllerStatus.Stopped; // Assume stopped if we can't check
            }
        }

        private ServiceControllerStatus GetServiceStatus(string serviceName)
        {
            try
            {
                WriteToLogFile($"GetServiceStatus: About to create ServiceController for '{serviceName}'");
                using var service = new ServiceController(serviceName);
                WriteToLogFile($"GetServiceStatus: ServiceController created successfully");
                
                WriteToLogFile($"GetServiceStatus: About to get Status property");
                var status = service.Status;
                WriteToLogFile($"GetServiceStatus: Status retrieved successfully: {status}");
                
                return status;
            }
            catch (Exception ex)
            {
                WriteToLogFile($"GetServiceStatus FAILED: {ex.Message}");
                WriteToLogFile($"GetServiceStatus STACK TRACE: {ex.StackTrace}");
                _logService?.LogError($"GetServiceStatus failed: {ex.Message}");
                return ServiceControllerStatus.Stopped;
            }
        }

        private string GetRecentServiceErrors(string serviceName)
        {
            try
            {
                using var eventLog = new System.Diagnostics.EventLog("System");
                var recentErrors = new List<string>();
                var cutoff = DateTime.Now.AddMinutes(-5); // Look for errors in the last 5 minutes

                foreach (System.Diagnostics.EventLogEntry entry in eventLog.Entries)
                {
                    if (entry.TimeGenerated < cutoff) continue;
                    if (entry.EntryType != System.Diagnostics.EventLogEntryType.Error) continue;
                    if (!entry.Message.Contains(serviceName, StringComparison.OrdinalIgnoreCase)) continue;

                    recentErrors.Add($"[{entry.TimeGenerated:yyyy-MM-dd HH:mm:ss}] {entry.Message}");
                    
                    if (recentErrors.Count >= 3) break; // Limit to 3 most recent errors
                }

                return recentErrors.Any() ? string.Join("\n\n", recentErrors) : "No recent service errors found in System Event Log.";
            }
            catch (Exception ex)
            {
                return $"Unable to read Event Log: {ex.Message}";
            }
        }

        private void InstallWindowsService()
        {
            WriteToLogFile("InstallWindowsService: Method entry point");
            try
            {
                WriteToLogFile("InstallWindowsService: Inside try block");
                _logService.LogInfo("Starting Windows Service installation...");
                
                var serviceExePath = FindServiceExecutable();
                if (string.IsNullOrEmpty(serviceExePath))
                {
                    var currentDir = Path.GetDirectoryName(Application.ExecutablePath) ?? Environment.CurrentDirectory;
                    var errorMsg = $"Service executable not found in expected location: {currentDir}";
                    _logService.LogError(errorMsg);
                    MessageBox.Show($"Service executable not found.\n\nExpected location:\n" +
                                  $"{Path.Combine(currentDir, "CNCFTPSyncService.exe")}\n\n" +
                                  $"The service must be installed in the same folder as the GUI application.", 
                        "Service Install Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Check if service already exists
                if (IsServiceInstalled("CNCFTPSyncService"))
                {
                    _logService.LogInfo("Service is already installed");
                    MessageBox.Show("Service is already installed.\n\nUse 'Uninstall Service' first if you need to reinstall.", 
                        "Service Already Exists", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                _logService.LogInfo($"Found service executable at: {serviceExePath}");
                _logService.LogInfo("Installing service using sc.exe...");
                
                // Build sc.exe create command for .NET 9.0 service
                var command = $"create CNCFTPSyncService binPath= \"\\\"{serviceExePath}\\\"\" start= auto DisplayName= \"CNC FTP Sync Service\"";
                _logService.LogInfo($"Executing sc.exe command: {command}");
                
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = command,
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    Verb = "runas",
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };

                using var process = System.Diagnostics.Process.Start(processInfo);
                if (process != null)
                {
                    process.WaitForExit();
                    
                    _logService.LogInfo($"Service install exit code: {process.ExitCode}");
                    
                    if (process.ExitCode == 0)
                    {
                        _logService.LogInfo("Service created successfully, setting description...");
                        
                        // Set service description
                        var descProcess = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "sc.exe",
                            Arguments = "description CNCFTPSyncService \"Monitors CNC project folders and automatically processes and uploads files via FTP\"",
                            UseShellExecute = true,
                            CreateNoWindow = false,
                            Verb = "runas"
                        };
                        using var descProc = System.Diagnostics.Process.Start(descProcess);
                        descProc?.WaitForExit();

                        _logService.LogInfo("Service installed successfully");

                        // Ask user if they want to start the service immediately
                        var startResult = MessageBox.Show(
                            "Service installed successfully!\n\n" +
                            "Would you like to start the service now?\n\n" +
                            "Note: The service needs valid configuration to run properly. " +
                            "If you haven't configured FTP settings yet, you can start it later using 'Start Service' from the context menu.",
                            "Start Service Now?",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question);

                        if (startResult == DialogResult.Yes)
                        {
                            _logService.LogInfo("User chose to start the service immediately");
                            try
                            {
                                WriteToLogFile("InstallWindowsService: About to create ServiceController for starting service");
                                using var service = new ServiceController("CNCFTPSyncService");
                                WriteToLogFile("InstallWindowsService: ServiceController created successfully for starting");
                                service.Start();
                                WriteToLogFile("InstallWindowsService: Service.Start() called");
                                service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                                WriteToLogFile("InstallWindowsService: WaitForStatus completed");
                                
                                if (service.Status == ServiceControllerStatus.Running)
                                {
                                    _logService.LogInfo("Service installed and started successfully");
                                    MessageBox.Show("Service installed and started successfully!", "Service Control", 
                                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                                }
                                else
                                {
                                    _logService.LogError($"Service start command succeeded but service is {service.Status}. Check Event Viewer for details.");
                                    var recentErrors = GetRecentServiceErrors("CNCFTPSyncService");
                                    MessageBox.Show($"Service installed but failed to start properly.\n\nActual Status: {service.Status}\n\nThis usually means:\n- Configuration file is missing or invalid\n- Required folders don't exist\n- FTP settings are incorrect\n- Dependencies are missing\n\nRecent Event Log Entries:\n{recentErrors}\n\nYou can configure settings and then use 'Start Service' when ready.", 
                                        "Service Install Complete - Start Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logService.LogWarning($"Service failed to start: {ex.Message}");
                                MessageBox.Show($"Service installed but failed to start: {ex.Message}\n\nThis usually means:\n- Configuration is missing or invalid\n- Required folders don't exist\n- FTP settings are incorrect\n\nCheck the Windows Event Log for details, or configure settings first then try 'Start Service' again.", 
                                    "Service Start Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            }
                        }
                        else
                        {
                            _logService.LogInfo("Service installed, user chose not to start immediately");
                            MessageBox.Show("Service installed successfully!\n\nUse 'Start Service' from the context menu when ready.", 
                                "Service Install Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        _logService.LogInfo("Windows Service installation completed");
                    }
                    else
                    {
                        var errorMsg = $"Service installation failed with exit code {process.ExitCode}.";
                        _logService.LogError(errorMsg);
                        
                        string errorDetails = process.ExitCode switch
                        {
                            5 => "Access denied - Administrator privileges required",
                            1073 => "Service already exists",
                            1060 => "Service does not exist or invalid path",
                            _ => "Unknown error - Check Windows Event Viewer for details"
                        };
                        
                        MessageBox.Show($"Service installation failed.\n\nError Code: {process.ExitCode}\nDetails: {errorDetails}\n\nTroubleshooting:\n- Ensure you're running as Administrator\n- Check that the service path is correct\n- Verify no conflicting services exist", 
                            "Service Install Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else
                {
                    _logService.LogError("Failed to start sc.exe process for service installation - User may have cancelled UAC prompt");
                    MessageBox.Show("Service installation cancelled or failed to start.\n\nThis could be because:\n- UAC elevation was cancelled\n- System security policy prevents elevation", "Service Install Error", 
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"Service installation exception: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Service installation failed: {ex.Message}", "Service Install Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UninstallWindowsService() 
        {
            try
            {
                _logService.LogInfo("Starting Windows Service uninstallation...");
                
                // Stop service first
                if (IsServiceInstalled("CNCFTPSyncService"))
                {
                    _logService.LogInfo("Service found, checking if running...");
                    var currentStatus = GetServiceStatus("CNCFTPSyncService");
                    if (currentStatus == ServiceControllerStatus.Running)
                    {
                        _logService.LogInfo("Service is running, stopping it...");
                        using var service = new ServiceController("CNCFTPSyncService");
                        service.Stop();
                        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                        _logService.LogInfo("Service stopped successfully");
                    }
                    else
                    {
                        _logService.LogInfo($"Service status: {currentStatus}");
                    }
                }
                else
                {
                    _logService.LogWarning("Service not found or not installed");
                }

                _logService.LogInfo("Uninstalling service using sc.exe delete...");
                
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = "delete CNCFTPSyncService",
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    Verb = "runas"
                };

                using var process = System.Diagnostics.Process.Start(processInfo);
                if (process != null)
                {
                    process.WaitForExit();
                    
                    _logService.LogInfo($"Service uninstall exit code: {process.ExitCode}");
                    
                    if (process.ExitCode == 0)
                    {
                        MessageBox.Show("Service uninstalled successfully!", "Service Uninstall", 
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        _logService.LogInfo("Windows Service uninstalled successfully");
                    }
                    else
                    {
                        var errorMsg = $"Service uninstallation failed with exit code {process.ExitCode}. Check Windows Event Viewer for details.";
                        _logService.LogError(errorMsg);
                        MessageBox.Show($"Service uninstallation failed with exit code {process.ExitCode}.\n\n" +
                                      $"Check the Windows Event Viewer for detailed error information.", "Service Uninstall Error", 
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else
                {
                    _logService.LogError("Failed to start sc.exe process for service uninstallation - User may have cancelled UAC prompt");
                    MessageBox.Show("Service uninstallation cancelled or failed to start.\n\nThis could be because:\n- UAC elevation was cancelled\n- System security policy prevents elevation", "Service Uninstall Error", 
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"Service uninstallation exception: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Service uninstallation failed: {ex.Message}", "Service Uninstall Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Exit_Click(object? sender, EventArgs e)
        {
            _isExiting = true;
            _notifyIcon?.Dispose(); // Remove from tray immediately
            Application.Exit();
        }

        protected override void SetVisibleCore(bool value)
        {
            // Write to debug log file
            WriteToLogFile($"SetVisibleCore: Called with value={value}, _allowVisible={_allowVisible}, WindowState={WindowState}");
            
            // Prevent the form from becoming visible until explicitly allowed
            if (!_allowVisible && value)
            {
                WriteToLogFile("SetVisibleCore: Blocked visibility - form not allowed to show yet");
                return;
            }
            
            base.SetVisibleCore(value);
            
            // Only hide if we're being set to visible but form is minimized AND we're not explicitly showing it
            if (value && WindowState == FormWindowState.Minimized && !_showingExplicitly)
            {
                WriteToLogFile("SetVisibleCore: Form was minimized and not explicitly showing, hiding it");
                Hide();
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            WriteToLogFile($"FormClosing: CloseReason={e.CloseReason}, _isExiting={_isExiting}, WindowState={this.WindowState}");
            
            if (e.CloseReason == CloseReason.UserClosing && !_isExiting)
            {
                // Hide to tray on X click, but don't cancel twice
                WriteToLogFile("FormClosing: Hiding to tray instead of closing");
                e.Cancel = true;
                
                // Fix: Restore window state before hiding when maximized
                if (this.WindowState == FormWindowState.Maximized)
                {
                    WriteToLogFile("FormClosing: Form is maximized, restoring to Normal before hiding");
                    this.WindowState = FormWindowState.Normal;
                }
                
                this.Hide();
                this.ShowInTaskbar = false;
                WriteToLogFile($"FormClosing: Form hidden successfully, Visible={this.Visible}");
                // Keep tray icon active - don't dispose it
            }
            else
            {
                WriteToLogFile("FormClosing: Actually closing and disposing resources");
                // Only dispose when truly exiting (from tray menu)
                _orchestrator?.Dispose();
                _notifyIcon?.Dispose();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _orchestrator?.Dispose();
                _notifyIcon?.Dispose();
                components?.Dispose();
            }
            base.Dispose(disposing);
        }



        private void WriteToLogFile(string message)
        {
            try
            {
                string logPath = Path.Combine(Application.StartupPath, "debug.log");
                string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {message}{Environment.NewLine}";
                File.AppendAllText(logPath, logEntry);
            }
            catch
            {
                // Ignore logging errors
            }
        }

        private Icon LoadIconFromFile(string fileName)
        {
            try
            {
                string iconPath = Path.Combine(Application.StartupPath, fileName);
                WriteToLogFile($"LoadIconFromFile: Looking for icon at: {iconPath}");
                
                if (File.Exists(iconPath))
                {
                    WriteToLogFile("LoadIconFromFile: File exists, loading as icon");
                    return new Icon(iconPath, 16, 16); // Load ICO file with 16x16 size for tray
                }
                else
                {
                    WriteToLogFile("LoadIconFromFile: File not found, trying embedded resource");
                    // Try embedded resource as fallback
                    var resourceName = "CNCFTPSyncGUI.CNCFTPSync.ico";
                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        WriteToLogFile("LoadIconFromFile: Loading from embedded resource");
                        return new Icon(stream, 16, 16);
                    }
                    else
                    {
                        WriteToLogFile("LoadIconFromFile: No embedded resource found, using system icon");
                        return SystemIcons.Application;
                    }
                }
            }
            catch (Exception ex)
            {
                WriteToLogFile($"LoadIconFromFile: Error loading icon: {ex.Message}");
                // Fallback to system icon if loading fails
                return SystemIcons.Application;
            }
        }

        // Button event handlers that will be connected in the designer
        private void btnBrowseWatch_Click(object sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog();
            dialog.Description = "Select folder to monitor for new G-Code projects";
            dialog.SelectedPath = txtWatchFolder.Text;
            
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                txtWatchFolder.Text = dialog.SelectedPath;
            }
        }

        private void btnBrowseFtpUpload_Click(object sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog();
            dialog.Description = "Select folder for FTP upload staging";
            dialog.SelectedPath = txtFtpUploadFolder.Text;
            
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                txtFtpUploadFolder.Text = dialog.SelectedPath;
            }
        }

        private void btnBrowseExternalProcessor_Click(object sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog();
            dialog.Filter = "Executable Files (*.exe;*.bat;*.cmd;*.ps1)|*.exe;*.bat;*.cmd;*.ps1|All Files (*.*)|*.*";
            dialog.Title = "Select External Processor Script";
            dialog.FileName = txtExternalProcessorPath.Text;
            
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                txtExternalProcessorPath.Text = dialog.FileName;
            }
        }

        private void chkUseExternalProcessor_CheckedChanged(object sender, EventArgs e)
        {
            bool useExternal = chkUseExternalProcessor.Checked;
            
            // Enable/disable related controls
            lblExternalProcessorPath.Enabled = useExternal;
            txtExternalProcessorPath.Enabled = useExternal;
            btnBrowseExternalProcessor.Enabled = useExternal;
            
            _logService.LogInfo($"External processor usage changed to: {(useExternal ? "Enabled" : "Disabled")}");
        }

        private void btnSaveConfig_Click(object sender, EventArgs e) => SaveConfiguration();
        
        private void btnStartStandalone_Click(object sender, EventArgs e) => StartStandaloneMode();
        
        private void btnStopStandalone_Click(object sender, EventArgs e) => StopStandaloneMode();
        
        private void btnClearLogs_Click(object sender, EventArgs e)
        {
            try
            {
                txtLogs.Text = "";
                txtBottomLog.Text = "";
                
                // Clear physical log files
                ClearLogFiles();
                
                OnLogEntryAdded("INFO", "Logs cleared by user (UI and files)");
            }
            catch (Exception ex)
            {
                OnLogEntryAdded("ERROR", $"Failed to clear logs: {ex.Message}");
            }
        }

        private void ClearLogFiles()
        {
            try
            {
                // Get log directory from configuration
                var logDirectory = Path.GetDirectoryName(_config.LogFilePath);
                if (!string.IsNullOrEmpty(logDirectory) && Directory.Exists(logDirectory))
                {
                    // Clear all GCodeSync log files
                    var logFiles = Directory.GetFiles(logDirectory, "GCodeSync-*.log");
                    foreach (var logFile in logFiles)
                    {
                        try
                        {
                            File.Delete(logFile);
                        }
                        catch (Exception)
                        {
                            // Log file might be in use, try to clear content instead
                            try
                            {
                                File.WriteAllText(logFile, "");
                            }
                            catch
                            {
                                // Ignore if we can't clear it
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Don't throw, just ignore - this is a nice-to-have feature
            }
        }
        
        private void UpdateBottomLogPanel(string newLogEntry)
        {
            try
            {
                // Add new entry at the bottom
                if (!string.IsNullOrEmpty(txtBottomLog.Text))
                {
                    txtBottomLog.Text += Environment.NewLine + newLogEntry;
                }
                else
                {
                    txtBottomLog.Text = newLogEntry;
                }
                
                // Keep only last 20 lines
                var lines = txtBottomLog.Lines;
                if (lines.Length > 20)
                {
                    var keepLines = lines.Skip(lines.Length - 20);
                    txtBottomLog.Text = string.Join(Environment.NewLine, keepLines);
                }
                
                // Auto-scroll to bottom for new entries
                txtBottomLog.SelectionStart = txtBottomLog.Text.Length;
                txtBottomLog.SelectionLength = 0;
                txtBottomLog.ScrollToCaret();
            }
            catch
            {
                // Ignore errors in bottom log panel update
            }
        }
        
        private void chkAnonymousFtp_CheckedChanged(object sender, EventArgs e) => UpdateFtpCredentialsVisibility();

        private async void btnManualProcess_Click(object sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog();
            dialog.Description = "Select G-Code project folder to process manually";
            
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                if (_orchestrator != null)
                {
                    btnManualProcess.Enabled = false;
                    try
                    {
                        var result = await _orchestrator.ProcessFolderManuallyAsync(dialog.SelectedPath);
                        var message = result.Success 
                            ? $"Processing completed successfully in {result.Duration.TotalSeconds:F1} seconds"
                            : $"Processing failed: {result.Message}";
                            
                        MessageBox.Show(message, "Manual Processing Result", 
                            MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                    }
                    finally
                    {
                        btnManualProcess.Enabled = true;
                    }
                }
                else
                {
                    MessageBox.Show("Please start standalone mode first.", "Not Running", 
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private async void btnTestFtp_Click(object sender, EventArgs e)
        {
            btnTestFtp.Enabled = false;
            try
            {
                WriteToLogFile("FTP Test: Starting FTP connection test");
                SaveConfiguration(); // Save current settings first
                _config = _configService.LoadConfiguration(); // Reload to get updated settings
                
                WriteToLogFile($"FTP Test: Testing connection to {_config.FtpServer}:{_config.FtpPort}");
                
                var ftpService = new FtpService(_logService, _config);
                var success = await ftpService.TestConnectionAsync();
                
                if (success)
                {
                    WriteToLogFile("FTP Test: Connection successful");
                    MessageBox.Show("FTP connection successful!", "FTP Test", 
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    WriteToLogFile("FTP Test: Connection failed - see FTP service logs for details");
                    
                    // Get recent logs to show error details
                    var recentLogs = _logService.GetRecentLogs(10);
                    var errorDetails = "";
                    foreach (var log in recentLogs.TakeLast(5))
                    {
                        if (log.Contains("ERROR") || log.Contains("FTP"))
                        {
                            errorDetails += $"• {log}\n";
                        }
                    }
                    
                    var errorMsg = $"FTP connection failed to {_config.FtpServer}:{_config.FtpPort}\n\n" +
                                  $"Connection details:\n" +
                                  $"• Server: {_config.FtpServer}\n" +
                                  $"• Port: {_config.FtpPort}\n" +
                                  $"• Mode: {(_config.UseAnonymousFtp ? "Anonymous" : "Authenticated")}\n" +
                                  $"• User: {(_config.UseAnonymousFtp ? "anonymous" : _config.FtpUsername)}\n\n";
                    
                    if (!string.IsNullOrEmpty(errorDetails))
                    {
                        errorMsg += $"Recent error details:\n{errorDetails}\n";
                    }
                    
                    errorMsg += $"Common issues:\n" +
                               $"• Check if FTP server is running on {_config.FtpServer}\n" +
                               $"• Verify port {_config.FtpPort} is correct\n" +
                               $"• Check firewall settings\n" +
                               $"• Verify credentials if using authentication";
                    
                    MessageBox.Show(errorMsg, "FTP Test Failed", 
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                WriteToLogFile($"FTP Test: Exception occurred - {ex.Message}");
                MessageBox.Show($"FTP test error: {ex.Message}\n\nFull details logged to application logs.", "FTP Test Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnTestFtp.Enabled = true;
            }
        }

        #region FTP Browser Methods

        private void InitializeFtpBrowser()
        {
            // Set 50/50 split when tab is first accessed
            if (splitFtp.Width > 0)
            {
                splitFtp.SplitterDistance = splitFtp.Width / 2;
            }
            
            _currentLocalPath = _config.FtpUploadFolder;
            lblLocalPath.Text = $"Local Files - {_currentLocalPath}";
            lblRemotePath.Text = $"Remote FTP Server - {_config.FtpServer}:{_config.FtpPort}";
            
            // Initialize BrowserContainer explorers
            InitializeLocalBrowser();
            InitializeRemoteBrowser();
            
            lblConnectionStatus.Text = "miniExplorer file browser - drag/drop files between panes";
        }

        private void RefreshLocalExplorer()
        {
            try
            {
                WriteToLogFile($"RefreshLocalExplorer: Navigating to '{_currentLocalPath}'");
                
                // Ensure directory exists
                if (!Directory.Exists(_currentLocalPath))
                {
                    WriteToLogFile($"RefreshLocalExplorer: Directory doesn't exist, creating '{_currentLocalPath}'");
                    Directory.CreateDirectory(_currentLocalPath);
                }
                
                WriteToLogFile($"RefreshLocalExplorer: Refreshing BrowserContainer for path: {_currentLocalPath}");
                browserLocal?.NavigateTo(_currentLocalPath);
                lblLocalPath.Text = $"Local Files - {_currentLocalPath}";
                txtLocalAddress.Text = _currentLocalPath;
                
                WriteToLogFile($"RefreshLocalExplorer: Loaded local folder into BrowserContainer");
            }
            catch (Exception ex)
            {
                WriteToLogFile($"Error refreshing local Explorer: {ex.Message}");
                MessageBox.Show($"Error opening local folder: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RefreshRemoteExplorer()
        {
            try
            {
                WriteToLogFile($"RefreshRemoteExplorer: Refreshing current FTP directory: {_currentRemoteFtpPath}");
                
                // Refresh the current FTP directory instead of going to FtpUploadFolder
                LoadRemoteFtpDirectory(_currentRemoteFtpPath);
                
                WriteToLogFile($"RefreshRemoteExplorer: Refreshed FTP directory: {_currentRemoteFtpPath}");
            }
            catch (Exception ex)
            {
                WriteToLogFile($"Error refreshing remote Explorer: {ex.Message}");
                lblConnectionStatus.Text = $"Error: {ex.Message}";
            }
        }

        private void OpenLocalExternal()
        {
            try
            {
                WriteToLogFile($"Opening local folder externally: {_currentLocalPath}");
                System.Diagnostics.Process.Start("explorer.exe", _currentLocalPath);
            }
            catch (Exception ex)
            {
                WriteToLogFile($"Error opening local folder externally: {ex.Message}");
                MessageBox.Show($"Error opening folder: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenRemoteExternal()
        {
            try
            {
                string ftpUrl = $"ftp://{_config.FtpServer}:{_config.FtpPort}/";
                WriteToLogFile($"Opening FTP server externally: {ftpUrl}");
                System.Diagnostics.Process.Start("explorer.exe", ftpUrl);
            }
            catch (Exception ex)
            {
                WriteToLogFile($"Error opening FTP server externally: {ex.Message}");
                MessageBox.Show($"Error opening FTP server: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Duplicate NavigateLocalTo method removed - using WebView2 version at line 24

        private void NavigateRemoteTo(string ftpPath)
        {
            try
            {
                // For FTP navigation, we'll need to implement proper FTP browsing later
                // For now, navigate to a local path
                browserRemote?.NavigateTo(_config.FtpUploadFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                _currentRemotePath = ftpPath;
                WriteToLogFile($"Navigated remote to: {ftpPath}");
            }
            catch (Exception ex)
            {
                WriteToLogFile($"Error navigating remote to {ftpPath}: {ex.Message}");
                MessageBox.Show($"Error navigating to: {ftpPath}\n{ex.Message}", "Navigation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #region WebBrowser Event Handlers



        #endregion

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

        private void InitializeLocalBrowser()
        {
            try
            {
                // Default to FTP upload folder or Documents folder
                string defaultPath = _config.FtpUploadFolder;
                if (string.IsNullOrEmpty(defaultPath) || !Directory.Exists(defaultPath))
                {
                    defaultPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                }
                
                // Set the root boundary for local browser
                if (browserLocal != null)
                {
                    browserLocal.RootPath = defaultPath;
                }
                
                WriteToLogFile($"Initializing local browser to: {defaultPath} (Root boundary set)");
                browserLocal?.NavigateTo(defaultPath);
                _currentLocalPath = defaultPath;
                
                // Update address bar
                txtLocalAddress.Text = defaultPath;
            }
            catch (Exception ex)
            {
                WriteToLogFile($"Error initializing local BrowserContainer: {ex.Message}");
            }
        }

        private void InitializeRemoteBrowser()
        {
            try
            {
                WriteToLogFile("Initializing remote browser with FTP connection");
                
                // Initialize FTP service for browsing
                if (_ftpService == null)
                {
                    _ftpService = new FtpService(_logService, _config);
                }
                
                // Hook up FTP navigation events
                if (browserRemote != null)
                {
                    browserRemote.DirectoryChanged += OnRemoteBrowserDirectoryChanged;
                }
                
                // Update address bar to show FTP URL
                txtRemoteAddress.Text = $"ftp://{_config.FtpServer}:{_config.FtpPort}/";
                
                // Update the label to show it as FTP server
                lblRemotePath.Text = $"Remote FTP Server - {_config.FtpServer}:{_config.FtpPort}";
                
                // Load FTP directory contents
                LoadRemoteFtpDirectory("/");
            }
            catch (Exception ex)
            {
                WriteToLogFile($"Error initializing remote FTP browser: {ex.Message}");
                
                // Fallback to local path if FTP fails
                string fallback = _config.FtpUploadFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                browserRemote?.NavigateTo(fallback);
                txtRemoteAddress.Text = fallback;
                lblRemotePath.Text = "Remote FTP Server - Connection Failed (Using Local)";
            }
        }

        private void OnRemoteBrowserDirectoryChanged(object? sender, string newPath)
        {
            // Check if this is FTP navigation by looking for FTP markers
            WriteToLogFile($"Remote browser directory changed to: {newPath}");
            
            // If this is an FTP browser (temp directory), handle navigation specially
            if (!string.IsNullOrEmpty(newPath) && newPath.Contains("GCodeSync_FTP_Browser"))
            {
                WriteToLogFile("FTP browser navigation detected");
                HandleFtpDirectoryNavigation(newPath);
            }
            else
            {
                WriteToLogFile($"Non-FTP directory navigation: {newPath}");
            }
        }

        private string _currentRemoteFtpPath = "/";

        private async void LoadRemoteFtpDirectory(string ftpPath)
        {
            try
            {
                WriteToLogFile($"Loading FTP directory: {ftpPath}");
                
                if (_ftpService == null)
                {
                    WriteToLogFile("FTP service not initialized");
                    return;
                }

                // Test connection first
                bool connected = await _ftpService.TestConnectionAsync();
                if (!connected)
                {
                    WriteToLogFile("FTP connection test failed");
                    lblConnectionStatus.Text = "FTP Connection Failed";
                    return;
                }

                WriteToLogFile($"FTP connection successful, getting directory listing for: {ftpPath}");
                
                // Get actual FTP directory listing
                var ftpFiles = await _ftpService.ListDirectoryAsync(ftpPath);
                
                // Store current FTP path for navigation and update UI
                _currentRemoteFtpPath = ftpPath;
                string displayPath = $"ftp://{_config.FtpServer}:{_config.FtpPort}{ftpPath}";
                
                // Update UI on the main thread
                WriteToLogFile($"Updating breadcrumb to: {displayPath}");
                if (InvokeRequired)
                {
                    Invoke(() => {
                        txtRemoteAddress.Text = displayPath;
                        lblRemotePath.Text = $"Remote FTP Server - {displayPath}";
                        WriteToLogFile($"Breadcrumb updated (Invoke): txtRemoteAddress='{txtRemoteAddress.Text}'");
                    });
                }
                else
                {
                    txtRemoteAddress.Text = displayPath;
                    lblRemotePath.Text = $"Remote FTP Server - {displayPath}";
                    WriteToLogFile($"Breadcrumb updated (Direct): txtRemoteAddress='{txtRemoteAddress.Text}'");
                }
                
                // Create a clean temporary directory for this FTP path
                string tempFtpDir = Path.Combine(Path.GetTempPath(), "GCodeSync_FTP_Browser", ftpPath.TrimStart('/').Replace('/', '_'));
                if (Directory.Exists(tempFtpDir))
                {
                    Directory.Delete(tempFtpDir, true);
                }
                Directory.CreateDirectory(tempFtpDir);
                
                // Simple rule: Never show ".." navigation when at FTP server root "/"
                // This completely prevents any navigation above root
                if (ftpPath != "/")
                {
                    string parentDir = Path.Combine(tempFtpDir, "..");
                    Directory.CreateDirectory(parentDir);
                    File.WriteAllText(Path.Combine(parentDir, "_PARENT_DIR.info"), "Parent Directory");
                    WriteToLogFile($"Created parent navigation for FTP path: {ftpPath}");
                }
                else
                {
                    WriteToLogFile($"At FTP root '/' - no parent navigation shown");
                }
                
                // Create local representations of actual FTP files/folders
                foreach (var ftpItem in ftpFiles)
                {
                    
                    if (ftpItem.IsDirectory)
                    {
                        // Create actual directory for proper folder icon
                        string dirPath = Path.Combine(tempFtpDir, ftpItem.Name);
                        Directory.CreateDirectory(dirPath);
                        
                        // Set directory timestamp to match FTP timestamp
                        try
                        {
                            Directory.SetCreationTime(dirPath, ftpItem.ModifiedDate);
                            Directory.SetLastWriteTime(dirPath, ftpItem.ModifiedDate);
                        }
                        catch
                        {
                            // Ignore timestamp setting errors
                        }
                        
                        // Mark this as an FTP directory for navigation
                        File.WriteAllText(Path.Combine(dirPath, "_FTP_DIR_MARKER.info"), ftpItem.FullPath);
                    }
                    else
                    {
                        // Create file with original name and extension for proper icons
                        string filePath = Path.Combine(tempFtpDir, ftpItem.Name);
                        File.WriteAllText(filePath, 
                            $"FTP File: {ftpItem.Name}\nSize: {ftpItem.Size:N0} bytes\nFTP Path: {ftpItem.FullPath}\nServer: {_config.FtpServer}:{_config.FtpPort}\nModified: {ftpItem.ModifiedDate}\n\nThis represents an actual file on the FTP server.\nDouble-click to download and open.");
                        
                        // Set file timestamp to match FTP timestamp
                        try
                        {
                            File.SetCreationTime(filePath, ftpItem.ModifiedDate);
                            File.SetLastWriteTime(filePath, ftpItem.ModifiedDate);
                        }
                        catch
                        {
                            // Ignore timestamp setting errors
                        }
                    }
                }
                
                // Update the browser and status on the main thread
                if (InvokeRequired)
                {
                    Invoke(() => {
                        browserRemote?.NavigateTo(tempFtpDir);
                        lblConnectionStatus.Text = $"FTP: {_config.FtpServer}:{_config.FtpPort} - {ftpFiles.Count} items in {ftpPath}";
                    });
                }
                else
                {
                    browserRemote?.NavigateTo(tempFtpDir);
                    lblConnectionStatus.Text = $"FTP: {_config.FtpServer}:{_config.FtpPort} - {ftpFiles.Count} items in {ftpPath}";
                }
                WriteToLogFile($"FTP browser showing {ftpFiles.Count} actual server items from {_config.FtpServer}");
            }
            catch (Exception ex)
            {
                WriteToLogFile($"Error loading FTP directory {ftpPath}: {ex.Message}");
                lblConnectionStatus.Text = $"FTP Error: {ex.Message}";
            }
        }

        private void HandleFtpDirectoryNavigation(string localDirPath)
        {
            try
            {
                // Check if this is a parent directory navigation
                if (Path.GetFileName(localDirPath) == "..")
                {
                    // Strict FTP root boundary check - never go above "/"
                    if (_currentRemoteFtpPath == "/" || _currentRemoteFtpPath == "")
                    {
                        WriteToLogFile("Cannot navigate above FTP server root - blocking navigation");
                        return; // Block navigation above root
                    }
                    
                    // Navigate to parent FTP directory, but not above root "/"
                    string parentPath = "/";
                    if (_currentRemoteFtpPath != "/" && _currentRemoteFtpPath.Length > 1)
                    {
                        var parts = _currentRemoteFtpPath.TrimEnd('/').Split('/');
                        if (parts.Length > 1)
                        {
                            parentPath = string.Join("/", parts.Take(parts.Length - 1));
                            if (!parentPath.StartsWith("/"))
                                parentPath = "/" + parentPath;
                            if (string.IsNullOrEmpty(parentPath.TrimStart('/')))
                                parentPath = "/";
                        }
                    }
                    
                    // Additional safety check - don't allow empty or invalid paths
                    if (string.IsNullOrEmpty(parentPath) || parentPath.Length < 1)
                    {
                        parentPath = "/";
                    }
                    
                    WriteToLogFile($"FTP parent navigation from {_currentRemoteFtpPath} to {parentPath}");
                    _ = Task.Run(() => LoadRemoteFtpDirectory(parentPath));
                    return;
                }
                
                // Check if this is navigation back to parent (root FTP directory)
                string tempFtpBaseDir = Path.Combine(Path.GetTempPath(), "GCodeSync_FTP_Browser");
                WriteToLogFile($"Checking if localDirPath '{localDirPath}' equals tempFtpBaseDir '{tempFtpBaseDir}'");
                
                if (localDirPath.Equals(tempFtpBaseDir, StringComparison.OrdinalIgnoreCase))
                {
                    WriteToLogFile("Detected parent navigation - moved back to FTP root temp directory");
                    
                    // This is parent navigation - go back to FTP root
                    if (_currentRemoteFtpPath != "/")
                    {
                        string parentPath = "/";
                        if (_currentRemoteFtpPath != "/" && _currentRemoteFtpPath.Length > 1)
                        {
                            var parts = _currentRemoteFtpPath.TrimEnd('/').Split('/');
                            if (parts.Length > 1)
                            {
                                parentPath = string.Join("/", parts.Take(parts.Length - 1));
                                if (!parentPath.StartsWith("/"))
                                    parentPath = "/" + parentPath;
                                if (string.IsNullOrEmpty(parentPath.TrimStart('/')))
                                    parentPath = "/";
                            }
                        }
                        
                        WriteToLogFile($"FTP parent navigation from {_currentRemoteFtpPath} to {parentPath}");
                        _ = Task.Run(() => LoadRemoteFtpDirectory(parentPath));
                        return;
                    }
                    else
                    {
                        WriteToLogFile("Already at FTP root, cannot navigate up further");
                        return;
                    }
                }

                // Check if this directory has an FTP marker
                string markerFile = Path.Combine(localDirPath, "_FTP_DIR_MARKER.info");
                if (File.Exists(markerFile))
                {
                    string ftpPath = File.ReadAllText(markerFile).Trim();
                    WriteToLogFile($"FTP directory navigation to: {ftpPath}");
                    _ = Task.Run(() => LoadRemoteFtpDirectory(ftpPath));
                }
                else
                {
                    WriteToLogFile($"No FTP marker found in directory: {localDirPath}");
                }
            }
            catch (Exception ex)
            {
                WriteToLogFile($"Error handling FTP navigation: {ex.Message}");
            }
        }

        private async Task LoadRemoteFtpDirectoryAsync(string ftpPath)
        {
            await Task.Run(() => LoadRemoteFtpDirectory(ftpPath));
        }

        #region Browser Navigation Event Handlers

        private void BtnLocalBack_Click(object sender, EventArgs e)
        {
            try
            {
                browserLocal?.GoToParentDirectory();
                _currentLocalPath = browserLocal?.DirPath ?? _currentLocalPath;
                txtLocalAddress.Text = _currentLocalPath;
            }
            catch (Exception ex)
            {
                WriteToLogFile($"Error navigating back: {ex.Message}");
            }
        }

        private void BtnLocalForward_Click(object sender, EventArgs e)
        {
            // Forward navigation could implement navigation history
            WriteToLogFile("Local forward navigation clicked");
        }

        private void BtnRemoteBack_Click(object sender, EventArgs e)
        {
            try
            {
                browserRemote?.GoToParentDirectory();
                txtRemoteAddress.Text = browserRemote?.DirPath ?? _currentRemotePath;
            }
            catch (Exception ex)
            {
                WriteToLogFile($"Error navigating remote back: {ex.Message}");
            }
        }

        private void BtnRemoteForward_Click(object sender, EventArgs e)
        {
            // Forward navigation could implement navigation history
            WriteToLogFile("Remote forward navigation clicked");
        }

        private void ResizeBrowserContainers()
        {
            try
            {
                if (browserLocal != null && pnlLocalFiles.Width > 20 && pnlLocalFiles.Height > 120)
                {
                    browserLocal.Size = new Size(pnlLocalFiles.Width - 10, pnlLocalFiles.Height - 100);
                }
                
                if (browserRemote != null && pnlRemoteFiles.Width > 20 && pnlRemoteFiles.Height > 120)
                {
                    browserRemote.Size = new Size(pnlRemoteFiles.Width - 10, pnlRemoteFiles.Height - 100);
                }
            }
            catch (Exception ex)
            {
                WriteToLogFile($"Error resizing browser containers: {ex.Message}");
            }
        }

        /// <summary>
        /// Preview FTP folder contents in file browser without changing navigation
        /// </summary>
        public async void PreviewFtpFolderContents(string ftpFolderPath)
        {
            try
            {
                if (_ftpService == null || _config == null) return;
                
                // Get the actual FTP path from the marker file
                string ftpPath = ftpFolderPath;
                string markerFile = Path.Combine(ftpFolderPath, "_FTP_DIR_MARKER.info");
                if (File.Exists(markerFile))
                {
                    ftpPath = File.ReadAllText(markerFile).Trim();
                }
                
                // Get FTP directory contents
                var ftpFiles = await _ftpService.ListDirectoryAsync(ftpPath);
                
                // Create a temporary directory for preview
                string previewTempDir = Path.Combine(Path.GetTempPath(), "GCodeSync_FTP_Preview", ftpPath.TrimStart('/').Replace('/', '_'));
                if (Directory.Exists(previewTempDir))
                {
                    Directory.Delete(previewTempDir, true);
                }
                Directory.CreateDirectory(previewTempDir);
                
                // Create file representations (files only, not subdirectories for preview)
                foreach (var ftpItem in ftpFiles)
                {
                    if (!ftpItem.IsDirectory)
                    {
                        string filePath = Path.Combine(previewTempDir, ftpItem.Name);
                        File.WriteAllText(filePath, 
                            $"FTP File: {ftpItem.Name}\nSize: {ftpItem.Size:N0} bytes\nFTP Path: {ftpItem.FullPath}\nServer: {_config.FtpServer}:{_config.FtpPort}\nModified: {ftpItem.ModifiedDate}\n\nThis represents an actual file on the FTP server.\nDouble-click to download and open.");
                        
                        // Set file timestamp to match FTP timestamp
                        try
                        {
                            File.SetCreationTime(filePath, ftpItem.ModifiedDate);
                            File.SetLastWriteTime(filePath, ftpItem.ModifiedDate);
                        }
                        catch
                        {
                            // Ignore timestamp setting errors
                        }
                    }
                }
                
                // Update only the file browser in the remote BrowserContainer
                if (InvokeRequired)
                {
                    Invoke(() => {
                        browserRemote?.PreviewFiles(previewTempDir);
                    });
                }
                else
                {
                    browserRemote?.PreviewFiles(previewTempDir);
                }
            }
            catch (Exception ex)
            {
                // Silently handle preview errors to avoid disrupting navigation
                WriteToLogFile($"Error previewing FTP folder contents: {ex.Message}");
            }
        }

        #endregion

        #region System Tray and File Menu Event Handlers

        private void CloseToTray_Click(object? sender, EventArgs e)
        {
            try
            {
                _logService.LogInfo("Closing application to system tray");
                this.Hide();
                this.notifyIcon.Visible = true;
                this.notifyIcon.ShowBalloonTip(3000, "CNC-FTP-SYNC", "Application minimized to system tray. Right-click the tray icon to access service controls.", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                _logService.LogError($"Error closing to tray: {ex.Message}");
            }
        }

        private void CloseFully_Click(object? sender, EventArgs e)
        {
            try
            {
                _logService.LogInfo("User requesting full application closure");
                
                var result = MessageBox.Show(
                    "⚠️ WARNING: This will completely close the CNC-FTP-SYNC application.\n\n" +
                    "This action may affect system processing:\n" +
                    "• Active file monitoring will stop\n" +
                    "• File processing operations will be interrupted\n" +
                    "• FTP uploads in progress may fail\n" +
                    "• The Windows service (if running) will continue independently\n\n" +
                    "Are you sure you want to close the application completely?",
                    "Close Application - Warning",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                
                if (result == DialogResult.Yes)
                {
                    _logService.LogInfo("User confirmed full application closure");
                    this.notifyIcon.Visible = false;
                    Application.Exit();
                }
                else
                {
                    _logService.LogInfo("User cancelled full application closure");
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"Error closing application: {ex.Message}");
            }
        }

        private void UninstallAndClose_Click(object? sender, EventArgs e)
        {
            try
            {
                _logService.LogInfo("User requesting service uninstall and application closure");
                
                var warningResult = MessageBox.Show(
                    "⚠️ WARNING: This will uninstall the Windows service and completely close the application.\n\n" +
                    "This action may severely affect system processing:\n" +
                    "• All file monitoring and processing will stop immediately\n" +
                    "• Active FTP uploads will be interrupted\n" +
                    "• The Windows service will be permanently removed\n" +
                    "• Any ongoing sync operations will fail\n" +
                    "• You'll need to reinstall the service to resume automatic processing\n\n" +
                    "Are you sure you want to uninstall the service and close the application?",
                    "Uninstall Service and Close - Critical Warning",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                
                if (warningResult == DialogResult.Yes)
                {
                    if (IsServiceInstalled("CNCFTPSyncService"))
                    {
                        var confirmResult = MessageBox.Show(
                            "Final confirmation: Uninstall the Windows service and close the application?", 
                            "Confirm Uninstall and Exit", 
                            MessageBoxButtons.YesNo, 
                            MessageBoxIcon.Question);
                        
                        if (confirmResult == DialogResult.Yes)
                        {
                            _logService.LogInfo("User double-confirmed service uninstall and closure");
                            UninstallService_Click(sender, e); // Use existing uninstall logic
                            
                            // Close after uninstall completes
                            _logService.LogInfo("Service uninstalled, closing application");
                            this.notifyIcon.Visible = false;
                            Application.Exit();
                        }
                        else
                        {
                            _logService.LogInfo("User cancelled at final confirmation");
                        }
                    }
                    else
                    {
                        _logService.LogInfo("No service to uninstall, proceeding with application closure");
                        var closeResult = MessageBox.Show(
                            "No Windows service is installed. Close the application anyway?",
                            "No Service Found - Close Application?",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question);
                            
                        if (closeResult == DialogResult.Yes)
                        {
                            this.notifyIcon.Visible = false;
                            Application.Exit();
                        }
                    }
                }
                else
                {
                    _logService.LogInfo("User cancelled uninstall and close operation");
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"Error uninstalling service and closing: {ex.Message}");
            }
        }

        private void NotifyIcon_MouseClick(object? sender, MouseEventArgs e)
        {
            try
            {
                if (e.Button == MouseButtons.Left)
                {
                    // Left click shows the main form
                    TrayShow_Click(sender, EventArgs.Empty);
                }
                // Right click automatically shows context menu (handled by ContextMenuStrip)
            }
            catch (Exception ex)
            {
                _logService.LogError($"Error handling tray icon click: {ex.Message}");
            }
        }

        private void TrayShow_Click(object? sender, EventArgs e)
        {
            try
            {
                _logService.LogInfo("Showing application from system tray");
                this.Show();
                this.WindowState = FormWindowState.Normal;
                this.Activate();
                this.BringToFront();
                this.notifyIcon.Visible = false;
            }
            catch (Exception ex)
            {
                _logService.LogError($"Error showing application from tray: {ex.Message}");
            }
        }

        private void About_Click(object? sender, EventArgs e)
        {
            try
            {
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                var versionString = version != null ? version.ToString() : "Unknown";
                
                var aboutMessage = $@"CNC-FTP-SYNC
Windows G-Code Sync Tool

Version: {versionString}
Copyright © 2025 Ben Harper 3DTek

A comprehensive Windows application that monitors folders for G-code file changes and automatically processes and uploads them via FTP.

Features:
• Smart folder monitoring with file completion detection
• G-code processing with coordinate conversion
• FTP integration with error handling
• Windows Service integration
• Automatic updates
• System tray operation

Visit: https://3dtek-xyz.github.io/CNC-FTPSync/";

                MessageBox.Show(aboutMessage, "About CNC-FTP-SYNC", 
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                
                _logService.LogInfo("About dialog displayed");
            }
            catch (Exception ex)
            {
                _logService.LogError($"Error showing About dialog: {ex.Message}");
                MessageBox.Show("Error displaying About information.", "Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void CheckForUpdates_Click(object? sender, EventArgs e)
        {
            try
            {
                _logService.LogInfo("=== Custom Update Check Initiated ===");
                _logService.LogInfo("🔄 User requested manual update check via menu");
                
                const string updateUrl = "https://3dtek-xyz.github.io/CNC-FTPSync/update.xml";
                _logService.LogInfo($"🔗 Update check URL: {updateUrl}");

                await CheckForUpdatesAsync(updateUrl, true);
            }
            catch (Exception ex)
            {
                _logService.LogError($"❌ Fatal error during manual update check: {ex.Message}");
                _logService.LogError($"Manual check exception: {ex}");
                MessageBox.Show($"Error checking for updates: {ex.Message}", 
                    "Update Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task CheckForUpdatesAsync(string updateUrl, bool isManualCheck = false)
        {
            try
            {
                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(30);
                
                _logService.LogInfo($"🌐 Downloading update information from: {updateUrl}");
                string xmlContent = await httpClient.GetStringAsync(updateUrl);
                _logService.LogInfo($"✅ Downloaded {xmlContent.Length} bytes of update data");

                // Parse XML
                var doc = new System.Xml.XmlDocument();
                doc.LoadXml(xmlContent);
                
                var versionNode = doc.SelectSingleNode("//version");
                var urlNode = doc.SelectSingleNode("//url");
                var changelogNode = doc.SelectSingleNode("//changelog");
                
                if (versionNode == null || urlNode == null)
                {
                    throw new Exception("Invalid update XML - missing version or URL");
                }

                string remoteVersionStr = versionNode.InnerText.Trim();
                string downloadUrl = urlNode.InnerText.Trim();
                string changelogUrl = changelogNode?.InnerText.Trim() ?? "";
                
                _logService.LogInfo($"📦 Remote version: {remoteVersionStr}");
                _logService.LogInfo($"🔗 Download URL: {downloadUrl}");

                // Get current version
                var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                var remoteVersion = new Version(remoteVersionStr);
                
                _logService.LogInfo($"📋 Current version: {currentVersion}");
                _logService.LogInfo($"🆚 Comparing versions: {currentVersion} vs {remoteVersion}");

                bool updateAvailable = remoteVersion > currentVersion;
                
                if (updateAvailable)
                {
                    _logService.LogInfo($"🎯 Update available! {currentVersion} → {remoteVersion}");
                    
                    var result = MessageBox.Show(
                        $"A new version of CNC-FTP-SYNC is available!\n\n" +
                        $"Current version: {currentVersion}\n" +
                        $"New version: {remoteVersion}\n\n" +
                        $"Would you like to download and install the update now?",
                        "Update Available",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);
                        
                    if (result == DialogResult.Yes)
                    {
                        _logService.LogInfo("🚀 User chose to install update");
                        await DownloadAndInstallUpdateAsync(downloadUrl, changelogUrl);
                    }
                    else
                    {
                        _logService.LogInfo("⏭️ User chose to skip update");
                    }
                }
                else
                {
                    _logService.LogInfo("✅ Application is up to date");
                    if (isManualCheck)
                    {
                        MessageBox.Show("You have the latest version of CNC-FTP-SYNC!", 
                            "No Updates Available", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"❌ Update check failed: {ex.Message}");
                if (isManualCheck)
                {
                    MessageBox.Show($"Failed to check for updates:\n{ex.Message}", 
                        "Update Check Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private async Task DownloadAndInstallUpdateAsync(string downloadUrl, string changelogUrl)
        {
            try
            {
                _logService.LogInfo($"⬇️ Starting download from: {downloadUrl}");
                
                // Show changelog if available
                if (!string.IsNullOrEmpty(changelogUrl))
                {
                    var showChangelog = MessageBox.Show(
                        "Would you like to view the release notes before updating?",
                        "View Release Notes",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);
                        
                    if (showChangelog == DialogResult.Yes)
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = changelogUrl,
                            UseShellExecute = true
                        });
                    }
                }
                
                // Create temp directory
                string tempDir = Path.Combine(Path.GetTempPath(), "CNC-FTP-SYNC-Update");
                Directory.CreateDirectory(tempDir);
                
                string fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
                string tempFile = Path.Combine(tempDir, fileName);
                
                _logService.LogInfo($"💾 Downloading to: {tempFile}");
                
                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.Timeout = TimeSpan.FromMinutes(10); // Large file timeout
                
                var response = await httpClient.GetAsync(downloadUrl);
                response.EnsureSuccessStatusCode();
                
                await using var fileStream = new FileStream(tempFile, FileMode.Create);
                await response.Content.CopyToAsync(fileStream);
                
                _logService.LogInfo($"✅ Download completed: {new FileInfo(tempFile).Length} bytes");
                
                // Verify it's an MSI file
                if (!fileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception("Downloaded file is not an MSI installer");
                }
                
                // Stop the service before updating
                try
                {
                    if (IsServiceInstalled("CNCFTPSyncService"))
                    {
                        var currentStatus = GetServiceStatus("CNCFTPSyncService");
                        if (currentStatus == ServiceControllerStatus.Running)
                        {
                            _logService.LogInfo("🛑 Stopping service for update...");
                            using var service = new ServiceController("CNCFTPSyncService");
                            service.Stop();
                            VerifyServiceStatus("CNCFTPSyncService", ServiceControllerStatus.Stopped, 30);
                        }
                    }
                }
                catch (Exception serviceEx)
                {
                    _logService.LogWarning($"⚠️ Could not stop service: {serviceEx.Message}");
                }
                
                // Start the installer
                _logService.LogInfo("🚀 Starting MSI installer...");
                
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "msiexec",
                    Arguments = $"/i \"{tempFile}\" /quiet /norestart",
                    UseShellExecute = true,
                    Verb = "runas" // Request admin privileges
                };
                
                var process = System.Diagnostics.Process.Start(startInfo);
                
                if (process != null)
                {
                    _logService.LogInfo("⏳ Waiting for installer to complete...");
                    MessageBox.Show(
                        "The update installer is running.\n\n" +
                        "This application will close and restart automatically with the new version.",
                        "Installing Update",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    
                    // Create a batch file to restart the application after update
                    string batchFile = Path.Combine(tempDir, "restart_app.bat");
                    string appPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    
                    var batchContent = $@"@echo off
echo Waiting for installer to complete and application to close...
timeout /t 5 /nobreak >nul

:wait_for_install
tasklist /fi ""imagename eq msiexec.exe"" 2>nul | find /i ""msiexec.exe"" >nul
if not errorlevel 1 (
    timeout /t 2 /nobreak >nul
    goto wait_for_install
)

echo Installer completed, starting application...
timeout /t 2 /nobreak >nul
start """" ""{appPath}""

echo Cleaning up temporary files...
timeout /t 2 /nobreak >nul
rd /s /q ""{tempDir}"" 2>nul
";
                    
                    File.WriteAllText(batchFile, batchContent);
                    _logService.LogInfo($"📝 Created restart script: {batchFile}");
                    
                    // Start the restart script
                    var restartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = batchFile,
                        UseShellExecute = true,
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                    };
                    
                    System.Diagnostics.Process.Start(restartInfo);
                    _logService.LogInfo("🔄 Started restart script, closing application for update...");
                    
                    Application.Exit();
                }
                else
                {
                    throw new Exception("Failed to start the installer");
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"❌ Update installation failed: {ex.Message}");
                MessageBox.Show($"Failed to install update:\n{ex.Message}", 
                    "Update Installation Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string FindServiceExecutable()
        {
            // Per WiX installer: service is installed in the same folder as GUI (INSTALLFOLDER)
            var currentDir = Path.GetDirectoryName(Application.ExecutablePath) ?? Environment.CurrentDirectory;
            _logService.LogInfo($"Current executable directory: {currentDir}");
            
            // Service must be in same directory as GUI application (WiX INSTALLFOLDER)
            var serviceExePath = Path.Combine(currentDir, "CNCFTPSyncService.exe");
            _logService.LogInfo($"Expected service path: {serviceExePath}");
            
            if (!File.Exists(serviceExePath))
            {
                _logService.LogError($"Service executable not found at expected location: {serviceExePath}");
                return string.Empty;
            }
            
            return serviceExePath;
        }



        #endregion

        #endregion
    }
} 