using GCodeSyncCore.Models;
using GCodeSyncCore.Services;
using System.ServiceProcess;
using System.Diagnostics;

namespace GCodeSyncGUI
{
    public partial class MainForm : Form
    {
        private readonly IConfigurationService _configService;
        private readonly ILogService _logService;
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
            InitializeComponent();
            _configService = new ConfigurationService();
            _logService = new LogService();
            _config = _configService.LoadConfiguration();
            
            InitializeNotifyIcon();
            LoadConfiguration();
            SetupEventHandlers();
            
            // Start minimized to system tray
            this.WindowState = FormWindowState.Minimized;
            this.ShowInTaskbar = false;
        }

        private void InitializeNotifyIcon()
        {
            _notifyIcon = new NotifyIcon()
            {
                Text = "CBWSS G-Code Sync Tool",
                Visible = true,
                Icon = LoadIconFromFile("CBWSS-Logo.png")
            };

            // Create context menu for system tray
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Show", null, ShowForm_Click);
            contextMenu.Items.Add("Configuration", null, Configuration_Click);
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Start Service", null, StartService_Click);
            contextMenu.Items.Add("Stop Service", null, StopService_Click);
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Exit", null, Exit_Click);

            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.DoubleClick += ShowForm_Click;

            UpdateNotifyIconStatus("Stopped");
        }

        private void UpdateNotifyIconStatus(string status)
        {
            if (_notifyIcon == null) return;

            // Update icon based on status
            var iconText = $"CBWSS G-Code Sync - {status}";
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
            try
            {
                if (_orchestrator?.IsRunning == true)
                {
                    MessageBox.Show("Standalone mode is already running.", "Already Running", 
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                btnStartStandalone.Enabled = false;
                btnStopStandalone.Enabled = true;

                // Initialize orchestrator
                var gCodeProcessor = new GCodeProcessorService(_logService, _config);
                var ftpService = new FtpService(_logService, _config);
                _orchestrator = new SyncOrchestrator(_configService, _logService, gCodeProcessor, ftpService);

                _orchestrator.StatusChanged += (status) => UpdateNotifyIconStatus(status);
                
                await _orchestrator.StartAsync();
                
                _logService.LogInfo("Standalone mode started from GUI");
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
                var service = new ServiceController("GCodeSyncService");
                if (service.Status != ServiceControllerStatus.Running)
                {
                    service.Start();
                    _logService.LogInfo("Windows Service start command sent");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start Windows Service: {ex.Message}", "Service Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StopService_Click(object? sender, EventArgs e)
        {
            try
            {
                var service = new ServiceController("GCodeSyncService");
                if (service.Status == ServiceControllerStatus.Running)
                {
                    service.Stop();
                    _logService.LogInfo("Windows Service stop command sent");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to stop Windows Service: {ex.Message}", "Service Error", 
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
                    WriteToLogFile("LoadIconFromFile: File exists, loading as bitmap");
                    using (var bitmap = new Bitmap(iconPath))
                    {
                        // Resize bitmap to 16x16 for tray icon
                        using (var resized = new Bitmap(bitmap, new Size(16, 16)))
                        {
                            return Icon.FromHandle(resized.GetHicon());
                        }
                    }
                }
                else
                {
                    WriteToLogFile("LoadIconFromFile: File not found, using system icon");
                    // Fallback to system icon if file not found
                    return SystemIcons.Application;
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

        private void btnSaveConfig_Click(object sender, EventArgs e) => SaveConfiguration();
        
        private void btnStartStandalone_Click(object sender, EventArgs e) => StartStandaloneMode();
        
        private void btnStopStandalone_Click(object sender, EventArgs e) => StopStandaloneMode();
        
        private void btnClearLogs_Click(object sender, EventArgs e)
        {
            try
            {
                txtLogs.Text = "";
                txtBottomLog.Text = "";
                OnLogEntryAdded("INFO", "Log cleared by user");
            }
            catch (Exception ex)
            {
                OnLogEntryAdded("ERROR", $"Failed to clear logs: {ex.Message}");
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

        private async void OnRemoteBrowserDirectoryChanged(object? sender, string newPath)
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

        #endregion
    }
}