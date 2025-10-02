using NLog;

namespace GCodeSyncGUI
{
    internal static class Program
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        [STAThread]
        static void Main()
        {
            try
            {
                Logger.Info("Starting G-Code Sync GUI application");

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetHighDpiMode(HighDpiMode.SystemAware);

                // Ensure only one instance runs
                using var mutex = new Mutex(true, "GCodeSyncGUI", out bool createdNew);
                
                if (!createdNew)
                {
                    MessageBox.Show("G-Code Sync GUI is already running. Check the system tray.", 
                        "Already Running", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Fatal error in GUI application");
                MessageBox.Show($"Fatal application error: {ex.Message}", "Fatal Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                LogManager.Shutdown();
            }
        }
    }
}