using System;
using System.Windows.Forms;
using NLog;

namespace CNCFTPSyncGUI
{
    public static class SafeMessageBox
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Shows a message box safely, handling service context where interactive dialogs are not allowed
        /// </summary>
        public static DialogResult Show(string text, string caption = "CNC-FTP-SYNC", 
            MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.Information)
        {
            try
            {
                // Check if we're running in an interactive user session
                if (Environment.UserInteractive && !IsRunningAsService())
                {
                    return MessageBox.Show(text, caption, buttons, icon);
                }
                else
                {
                    // Running as service or non-interactive - log instead of showing dialog
                    var logLevel = icon switch
                    {
                        MessageBoxIcon.Error => LogLevel.Error,
                        MessageBoxIcon.Warning => LogLevel.Warn,
                        _ => LogLevel.Info
                    };
                    
                    Logger.Log(logLevel, $"[GUI Dialog] {caption}: {text}");
                    
                    // For service context, we can try ServiceNotification style for critical messages
                    if (icon == MessageBoxIcon.Error || icon == MessageBoxIcon.Warning)
                    {
                        try
                        {
                            return MessageBox.Show(text, caption, buttons, icon, 
                                MessageBoxDefaultButton.Button1, MessageBoxOptions.ServiceNotification);
                        }
                        catch
                        {
                            // If even ServiceNotification fails, just log and return OK
                            Logger.Error($"Failed to show service notification dialog: {caption}: {text}");
                        }
                    }
                    
                    // Default return value based on buttons
                    return buttons switch
                    {
                        MessageBoxButtons.YesNo => DialogResult.No,
                        MessageBoxButtons.YesNoCancel => DialogResult.Cancel,
                        MessageBoxButtons.OKCancel => DialogResult.Cancel,
                        _ => DialogResult.OK
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error showing message box: {caption}: {text}");
                return DialogResult.OK;
            }
        }

        /// <summary>
        /// Checks if the application is running as a Windows Service
        /// </summary>
        private static bool IsRunningAsService()
        {
            try
            {
                // Services typically have no console window
                return Console.IsInputRedirected || !Environment.UserInteractive;
            }
            catch
            {
                return false;
            }
        }
    }
}