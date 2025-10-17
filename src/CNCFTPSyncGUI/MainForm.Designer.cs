namespace CNCFTPSyncGUI
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;
        private MenuStrip menuStrip;
        private ToolStripMenuItem fileToolStripMenuItem;
        private ToolStripMenuItem closeToTrayToolStripMenuItem;
        private ToolStripMenuItem closeFullyToolStripMenuItem;
        private ToolStripMenuItem checkForUpdatesToolStripMenuItem;
        private ToolStripMenuItem uninstallAndCloseToolStripMenuItem;
        private ToolStripMenuItem serviceToolStripMenuItem;
        private ToolStripMenuItem installServiceToolStripMenuItem;
        private ToolStripMenuItem uninstallServiceToolStripMenuItem;
        private ToolStripMenuItem startServiceToolStripMenuItem;
        private ToolStripMenuItem stopServiceToolStripMenuItem;
        private ToolStripMenuItem serviceStatusToolStripMenuItem;
        private ToolStripMenuItem helpToolStripMenuItem;
        private ToolStripMenuItem howToToolStripMenuItem;
        private ToolStripMenuItem aboutToolStripMenuItem;
        private NotifyIcon notifyIcon;
        private ContextMenuStrip trayContextMenu;
        private TabControl tabControl;
        private TabPage tabStatus;
        private TabPage tabConfiguration;
        private TabPage tabFtp;
        private TabPage tabLogs;
        
        // Status Tab Controls
        private Label lblStatus;
        private Button btnStartStandalone;
        private Button btnStopStandalone;
        private Button btnManualProcess;
        private GroupBox grpStandaloneControl;
        
        // Configuration Tab Controls
        private Label lblWatchFolder;
        private TextBox txtWatchFolder;
        private Button btnBrowseWatch;
        private Label lblFtpUploadFolder;
        private TextBox txtFtpUploadFolder;
        private Button btnBrowseFtpUpload;
        private Label lblFtpServer;
        private TextBox txtFtpServer;
        private Label lblFtpPort;
        private NumericUpDown numFtpPort;
        private CheckBox chkAnonymousFtp;
        private Label lblFtpUsername;
        private TextBox txtFtpUsername;
        private Label lblFtpPassword;
        private TextBox txtFtpPassword;
        private Label lblStabilityDelay;
        private NumericUpDown numStabilityDelay;
        private CheckBox chkAutoUpload;
        private CheckBox chkUseExternalProcessor;
        private Label lblExternalProcessorPath;
        private TextBox txtExternalProcessorPath;
        private Button btnBrowseExternalProcessor;
        private Label lblInternalProcessingType;
        private ComboBox cmbInternalProcessingType;
        private Button btnSaveConfig;
        private Button btnTestFtp;
        
        // FTP Tab Controls
        private SplitContainer splitFtp;
        private Panel pnlLocalFiles;
        private Panel pnlRemoteFiles;
        private BrowserContainer browserLocal;
        private BrowserContainer browserRemote;
        private ToolStrip tsLocalNav;
        private ToolStrip tsRemoteNav;
        private ToolStripButton btnLocalBack;
        private ToolStripButton btnLocalForward;
        private ToolStripButton btnRemoteBack;
        private ToolStripButton btnRemoteForward;
        private ToolStripTextBox txtLocalAddress;
        private ToolStripTextBox txtRemoteAddress;
        private Button btnRefreshLocal;
        private Button btnRefreshRemote;
        private Button btnOpenLocalExternal;
        private Button btnOpenRemoteExternal;
        private Label lblLocalPath;
        private Label lblRemotePath;
        private StatusStrip statusFtp;
        private ToolStripStatusLabel lblConnectionStatus;

        // Logs Tab Controls
        private TextBox txtLogs;
        private Button btnClearLogs;
        
        // Bottom Log Preview Panel
        private Panel pnlBottomLog;
        private TextBox txtBottomLog;
        private Label lblBottomLogTitle;
        
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            
            // Main Form
            this.Text = "CNC-FTP-SYNC G-Code Processing Tool";
            this.Size = new Size(800, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormClosing += MainForm_FormClosing;
            this.WindowState = FormWindowState.Minimized;
            this.ShowInTaskbar = false;
            
            // Bottom Log Panel (must be added before tab control for proper docking order)
            this.pnlBottomLog = new Panel();
            this.pnlBottomLog.Height = 120;
            this.pnlBottomLog.Dock = DockStyle.Bottom;
            this.pnlBottomLog.BorderStyle = BorderStyle.FixedSingle;
            this.Controls.Add(this.pnlBottomLog);
            
            this.lblBottomLogTitle = new Label();
            this.lblBottomLogTitle.Text = "Recent Log Messages (Last 20 lines)";
            this.lblBottomLogTitle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            this.lblBottomLogTitle.Location = new Point(5, 5);
            this.lblBottomLogTitle.Size = new Size(300, 15);
            this.pnlBottomLog.Controls.Add(this.lblBottomLogTitle);
            
            this.txtBottomLog = new TextBox();
            this.txtBottomLog.Location = new Point(5, 25);
            this.txtBottomLog.Size = new Size(this.pnlBottomLog.Width - 10, 90);
            this.txtBottomLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.txtBottomLog.Font = new Font("Consolas", 8);
            this.txtBottomLog.Multiline = true;
            this.txtBottomLog.ScrollBars = ScrollBars.Both;
            this.txtBottomLog.ReadOnly = true;
            this.txtBottomLog.BackColor = Color.LightGray;
            this.pnlBottomLog.Controls.Add(this.txtBottomLog);
            
            // MenuStrip
            this.menuStrip = new MenuStrip();
            this.fileToolStripMenuItem = new ToolStripMenuItem();
            this.closeToTrayToolStripMenuItem = new ToolStripMenuItem();
            this.closeFullyToolStripMenuItem = new ToolStripMenuItem();
            this.checkForUpdatesToolStripMenuItem = new ToolStripMenuItem();
            this.howToToolStripMenuItem = new ToolStripMenuItem();
            this.uninstallAndCloseToolStripMenuItem = new ToolStripMenuItem();
            this.serviceToolStripMenuItem = new ToolStripMenuItem();
            this.installServiceToolStripMenuItem = new ToolStripMenuItem();
            this.uninstallServiceToolStripMenuItem = new ToolStripMenuItem();
            this.startServiceToolStripMenuItem = new ToolStripMenuItem();
            this.stopServiceToolStripMenuItem = new ToolStripMenuItem();
            this.serviceStatusToolStripMenuItem = new ToolStripMenuItem();
            
            // NotifyIcon and Tray Context Menu
            this.notifyIcon = new NotifyIcon(this.components);
            this.trayContextMenu = new ContextMenuStrip();
            
            // File Menu
            this.fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            this.fileToolStripMenuItem.Size = new Size(37, 20);
            this.fileToolStripMenuItem.Text = "&File";
            try 
            {
                // Check for null file menu items
                if (this.closeToTrayToolStripMenuItem == null) throw new Exception("closeToTrayToolStripMenuItem is null");
                if (this.closeFullyToolStripMenuItem == null) throw new Exception("closeFullyToolStripMenuItem is null");
                if (this.checkForUpdatesToolStripMenuItem == null) throw new Exception("checkForUpdatesToolStripMenuItem is null");
                if (this.uninstallAndCloseToolStripMenuItem == null) throw new Exception("uninstallAndCloseToolStripMenuItem is null");
                
                this.fileToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] {
                    this.closeToTrayToolStripMenuItem,
                    this.closeFullyToolStripMenuItem,
                    new ToolStripSeparator(),
                    this.uninstallAndCloseToolStripMenuItem});
            }
            catch (Exception ex)
            {
                throw new Exception("Error in File menu AddRange: " + ex.Message, ex);
            }
                
            // Close to Tray Menu Item
            this.closeToTrayToolStripMenuItem.Name = "closeToTrayToolStripMenuItem";
            this.closeToTrayToolStripMenuItem.Size = new Size(200, 22);
            this.closeToTrayToolStripMenuItem.Text = "Close to System &Tray";
            this.closeToTrayToolStripMenuItem.Click += CloseToTray_Click;
            
            // Close Fully Menu Item
            this.closeFullyToolStripMenuItem.Name = "closeFullyToolStripMenuItem";
            this.closeFullyToolStripMenuItem.Size = new Size(200, 22);
            this.closeFullyToolStripMenuItem.Text = "Close &Fully";
            this.closeFullyToolStripMenuItem.Click += CloseFully_Click;
            
            // Check for Updates Menu Item
            this.checkForUpdatesToolStripMenuItem.Name = "checkForUpdatesToolStripMenuItem";
            this.checkForUpdatesToolStripMenuItem.Size = new Size(200, 22);
            this.checkForUpdatesToolStripMenuItem.Text = "Check for &Updates";
            this.checkForUpdatesToolStripMenuItem.Click += CheckForUpdates_Click;
            
            // Uninstall and Close Menu Item
            this.uninstallAndCloseToolStripMenuItem.Name = "uninstallAndCloseToolStripMenuItem";
            this.uninstallAndCloseToolStripMenuItem.Size = new Size(200, 22);
            this.uninstallAndCloseToolStripMenuItem.Text = "&Uninstall Service and Close";
            this.uninstallAndCloseToolStripMenuItem.Click += UninstallAndClose_Click;
            
            // Service Menu
            this.serviceToolStripMenuItem.Name = "serviceToolStripMenuItem";
            this.serviceToolStripMenuItem.Size = new Size(56, 20);
            this.serviceToolStripMenuItem.Text = "&Service";
            try 
            {
                this.serviceToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] {
                    this.installServiceToolStripMenuItem,
                    this.uninstallServiceToolStripMenuItem,
                    new ToolStripSeparator(),
                    this.startServiceToolStripMenuItem,
                    this.stopServiceToolStripMenuItem,
                    this.serviceStatusToolStripMenuItem});
            }
            catch (Exception ex)
            {
                throw new Exception("Error in Service menu AddRange: " + ex.Message, ex);
            }
                
            // Install Service Menu Item
            this.installServiceToolStripMenuItem.Name = "installServiceToolStripMenuItem";
            this.installServiceToolStripMenuItem.Size = new Size(180, 22);
            this.installServiceToolStripMenuItem.Text = "&Install Service";
            this.installServiceToolStripMenuItem.Click += InstallService_Click;
            
            // Uninstall Service Menu Item
            this.uninstallServiceToolStripMenuItem.Name = "uninstallServiceToolStripMenuItem";
            this.uninstallServiceToolStripMenuItem.Size = new Size(180, 22);
            this.uninstallServiceToolStripMenuItem.Text = "&Uninstall Service";
            this.uninstallServiceToolStripMenuItem.Click += UninstallService_Click;
            
            // Start Service Menu Item
            this.startServiceToolStripMenuItem.Name = "startServiceToolStripMenuItem";
            this.startServiceToolStripMenuItem.Size = new Size(180, 22);
            this.startServiceToolStripMenuItem.Text = "&Start Service";
            this.startServiceToolStripMenuItem.Click += StartService_Click;
            
            // Stop Service Menu Item
            this.stopServiceToolStripMenuItem.Name = "stopServiceToolStripMenuItem";
            this.stopServiceToolStripMenuItem.Size = new Size(180, 22);
            this.stopServiceToolStripMenuItem.Text = "S&top Service";
            this.stopServiceToolStripMenuItem.Click += StopService_Click;
            
            // Service Status Menu Item
            this.serviceStatusToolStripMenuItem.Name = "serviceStatusToolStripMenuItem";
            this.serviceStatusToolStripMenuItem.Size = new Size(180, 22);
            this.serviceStatusToolStripMenuItem.Text = "Service &Status";
            this.serviceStatusToolStripMenuItem.Click += ServiceStatus_Click;
            
            // Initialize Help menu first
            InitializeHelpMenu();
            
            // Add menus to MenuStrip
            try 
            {
                // Check for null menu items
                if (this.fileToolStripMenuItem == null) throw new Exception("fileToolStripMenuItem is null");
                if (this.serviceToolStripMenuItem == null) throw new Exception("serviceToolStripMenuItem is null");
                if (this.helpToolStripMenuItem == null) throw new Exception("helpToolStripMenuItem is null");
                
                this.menuStrip.Items.AddRange(new ToolStripItem[] {
                    this.fileToolStripMenuItem,
                    this.serviceToolStripMenuItem,
                    this.helpToolStripMenuItem});
            }
            catch (Exception ex)
            {
                throw new Exception("Error in MenuStrip AddRange: " + ex.Message, ex);
            }
            this.menuStrip.Location = new Point(0, 0);
            this.menuStrip.Name = "menuStrip";
            this.menuStrip.Size = new Size(800, 24);
            this.menuStrip.TabIndex = 0;
            this.menuStrip.Text = "menuStrip";
            
            // Add MenuStrip to form
            // Tab Control
            this.tabControl = new TabControl();
            this.tabControl.Dock = DockStyle.Fill;
            this.Controls.Add(this.tabControl);
            
            this.MainMenuStrip = this.menuStrip;
            this.Controls.Add(this.menuStrip);
            
            // Status Tab
            this.tabStatus = new TabPage("Status");
            this.tabControl.TabPages.Add(this.tabStatus);
            
            this.lblStatus = new Label();
            this.lblStatus.Text = "Status: Stopped";
            this.lblStatus.Font = new Font("Segoe UI", 12, FontStyle.Bold);
            this.lblStatus.Location = new Point(20, 20);
            this.lblStatus.Size = new Size(400, 25);
            this.tabStatus.Controls.Add(this.lblStatus);
            
            // Standalone Control Group
            this.grpStandaloneControl = new GroupBox();
            this.grpStandaloneControl.Text = "Standalone Mode";
            this.grpStandaloneControl.Location = new Point(20, 60);
            this.grpStandaloneControl.Size = new Size(350, 120);
            this.tabStatus.Controls.Add(this.grpStandaloneControl);
            
            this.btnStartStandalone = new Button();
            this.btnStartStandalone.Text = "Start Monitoring";
            this.btnStartStandalone.Location = new Point(20, 30);
            this.btnStartStandalone.Size = new Size(120, 30);
            this.btnStartStandalone.Click += btnStartStandalone_Click;
            this.grpStandaloneControl.Controls.Add(this.btnStartStandalone);
            
            this.btnStopStandalone = new Button();
            this.btnStopStandalone.Text = "Stop Monitoring";
            this.btnStopStandalone.Location = new Point(160, 30);
            this.btnStopStandalone.Size = new Size(120, 30);
            this.btnStopStandalone.Enabled = false;
            this.btnStopStandalone.Click += btnStopStandalone_Click;
            this.grpStandaloneControl.Controls.Add(this.btnStopStandalone);
            
            this.btnManualProcess = new Button();
            this.btnManualProcess.Text = "Manual Process Folder";
            this.btnManualProcess.Location = new Point(20, 70);
            this.btnManualProcess.Size = new Size(260, 30);
            this.btnManualProcess.Click += btnManualProcess_Click;
            this.grpStandaloneControl.Controls.Add(this.btnManualProcess);
            
            // Configuration Tab
            this.tabConfiguration = new TabPage("Configuration");
            this.tabConfiguration.AutoScroll = true;
            this.tabConfiguration.AutoScrollMinSize = new Size(700, 800); // Ensure content area is larger than visible
            this.tabConfiguration.Dock = DockStyle.Fill;
            this.tabControl.TabPages.Add(this.tabConfiguration);
            
            int yPos = 20;
            
            // Watch Folder
            this.lblWatchFolder = new Label();
            this.lblWatchFolder.Text = "Watch Folder:";
            this.lblWatchFolder.Location = new Point(20, yPos);
            this.lblWatchFolder.Size = new Size(100, 20);
            this.tabConfiguration.Controls.Add(this.lblWatchFolder);
            
            this.txtWatchFolder = new TextBox();
            this.txtWatchFolder.Location = new Point(130, yPos);
            this.txtWatchFolder.Size = new Size(500, 20);
            this.tabConfiguration.Controls.Add(this.txtWatchFolder);
            
            this.btnBrowseWatch = new Button();
            this.btnBrowseWatch.Text = "Browse";
            this.btnBrowseWatch.Location = new Point(640, yPos - 2);
            this.btnBrowseWatch.Size = new Size(70, 24);
            this.btnBrowseWatch.Click += btnBrowseWatch_Click;
            this.tabConfiguration.Controls.Add(this.btnBrowseWatch);
            
            yPos += 40;
            
            // FTP Upload Folder
            this.lblFtpUploadFolder = new Label();
            this.lblFtpUploadFolder.Text = "FTP Upload Folder:";
            this.lblFtpUploadFolder.Location = new Point(20, yPos);
            this.lblFtpUploadFolder.Size = new Size(100, 20);
            this.tabConfiguration.Controls.Add(this.lblFtpUploadFolder);
            
            this.txtFtpUploadFolder = new TextBox();
            this.txtFtpUploadFolder.Location = new Point(130, yPos);
            this.txtFtpUploadFolder.Size = new Size(500, 20);
            this.tabConfiguration.Controls.Add(this.txtFtpUploadFolder);
            
            this.btnBrowseFtpUpload = new Button();
            this.btnBrowseFtpUpload.Text = "Browse";
            this.btnBrowseFtpUpload.Location = new Point(640, yPos - 2);
            this.btnBrowseFtpUpload.Size = new Size(70, 24);
            this.btnBrowseFtpUpload.Click += btnBrowseFtpUpload_Click;
            this.tabConfiguration.Controls.Add(this.btnBrowseFtpUpload);
            
            yPos += 40;
            
            // FTP Server
            this.lblFtpServer = new Label();
            this.lblFtpServer.Text = "FTP Server:";
            this.lblFtpServer.Location = new Point(20, yPos);
            this.lblFtpServer.Size = new Size(100, 20);
            this.tabConfiguration.Controls.Add(this.lblFtpServer);
            
            this.txtFtpServer = new TextBox();
            this.txtFtpServer.Location = new Point(130, yPos);
            this.txtFtpServer.Size = new Size(200, 20);
            this.tabConfiguration.Controls.Add(this.txtFtpServer);
            
            // FTP Port
            this.lblFtpPort = new Label();
            this.lblFtpPort.Text = "Port:";
            this.lblFtpPort.Location = new Point(350, yPos);
            this.lblFtpPort.Size = new Size(40, 20);
            this.tabConfiguration.Controls.Add(this.lblFtpPort);
            
            this.numFtpPort = new NumericUpDown();
            this.numFtpPort.Location = new Point(400, yPos);
            this.numFtpPort.Size = new Size(80, 20);
            this.numFtpPort.Minimum = 1;
            this.numFtpPort.Maximum = 65535;
            this.numFtpPort.Value = 21;
            this.tabConfiguration.Controls.Add(this.numFtpPort);
            
            yPos += 40;
            
            // Anonymous FTP
            this.chkAnonymousFtp = new CheckBox();
            this.chkAnonymousFtp.Text = "Use Anonymous FTP";
            this.chkAnonymousFtp.Location = new Point(130, yPos);
            this.chkAnonymousFtp.Size = new Size(150, 20);
            this.chkAnonymousFtp.Checked = true;
            this.chkAnonymousFtp.CheckedChanged += chkAnonymousFtp_CheckedChanged;
            this.tabConfiguration.Controls.Add(this.chkAnonymousFtp);
            
            yPos += 30;
            
            // FTP Username
            this.lblFtpUsername = new Label();
            this.lblFtpUsername.Text = "FTP Username:";
            this.lblFtpUsername.Location = new Point(20, yPos);
            this.lblFtpUsername.Size = new Size(100, 20);
            this.lblFtpUsername.Enabled = false;
            this.tabConfiguration.Controls.Add(this.lblFtpUsername);
            
            this.txtFtpUsername = new TextBox();
            this.txtFtpUsername.Location = new Point(130, yPos);
            this.txtFtpUsername.Size = new Size(200, 20);
            this.txtFtpUsername.Enabled = false;
            this.tabConfiguration.Controls.Add(this.txtFtpUsername);
            
            yPos += 30;
            
            // FTP Password
            this.lblFtpPassword = new Label();
            this.lblFtpPassword.Text = "FTP Password:";
            this.lblFtpPassword.Location = new Point(20, yPos);
            this.lblFtpPassword.Size = new Size(100, 20);
            this.lblFtpPassword.Enabled = false;
            this.tabConfiguration.Controls.Add(this.lblFtpPassword);
            
            this.txtFtpPassword = new TextBox();
            this.txtFtpPassword.Location = new Point(130, yPos);
            this.txtFtpPassword.Size = new Size(200, 20);
            this.txtFtpPassword.UseSystemPasswordChar = true;
            this.txtFtpPassword.Enabled = false;
            this.tabConfiguration.Controls.Add(this.txtFtpPassword);
            
            yPos += 40;
            
            // Stability Delay
            this.lblStabilityDelay = new Label();
            this.lblStabilityDelay.Text = "File Stability Delay (seconds):";
            this.lblStabilityDelay.Location = new Point(20, yPos);
            this.lblStabilityDelay.Size = new Size(160, 20);
            this.tabConfiguration.Controls.Add(this.lblStabilityDelay);
            
            this.numStabilityDelay = new NumericUpDown();
            this.numStabilityDelay.Location = new Point(190, yPos);
            this.numStabilityDelay.Size = new Size(80, 20);
            this.numStabilityDelay.Minimum = 1;
            this.numStabilityDelay.Maximum = 300;
            this.numStabilityDelay.Value = 30;
            this.tabConfiguration.Controls.Add(this.numStabilityDelay);
            
            yPos += 40;
            
            // Auto Upload
            this.chkAutoUpload = new CheckBox();
            this.chkAutoUpload.Text = "Auto upload after processing";
            this.chkAutoUpload.Location = new Point(20, yPos);
            this.chkAutoUpload.Size = new Size(200, 20);
            this.chkAutoUpload.Checked = true;
            this.tabConfiguration.Controls.Add(this.chkAutoUpload);
            
            yPos += 40;
            
            // Use External Processor
            this.chkUseExternalProcessor = new CheckBox();
            this.chkUseExternalProcessor.Text = "Use external script for processing (instead of built-in processing)";
            this.chkUseExternalProcessor.Location = new Point(20, yPos);
            this.chkUseExternalProcessor.Size = new Size(400, 20);
            this.chkUseExternalProcessor.Checked = false;
            this.chkUseExternalProcessor.CheckedChanged += chkUseExternalProcessor_CheckedChanged;
            this.tabConfiguration.Controls.Add(this.chkUseExternalProcessor);
            
            yPos += 30;
            
            // External Processor Path
            this.lblExternalProcessorPath = new Label();
            this.lblExternalProcessorPath.Text = "External Script Path:";
            this.lblExternalProcessorPath.Location = new Point(20, yPos);
            this.lblExternalProcessorPath.Size = new Size(120, 20);
            this.lblExternalProcessorPath.Enabled = false;
            this.tabConfiguration.Controls.Add(this.lblExternalProcessorPath);
            
            this.txtExternalProcessorPath = new TextBox();
            this.txtExternalProcessorPath.Location = new Point(150, yPos);
            this.txtExternalProcessorPath.Size = new Size(480, 20);
            this.txtExternalProcessorPath.Enabled = false;
            this.tabConfiguration.Controls.Add(this.txtExternalProcessorPath);
            
            this.btnBrowseExternalProcessor = new Button();
            this.btnBrowseExternalProcessor.Text = "Browse";
            this.btnBrowseExternalProcessor.Location = new Point(640, yPos - 2);
            this.btnBrowseExternalProcessor.Size = new Size(70, 24);
            this.btnBrowseExternalProcessor.Enabled = false;
            this.btnBrowseExternalProcessor.Click += btnBrowseExternalProcessor_Click;
            this.tabConfiguration.Controls.Add(this.btnBrowseExternalProcessor);
            
            yPos += 40;
            
            // Internal Processing Type
            this.lblInternalProcessingType = new Label();
            this.lblInternalProcessingType.Text = "Internal Processing Type:";
            this.lblInternalProcessingType.Location = new Point(20, yPos);
            this.lblInternalProcessingType.Size = new Size(140, 20);
            this.tabConfiguration.Controls.Add(this.lblInternalProcessingType);
            
            this.cmbInternalProcessingType = new ComboBox();
            this.cmbInternalProcessingType.Location = new Point(170, yPos);
            this.cmbInternalProcessingType.Size = new Size(300, 20);
            this.cmbInternalProcessingType.DropDownStyle = ComboBoxStyle.DropDownList;
            this.cmbInternalProcessingType.Items.Add("Mozaik=>SyntecLabel+CNC");
            this.cmbInternalProcessingType.Items.Add("Mozaik=>SyntecLabel+CNC (CYC Coordinate Update)");
            this.cmbInternalProcessingType.Items.Add("Simple FTP Upload");
            this.cmbInternalProcessingType.SelectedIndex = 0; // Default to first option
            this.tabConfiguration.Controls.Add(this.cmbInternalProcessingType);
            
            yPos += 40;
            
            // Save Config Button
            this.btnSaveConfig = new Button();
            this.btnSaveConfig.Text = "Save Configuration";
            this.btnSaveConfig.Location = new Point(20, yPos);
            this.btnSaveConfig.Size = new Size(150, 30);
            this.btnSaveConfig.Click += btnSaveConfig_Click;
            this.tabConfiguration.Controls.Add(this.btnSaveConfig);
            
            // Test FTP Button
            this.btnTestFtp = new Button();
            this.btnTestFtp.Text = "Test FTP Connection";
            this.btnTestFtp.Location = new Point(190, yPos);
            this.btnTestFtp.Size = new Size(150, 30);
            this.btnTestFtp.Click += btnTestFtp_Click;
            this.tabConfiguration.Controls.Add(this.btnTestFtp);
            
            // FTP Tab
            this.tabFtp = new TabPage("FTP Browser");
            this.tabFtp.AutoScroll = true;
            this.tabControl.TabPages.Add(this.tabFtp);
            
            // Split container for dual panes
            this.splitFtp = new SplitContainer();
            this.splitFtp.Dock = DockStyle.Fill;
            this.splitFtp.Panel1.BackColor = SystemColors.Control;
            this.splitFtp.Panel2.BackColor = SystemColors.Control;
            this.tabFtp.Controls.Add(this.splitFtp);
            
            // Local files panel (left side)
            this.pnlLocalFiles = new Panel();
            this.pnlLocalFiles.Dock = DockStyle.Fill;
            this.pnlLocalFiles.Padding = new Padding(5);
            this.splitFtp.Panel1.Controls.Add(this.pnlLocalFiles);
            
            // Local files header
            this.lblLocalPath = new Label();
            this.lblLocalPath.Text = "Local Files - FTP Upload Folder";
            this.lblLocalPath.Dock = DockStyle.Top;
            this.lblLocalPath.Height = 25;
            this.lblLocalPath.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            this.pnlLocalFiles.Controls.Add(this.lblLocalPath);
            
            // Local files buttons panel
            var pnlLocalButtons = new Panel();
            pnlLocalButtons.Dock = DockStyle.Top;
            pnlLocalButtons.Height = 35;
            this.pnlLocalFiles.Controls.Add(pnlLocalButtons);
            
            this.btnRefreshLocal = new Button();
            this.btnRefreshLocal.Text = "Refresh";
            this.btnRefreshLocal.Size = new Size(70, 25);
            this.btnRefreshLocal.Location = new Point(5, 5);
            pnlLocalButtons.Controls.Add(this.btnRefreshLocal);
            
            this.btnOpenLocalExternal = new Button();
            this.btnOpenLocalExternal.Text = "Open External";
            this.btnOpenLocalExternal.Size = new Size(90, 25);
            this.btnOpenLocalExternal.Location = new Point(80, 5);
            pnlLocalButtons.Controls.Add(this.btnOpenLocalExternal);
            
            // Local navigation toolbar
            this.tsLocalNav = new ToolStrip();
            this.tsLocalNav.Dock = DockStyle.Top;
            this.tsLocalNav.GripStyle = ToolStripGripStyle.Hidden;
            
            this.btnLocalBack = new ToolStripButton("←");
            this.btnLocalBack.ToolTipText = "Back";
            this.btnLocalBack.Click += BtnLocalBack_Click;
            this.tsLocalNav.Items.Add(this.btnLocalBack);
            
            this.btnLocalForward = new ToolStripButton("→");
            this.btnLocalForward.ToolTipText = "Forward";
            this.btnLocalForward.Click += BtnLocalForward_Click;
            this.tsLocalNav.Items.Add(this.btnLocalForward);
            
            this.tsLocalNav.Items.Add(new ToolStripSeparator());
            
            this.txtLocalAddress = new ToolStripTextBox();
            this.txtLocalAddress.Size = new Size(400, 25);
            this.txtLocalAddress.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) NavigateLocalTo(txtLocalAddress.Text); };
            this.tsLocalNav.Items.Add(new ToolStripLabel("Address:"));
            this.tsLocalNav.Items.Add(this.txtLocalAddress);
            
            this.pnlLocalFiles.Controls.Add(this.tsLocalNav);
            
            // Local files browser - positioned below other controls with explicit spacing
            this.browserLocal = new BrowserContainer();
            this.browserLocal.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            this.browserLocal.Location = new Point(5, 95);  // Position below toolbar/buttons with 95px top margin
            this.browserLocal.Size = new Size(this.pnlLocalFiles.Width - 10, this.pnlLocalFiles.Height - 100);
            this.pnlLocalFiles.Controls.Add(this.browserLocal);
            
            // Remote files panel (right side)
            this.pnlRemoteFiles = new Panel();
            this.pnlRemoteFiles.Dock = DockStyle.Fill;
            this.pnlRemoteFiles.Padding = new Padding(5);
            this.splitFtp.Panel2.Controls.Add(this.pnlRemoteFiles);
            
            // Remote files header
            this.lblRemotePath = new Label();
            this.lblRemotePath.Text = "Remote FTP Server";
            this.lblRemotePath.Dock = DockStyle.Top;
            this.lblRemotePath.Height = 25;
            this.lblRemotePath.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            this.pnlRemoteFiles.Controls.Add(this.lblRemotePath);
            
            // Remote files buttons panel
            var pnlRemoteButtons = new Panel();
            pnlRemoteButtons.Dock = DockStyle.Top;
            pnlRemoteButtons.Height = 35;
            this.pnlRemoteFiles.Controls.Add(pnlRemoteButtons);
            
            this.btnRefreshRemote = new Button();
            this.btnRefreshRemote.Text = "Refresh";
            this.btnRefreshRemote.Size = new Size(70, 25);
            this.btnRefreshRemote.Location = new Point(5, 5);
            pnlRemoteButtons.Controls.Add(this.btnRefreshRemote);
            
            this.btnOpenRemoteExternal = new Button();
            this.btnOpenRemoteExternal.Text = "Open External";
            this.btnOpenRemoteExternal.Size = new Size(90, 25);
            this.btnOpenRemoteExternal.Location = new Point(80, 5);
            pnlRemoteButtons.Controls.Add(this.btnOpenRemoteExternal);
            
            // Remote navigation toolbar
            this.tsRemoteNav = new ToolStrip();
            this.tsRemoteNav.Dock = DockStyle.Top;
            this.tsRemoteNav.GripStyle = ToolStripGripStyle.Hidden;
            
            this.btnRemoteBack = new ToolStripButton("←");
            this.btnRemoteBack.ToolTipText = "Back";
            this.btnRemoteBack.Click += BtnRemoteBack_Click;
            this.tsRemoteNav.Items.Add(this.btnRemoteBack);
            
            this.btnRemoteForward = new ToolStripButton("→");
            this.btnRemoteForward.ToolTipText = "Forward";
            this.btnRemoteForward.Click += BtnRemoteForward_Click;
            this.tsRemoteNav.Items.Add(this.btnRemoteForward);
            
            this.tsRemoteNav.Items.Add(new ToolStripSeparator());
            
            this.txtRemoteAddress = new ToolStripTextBox();
            this.txtRemoteAddress.Size = new Size(400, 25);
            this.txtRemoteAddress.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) NavigateRemoteTo(txtRemoteAddress.Text); };
            this.tsRemoteNav.Items.Add(new ToolStripLabel("Address:"));
            this.tsRemoteNav.Items.Add(this.txtRemoteAddress);
            
            this.pnlRemoteFiles.Controls.Add(this.tsRemoteNav);
            
            // Remote files browser - positioned below other controls with explicit spacing
            this.browserRemote = new BrowserContainer();
            this.browserRemote.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            this.browserRemote.Location = new Point(5, 95);  // Position below toolbar/buttons with 95px top margin
            this.browserRemote.Size = new Size(this.pnlRemoteFiles.Width - 10, this.pnlRemoteFiles.Height - 100);
            this.pnlRemoteFiles.Controls.Add(this.browserRemote);
            
            // Status bar for FTP tab
            this.statusFtp = new StatusStrip();
            this.statusFtp.Dock = DockStyle.Bottom;
            this.lblConnectionStatus = new ToolStripStatusLabel();
            this.lblConnectionStatus.Text = "Not connected";
            this.statusFtp.Items.Add(this.lblConnectionStatus);
            this.tabFtp.Controls.Add(this.statusFtp);
            
            // Logs Tab
            this.tabLogs = new TabPage("Logs");
            this.tabControl.TabPages.Add(this.tabLogs);
            
            // Clear Logs Button
            this.btnClearLogs = new Button();
            this.btnClearLogs.Text = "Clear Logs";
            this.btnClearLogs.Size = new Size(100, 30);
            this.btnClearLogs.Location = new Point(10, 10);
            this.btnClearLogs.Click += new EventHandler(this.btnClearLogs_Click);
            this.tabLogs.Controls.Add(this.btnClearLogs);
            
            this.txtLogs = new TextBox();
            this.txtLogs.Location = new Point(10, 50);
            this.txtLogs.Size = new Size(this.tabLogs.Width - 20, this.tabLogs.Height - 60);
            this.txtLogs.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.txtLogs.Font = new Font("Consolas", 9);
            this.txtLogs.Multiline = true;
            this.txtLogs.WordWrap = true;
            this.txtLogs.ScrollBars = ScrollBars.Both;
            this.txtLogs.ReadOnly = true;
            this.txtLogs.BackColor = Color.White;
            this.tabLogs.Controls.Add(this.txtLogs);
            
            // NotifyIcon Configuration
            this.notifyIcon.Text = "CNC-FTP-SYNC Service Manager";
            this.notifyIcon.Icon = this.Icon; // Use the form's icon
            this.notifyIcon.Visible = false;
            this.notifyIcon.MouseClick += NotifyIcon_MouseClick;
            
            // Tray Context Menu
            var trayShowMenuItem = new ToolStripMenuItem("&Show", null, TrayShow_Click);
            var trayInstallMenuItem = new ToolStripMenuItem("&Install Service", null, InstallService_Click);
            var trayUninstallMenuItem = new ToolStripMenuItem("&Uninstall Service", null, UninstallService_Click);
            var trayStartMenuItem = new ToolStripMenuItem("&Start Service", null, StartService_Click);
            var trayStopMenuItem = new ToolStripMenuItem("S&top Service", null, StopService_Click);
            var trayCheckUpdatesMenuItem = new ToolStripMenuItem("Check for &Updates", null, CheckForUpdates_Click);
            var trayExitMenuItem = new ToolStripMenuItem("E&xit", null, CloseFully_Click);
            
            try 
            {
                this.trayContextMenu.Items.AddRange(new ToolStripItem[] {
                    trayShowMenuItem,
                    new ToolStripSeparator(),
                    trayInstallMenuItem,
                    trayUninstallMenuItem,
                    new ToolStripSeparator(),
                    trayStartMenuItem,
                    trayStopMenuItem,
                    new ToolStripSeparator(),
                    trayCheckUpdatesMenuItem,
                    new ToolStripSeparator(),
                    trayExitMenuItem
                });
            }
            catch (Exception ex)
            {
                throw new Exception("Error in Tray context menu AddRange: " + ex.Message, ex);
            }
            
            this.notifyIcon.ContextMenuStrip = this.trayContextMenu;
        }
        
        private void InitializeHelpMenu()
        {
            try 
            {
                // Help menu
                this.helpToolStripMenuItem = new ToolStripMenuItem();
                this.helpToolStripMenuItem.Name = "helpToolStripMenuItem";
                this.helpToolStripMenuItem.Size = new Size(44, 20);
                this.helpToolStripMenuItem.Text = "&Help";
                
                // How To menu item
                this.howToToolStripMenuItem = new ToolStripMenuItem();
                this.howToToolStripMenuItem.Name = "howToToolStripMenuItem";
                this.howToToolStripMenuItem.Size = new Size(152, 22);
                this.howToToolStripMenuItem.Text = "&How To Guide...";
                this.howToToolStripMenuItem.Click += new EventHandler(this.HowTo_Click);
                
                // About menu item
                this.aboutToolStripMenuItem = new ToolStripMenuItem();
                this.aboutToolStripMenuItem.Name = "aboutToolStripMenuItem";
                this.aboutToolStripMenuItem.Size = new Size(152, 22);
                this.aboutToolStripMenuItem.Text = "&About CNC-FTP-SYNC...";
                this.aboutToolStripMenuItem.Click += new EventHandler(this.About_Click);
                
                // Add items to Help menu
                if (this.howToToolStripMenuItem == null) throw new Exception("howToToolStripMenuItem is null before Add");
                if (this.aboutToolStripMenuItem == null) throw new Exception("aboutToolStripMenuItem is null before Add");
                if (this.checkForUpdatesToolStripMenuItem == null) throw new Exception("checkForUpdatesToolStripMenuItem is null before Add");
                this.helpToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] {
                    this.howToToolStripMenuItem,
                    new ToolStripSeparator(),
                    this.checkForUpdatesToolStripMenuItem,
                    new ToolStripSeparator(),
                    this.aboutToolStripMenuItem});
            }
            catch (Exception ex)
            {
                throw new Exception("Error in InitializeHelpMenu: " + ex.Message, ex);
            }
        }
    }
}