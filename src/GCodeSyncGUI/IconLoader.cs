using System;
using System.Drawing;
using System.IO;

namespace GCodeSyncGUI.Resources
{
    public static class IconLoader
    {
        public static Icon LoadApplicationIcon()
        {
            try
            {
                // Look for CBWSS-Logo.png in the application directory or parent directories
                var currentDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? Environment.CurrentDirectory;
                
                // Search locations for the logo
                string[] searchPaths = {
                    Path.Combine(currentDir, "CBWSS-Logo.png"),                    // Same directory as exe
                    Path.Combine(Path.GetDirectoryName(currentDir) ?? currentDir, "CBWSS-Logo.png"),  // Parent directory 
                    Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(currentDir) ?? currentDir) ?? currentDir, "CBWSS-Logo.png"), // Grandparent
                    Path.Combine(Environment.CurrentDirectory, "CBWSS-Logo.png"),  // Working directory
                };

                foreach (var path in searchPaths)
                {
                    if (File.Exists(path))
                    {
                        using (var bitmap = new Bitmap(path))
                        {
                            return Icon.FromHandle(bitmap.GetHicon());
                        }
                    }
                }
                
                // Fallback to default icon if logo not found
                return SystemIcons.Application;
            }
            catch
            {
                return SystemIcons.Application;
            }
        }
    }
}