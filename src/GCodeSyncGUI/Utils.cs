using System;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Microsoft.VisualBasic.FileIO;
using System.Collections.Generic;
using System.Diagnostics;

namespace GCodeSyncGUI
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    public static class TimeHelper
{
    public static string FormatTime(DateTime time)
    {
        return time.ToString("HH:mm:ss");
    }
    
    public static string FormatDateTime(DateTime dateTime)
    {
        return dateTime.ToString("dd/MM/yyyy HH:mm");
    }
}

public static class ShellContextMenu
{
    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern uint TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("shell32.dll")]
    private static extern IntPtr ILCreateFromPath([MarshalAs(UnmanagedType.LPWStr)] string pszPath);

    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);

    public static void ShowContextMenu(Control parent, string[] filePaths, Point location)
    {
        try
        {
            if (filePaths == null || filePaths.Length == 0)
                return;

            // For now, just show a basic context menu
            // Full shell context menu implementation would be more complex
            var contextMenu = new ContextMenuStrip();
            
            contextMenu.Items.Add("Copy", null, (s, e) => CopyToClipboard(filePaths));
            contextMenu.Items.Add("Properties", null, (s, e) => ShowProperties(filePaths[0]));
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Delete", null, (s, e) => DeleteFiles(filePaths));
            
            contextMenu.Show(parent, location);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Context menu error: {ex.Message}");
        }
    }

    private static void CopyToClipboard(string[] filePaths)
    {
        try
        {
            var fileCollection = new System.Collections.Specialized.StringCollection();
            fileCollection.AddRange(filePaths);
            Clipboard.SetFileDropList(fileCollection);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error copying files: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void ShowProperties(string filePath)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{filePath}\"",
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error showing properties: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void DeleteFiles(string[] filePaths)
    {
        try
        {
            var result = MessageBox.Show($"Are you sure you want to delete {filePaths.Length} item(s)?", 
                "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            
            if (result == DialogResult.Yes)
            {
                foreach (var path in filePaths)
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                    else if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error deleting files: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}public static class FileSizeHelper
{
    public static string FormatSize(long size)
    {
        string[] sizes = { "bytes", "KB", "MB", "GB", "TB" };
        int order = 0;
        double formattedSize = size;
        
        while (formattedSize >= 1024 && order < sizes.Length - 1)
        {
            order++;
            formattedSize /= 1024;
        }
        
        return $"{formattedSize:0.##} {sizes[order]}";
    }
}

public class FileSystemHelper
{
    public static void OperateFileSystemItem(string sourceName, string destinationName, DragDropEffects effect)
    {
        try
        {
            if (effect == DragDropEffects.Move)
            {
                if (File.Exists(sourceName))
                {
                    FileSystem.MoveFile(sourceName, destinationName, UIOption.AllDialogs, UICancelOption.DoNothing);
                }
                else if (Directory.Exists(sourceName))
                {
                    FileSystem.MoveDirectory(sourceName, destinationName, UIOption.AllDialogs, UICancelOption.DoNothing);
                }
            }
            else if (effect == DragDropEffects.Copy)
            {
                if (File.Exists(sourceName))
                {
                    FileSystem.CopyFile(sourceName, destinationName, UIOption.AllDialogs, UICancelOption.DoNothing);
                }
                else if (Directory.Exists(sourceName))
                {
                    FileSystem.CopyDirectory(sourceName, destinationName, UIOption.AllDialogs, UICancelOption.DoNothing);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"File operation failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

public class ShellInfoHelper
{
    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_LARGEICON = 0x0;
    private const uint SHGFI_SMALLICON = 0x1;
    private const uint SHGFI_DISPLAYNAME = 0x200;
    private const uint SHGFI_TYPENAME = 0x400;

    public static Icon GetIconFromPath(string path)
    {
        try
        {
            SHFILEINFO shinfo = new SHFILEINFO();
            IntPtr hImgSmall = SHGetFileInfo(path, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_SMALLICON);
            
            if (shinfo.hIcon != IntPtr.Zero)
            {
                Icon icon = Icon.FromHandle(shinfo.hIcon);
                return icon;
            }
        }
        catch (Exception)
        {
            // Return default icon if failed
        }
        
        return SystemIcons.Application;
    }

    public static string GetDisplayName(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path)) return "";
            
            SHFILEINFO shinfo = new SHFILEINFO();
            IntPtr result = SHGetFileInfo(path, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_DISPLAYNAME);
            
            if (result != IntPtr.Zero && !string.IsNullOrEmpty(shinfo.szDisplayName))
            {
                return shinfo.szDisplayName;
            }
            
            return Path.GetFileName(path);
        }
        catch (Exception)
        {
            return Path.GetFileName(path) ?? path;
        }
    }

    public static string GetTypeName(string path)
    {
        try
        {
            SHFILEINFO shinfo = new SHFILEINFO();
            IntPtr result = SHGetFileInfo(path, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_TYPENAME);
            
            if (result != IntPtr.Zero && !string.IsNullOrEmpty(shinfo.szTypeName))
            {
                return shinfo.szTypeName;
            }
            
            if (Directory.Exists(path))
                return "Folder";
            
            string ext = Path.GetExtension(path);
            return string.IsNullOrEmpty(ext) ? "File" : $"{ext.ToUpper().TrimStart('.')} File";
        }
        catch (Exception)
        {
            return Directory.Exists(path) ? "Folder" : "File";
        }
    }

    public static string GetExactPathName(string pathName)
    {
        try
        {
            if (!(File.Exists(pathName) || Directory.Exists(pathName)))
                return pathName;

            DirectoryInfo di = new DirectoryInfo(pathName);
            if (di.Parent != null)
            {
                return Path.Combine(
                    GetExactPathName(di.Parent.FullName),
                    di.Parent.GetFileSystemInfos(di.Name)[0].Name);
            }
            else
            {
                return di.Name.ToUpper();
            }
        }
        catch (Exception)
        {
            return pathName;
        }
    }

    public static string GetDownloadFolderPath()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads";
    }
}
}