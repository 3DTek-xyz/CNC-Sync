namespace GCodeSyncGUI
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;
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
        private GroupBox grpServiceControl;
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
            this.Text = "CBWSS G-Code Sync Tool";
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
            
            // Tab Control
            this.tabControl = new TabControl();
            this.tabControl.Dock = DockStyle.Fill;
            this.Controls.Add(this.tabControl);
            
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
        }
    }
}