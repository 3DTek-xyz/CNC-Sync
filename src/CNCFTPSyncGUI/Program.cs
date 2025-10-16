using NLog; 

namespace CNCFTPSyncGUI
{
    internal static class Program
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        [STAThread]
        static void Main()
        {
            try
            {
                // Configure shared log directory in ProgramData (same as service)
                var sharedDataDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "CNC-FTP-SYNC"
                );
                var logDirectory = Path.Combine(sharedDataDirectory, "Logs");
                Directory.CreateDirectory(logDirectory);
                
                // Ensure NLog configuration is loaded
                if (LogManager.Configuration == null)
                {
                    var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NLog.config");
                    if (File.Exists(configPath))
                    {
                        LogManager.Configuration = new NLog.Config.XmlLoggingConfiguration(configPath);
                    }
                    else
                    {
                        // Create basic configuration if config file is missing
                        var config = new NLog.Config.LoggingConfiguration();
                        var fileTarget = new NLog.Targets.FileTarget("fileTarget")
                        {
                            FileName = Path.Combine(logDirectory, "CNCFTPSyncGUI-${shortdate}.log"),
                            Layout = "${longdate} ${uppercase:${level}} ${logger} ${message} ${exception:format=tostring}"
                        };
                        config.AddTarget(fileTarget);
                        config.AddRuleForAllLevels(fileTarget);
                        LogManager.Configuration = config;
                    }
                }
                
                if (LogManager.Configuration?.Variables != null)
                {
                    LogManager.Configuration.Variables["logDirectory"] = logDirectory;
                }

                Logger.Info("Starting CNC-FTP-SYNC GUI application");
                Logger.Info($"Log directory: {logDirectory}");

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetHighDpiMode(HighDpiMode.SystemAware);

                // Ensure only one instance runs
                using var mutex = new Mutex(true, "CNCFTPSyncGUI", out bool createdNew);
                
                if (!createdNew)
                {
                    MessageBox.Show("CNC-FTP-SYNC GUI is already running. Check the system tray.", 
                        "Already Running", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                try
                {
                    Logger.Error(ex, "Fatal error in GUI application");
                }
                catch
                {
                    // If logging fails, still show the message box
                }
                MessageBox.Show($"Fatal application error: {ex.Message}\n\nStack trace:\n{ex.StackTrace}", "Fatal Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                LogManager.Shutdown();
            }
        }
    }
}