using System;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
using System.ComponentModel;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Timers;
using System.Threading.Tasks;

namespace GCodeSyncGUI
{
    public class ListViewBrowser : ListView
    {
        public new BrowserContainer? Container { get => Parent?.Parent as BrowserContainer; }
        public string DirPath { get => Container?.DirPath ?? ""; }
        
        public ListViewBrowser()
        {
            View = View.Details;
            AllowDrop = true;
            LabelEdit = true;
            Margin = new Padding(5);
            Dock = DockStyle.Fill;
            FullRowSelect = true;
            GridLines = true;
            HideSelection = false;
            HeaderStyle = ColumnHeaderStyle.Clickable;
            Scrollable = true;
            MultiSelect = true;
            
            // Set up event handlers
            DragEnter += new DragEventHandler((object? sender, DragEventArgs e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effect = DragDropEffects.Copy;
                }
                Focus();
            });
            
            DragOver += new DragEventHandler((object? sender, DragEventArgs e) =>
            {
                Focus();
            });
            
            // Add right-click context menu support
            MouseUp += new MouseEventHandler((object? sender, MouseEventArgs e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    ShowShellContextMenu(e.Location);
                }
            });
        }
        
        private void ShowShellContextMenu(Point location)
        {
            try
            {
                var selectedItems = SelectedItems;
                
                // Check if this is an FTP browser (temp directory for FTP content)
                bool isFtpBrowser = !string.IsNullOrEmpty(DirPath) && DirPath.Contains("GCodeSync_FTP_Browser");
                
                if (isFtpBrowser)
                {
                    ShowFtpContextMenu(location);
                }
                else
                {
                    // For local files/folders, show custom context menu with upload options
                    ShowLocalContextMenu(location);
                }
            }
            catch (Exception ex)
            {
                // Log error but don't show message box for context menu issues
                System.Diagnostics.Debug.WriteLine($"Context menu error: {ex.Message}");
            }
        }
        
        private void ShowLocalContextMenu(Point location)
        {
            try
            {
                var selectedItems = SelectedItems;
                var contextMenu = new ContextMenuStrip();
                
                if (selectedItems.Count > 0)
                {
                    // Add upload options for selected items
                    foreach (ListViewItem item in selectedItems)
                    {
                        if (!string.IsNullOrEmpty(item.Name) && Directory.Exists(item.Name))
                        {
                            // This is a folder - add upload folder option
                            var folderName = Path.GetFileName(item.Name);
                            contextMenu.Items.Add($"Upload Folder '{folderName}' to FTP", null, (s, e) => UploadFolderToFtp(item.Name));
                        }
                        else if (!string.IsNullOrEmpty(item.Name) && File.Exists(item.Name))
                        {
                            // This is a file - add upload file option
                            var fileName = Path.GetFileName(item.Name);
                            contextMenu.Items.Add($"Upload File '{fileName}' to FTP", null, (s, e) => UploadFileToFtp(item.Name));
                        }
                    }
                    
                    if (contextMenu.Items.Count > 0)
                    {
                        contextMenu.Items.Add("-"); // Separator
                    }
                }
                
                // Add standard shell context menu items
                contextMenu.Items.Add("Show Windows Context Menu", null, (s, e) => ShowStandardShellMenu(location));
                
                if (contextMenu.Items.Count > 0)
                {
                    contextMenu.Show(this, location);
                }
                else
                {
                    // Fallback to standard shell menu
                    ShowStandardShellMenu(location);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Local context menu error: {ex.Message}");
                // Fallback to standard shell menu
                ShowStandardShellMenu(location);
            }
        }
        
        private void ShowStandardShellMenu(Point location)
        {
            try
            {
                var selectedItems = SelectedItems;
                
                if (selectedItems.Count == 0 && !string.IsNullOrEmpty(DirPath))
                {
                    // Show context menu for the directory itself
                    ShellContextMenu.ShowContextMenu(this, new string[] { DirPath }, location);
                }
                else if (selectedItems.Count > 0)
                {
                    // Show context menu for selected items
                    var itemPaths = new List<string>();
                    foreach (ListViewItem item in selectedItems)
                    {
                        if (!string.IsNullOrEmpty(item.Name))
                        {
                            itemPaths.Add(item.Name);
                        }
                    }
                    if (itemPaths.Count > 0)
                    {
                        ShellContextMenu.ShowContextMenu(this, itemPaths.ToArray(), location);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Standard shell context menu error: {ex.Message}");
            }
        }

        private void ShowFtpContextMenu(Point location)
        {
            try
            {
                var contextMenu = new ContextMenuStrip();
                var selectedItems = SelectedItems;
                
                if (selectedItems.Count > 0)
                {
                    // Add FTP-specific menu items for selected files/folders
                    foreach (ListViewItem item in selectedItems)
                    {
                        // Skip the ".." parent directory item and info files
                        if (item.Text == ".." || item.Text.StartsWith("0_FTP_CONNECTION_INFO") || 
                            item.Text.Contains("_FTP_FOLDER_INFO") || item.Text.Contains("_PARENT_DIR"))
                            continue;
                            
                        // Add context menu items for real FTP items
                        if (selectedItems.Count == 1)
                        {
                            contextMenu.Items.Add($"Download '{item.Text}'", null, (s, e) => DownloadFtpItem(item));
                            contextMenu.Items.Add($"Properties of '{item.Text}'", null, (s, e) => ShowFtpProperties(item));
                            contextMenu.Items.Add("-"); // Separator
                            contextMenu.Items.Add($"Delete '{item.Text}'", null, (s, e) => DeleteFtpItem(item));
                        }
                        break; // Only process first item for menu text
                    }
                    
                    if (selectedItems.Count > 1)
                    {
                        contextMenu.Items.Add($"Download {selectedItems.Count} items", null, (s, e) => DownloadMultipleFtpItems());
                        contextMenu.Items.Add("-");
                        contextMenu.Items.Add($"Delete {selectedItems.Count} items", null, (s, e) => DeleteMultipleFtpItems());
                    }
                }
                
                // Add general FTP menu items
                if (contextMenu.Items.Count > 0)
                    contextMenu.Items.Add("-");
                    
                contextMenu.Items.Add("Refresh FTP Directory", null, (s, e) => RefreshFtpDirectory());
                contextMenu.Items.Add("New Folder...", null, (s, e) => CreateFtpFolder());
                
                if (contextMenu.Items.Count > 0)
                {
                    contextMenu.Show(this, location);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error showing FTP context menu: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private async void DownloadFtpItem(ListViewItem item)
        {
            try
            {
                using (var folderDialog = new FolderBrowserDialog())
                {
                    folderDialog.Description = "Select folder to download file to:";
                    folderDialog.ShowNewFolderButton = true;
                    
                    if (folderDialog.ShowDialog() == DialogResult.OK)
                    {
                        var mainForm = FindForm() as MainForm;
                        if (mainForm != null)
                        {
                            var ftpServiceField = mainForm.GetType().GetField("_ftpService", 
                                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            var currentPathField = mainForm.GetType().GetField("_currentRemoteFtpPath", 
                                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            
                            if (ftpServiceField?.GetValue(mainForm) is GCodeSyncCore.Services.FtpService ftpService &&
                                currentPathField?.GetValue(mainForm) is string currentPath)
                            {
                                string fileName = item.Text.Replace("[FTP] ", "");
                                string ftpFilePath = currentPath.TrimEnd('/') + "/" + fileName;
                                string localFilePath = Path.Combine(folderDialog.SelectedPath, fileName);
                                
                                bool success = await ftpService.DownloadFileAsync(ftpFilePath, localFilePath);
                                
                                if (success)
                                {
                                    MessageBox.Show($"Successfully downloaded '{fileName}' to:\n{localFilePath}", 
                                        "Download Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                }
                                else
                                {
                                    MessageBox.Show($"Failed to download '{fileName}'", "Download Failed", 
                                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error downloading FTP file: {ex.Message}", "Download Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private void ShowFtpProperties(ListViewItem item)
        {
            try
            {
                // Read the FTP file info if available
                string itemPath = item.Name;
                string infoText = "FTP Item Properties\n\n";
                infoText += $"Name: {item.Text}\n";
                infoText += $"Path: {itemPath}\n";
                
                // Try to read additional info from the file content
                if (File.Exists(itemPath))
                {
                    string content = File.ReadAllText(itemPath);
                    infoText += $"\nDetails:\n{content}";
                }
                
                MessageBox.Show(infoText, $"Properties: {item.Text}", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reading FTP properties: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private async void DeleteFtpItem(ListViewItem item)
        {
            var result = MessageBox.Show($"Are you sure you want to delete '{item.Text}' from the FTP server?", 
                "Delete FTP Item", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result == DialogResult.Yes)
            {
                try
                {
                    // Get the MainForm to access FTP service
                    var mainForm = FindForm() as MainForm;
                    if (mainForm != null)
                    {
                        // Get FTP service using reflection
                        var ftpServiceField = mainForm.GetType().GetField("_ftpService", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var configField = mainForm.GetType().GetField("_config", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var currentPathField = mainForm.GetType().GetField("_currentRemoteFtpPath", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        
                        if (ftpServiceField?.GetValue(mainForm) is GCodeSyncCore.Services.FtpService ftpService && 
                            configField?.GetValue(mainForm) is GCodeSyncCore.Models.SyncConfiguration config &&
                            currentPathField?.GetValue(mainForm) is string currentPath)
                        {
                            // Get the actual file/folder name without any prefixes
                            string fileName = item.Text.Replace("[FTP] ", "").Trim();
                            string ftpItemPath = currentPath.TrimEnd('/') + "/" + fileName;
                            
                            // Check if this is a directory by looking at the local temp representation
                            bool isDirectory = Directory.Exists(item.Name);
                            
                            if (isDirectory)
                            {
                                // Confirm directory deletion
                                var dirResult = MessageBox.Show($"Are you sure you want to delete the directory '{fileName}' and all its contents from the FTP server?\n\nThis action cannot be undone.", 
                                    "Confirm Directory Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                                
                                if (dirResult == DialogResult.Yes)
                                {
                                    // Delete the directory from FTP server
                                    bool success = await ftpService.DeleteDirectoryAsync(ftpItemPath);
                                    
                                    if (success)
                                    {
                                        MessageBox.Show($"Successfully deleted directory '{fileName}' from FTP server", "Delete Success", 
                                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                                        
                                        // Refresh the FTP directory
                                        RefreshFtpDirectory();
                                    }
                                    else
                                    {
                                        MessageBox.Show($"Failed to delete directory '{fileName}' from FTP server. Check logs for details.", "Delete Failed", 
                                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                                    }
                                }
                            }
                            else
                            {
                                // Delete the file from FTP server
                                bool success = await ftpService.DeleteFileAsync(ftpItemPath);
                                
                                if (success)
                                {
                                    MessageBox.Show($"Successfully deleted '{fileName}' from FTP server", "Delete Success", 
                                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                                    
                                    // Refresh the FTP directory
                                    RefreshFtpDirectory();
                                }
                                else
                                {
                                    MessageBox.Show($"Failed to delete '{fileName}' from FTP server.\nPath attempted: {ftpItemPath}", "Delete Failed", 
                                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                                }
                            }
                        }
                        else
                        {
                            MessageBox.Show("Cannot access FTP service", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting FTP file: {ex.Message}", "Delete Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
        
        private async void DownloadMultipleFtpItems()
        {
            try
            {
                using (var folderDialog = new FolderBrowserDialog())
                {
                    folderDialog.Description = "Select folder to download files to:";
                    folderDialog.ShowNewFolderButton = true;
                    
                    if (folderDialog.ShowDialog() == DialogResult.OK)
                    {
                        var mainForm = FindForm() as MainForm;
                        if (mainForm != null)
                        {
                            var ftpServiceField = mainForm.GetType().GetField("_ftpService", 
                                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            var currentPathField = mainForm.GetType().GetField("_currentRemoteFtpPath", 
                                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            
                            if (ftpServiceField?.GetValue(mainForm) is GCodeSyncCore.Services.FtpService ftpService &&
                                currentPathField?.GetValue(mainForm) is string currentPath)
                            {
                                int successCount = 0;
                                int failCount = 0;
                                
                                foreach (ListViewItem item in SelectedItems)
                                {
                                    // Skip special items
                                    if (item.Text == ".." || item.Text.StartsWith("0_FTP_CONNECTION_INFO") || 
                                        item.Text.Contains("_FTP_FOLDER_INFO") || item.Text.Contains("_PARENT_DIR"))
                                        continue;
                                    
                                    string fileName = item.Text.Replace("[FTP] ", "");
                                    string ftpFilePath = currentPath.TrimEnd('/') + "/" + fileName;
                                    string localFilePath = Path.Combine(folderDialog.SelectedPath, fileName);
                                    
                                    bool success = await ftpService.DownloadFileAsync(ftpFilePath, localFilePath);
                                    if (success)
                                        successCount++;
                                    else
                                        failCount++;
                                }
                                
                                string message = $"Download operation complete.\nSuccessful: {successCount}\nFailed: {failCount}\nLocation: {folderDialog.SelectedPath}";
                                MessageBox.Show(message, "Download Results", MessageBoxButtons.OK, 
                                    failCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error downloading FTP files: {ex.Message}", "Download Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private async void DeleteMultipleFtpItems()
        {
            var result = MessageBox.Show($"Are you sure you want to delete {SelectedItems.Count} items from the FTP server?", 
                "Delete FTP Items", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result == DialogResult.Yes)
            {
                try
                {
                    // Get the MainForm to access FTP service
                    var mainForm = FindForm() as MainForm;
                    if (mainForm != null)
                    {
                        var ftpServiceField = mainForm.GetType().GetField("_ftpService", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var currentPathField = mainForm.GetType().GetField("_currentRemoteFtpPath", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        
                        if (ftpServiceField?.GetValue(mainForm) is GCodeSyncCore.Services.FtpService ftpService &&
                            currentPathField?.GetValue(mainForm) is string currentPath)
                        {
                            int successCount = 0;
                            int failCount = 0;
                            
                            foreach (ListViewItem item in SelectedItems)
                            {
                                // Skip special items
                                if (item.Text == ".." || item.Text.StartsWith("0_FTP_CONNECTION_INFO") || 
                                    item.Text.Contains("_FTP_FOLDER_INFO") || item.Text.Contains("_PARENT_DIR"))
                                    continue;
                                
                                string fileName = item.Text.Replace("[FTP] ", "");
                                string ftpFilePath = currentPath.TrimEnd('/') + "/" + fileName;
                                
                                bool success = await ftpService.DeleteFileAsync(ftpFilePath);
                                if (success)
                                    successCount++;
                                else
                                    failCount++;
                            }
                            
                            string message = $"Delete operation complete.\nSuccessful: {successCount}\nFailed: {failCount}";
                            MessageBox.Show(message, "Delete Results", MessageBoxButtons.OK, 
                                failCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                            
                            // Refresh the FTP directory
                            RefreshFtpDirectory();
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting FTP files: {ex.Message}", "Delete Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
        
        private void RefreshFtpDirectory()
        {
            // Find the parent form and call refresh
            var mainForm = FindForm() as MainForm;
            if (mainForm != null)
            {
                // Call the refresh method on main form
                var refreshMethod = mainForm.GetType().GetMethod("RefreshRemoteExplorer", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                refreshMethod?.Invoke(mainForm, null);
            }
        }
        
        private void CreateFtpFolder()
        {
            // Simple input dialog using a form
            using (var form = new Form())
            {
                form.Text = "Create FTP Folder";
                form.Size = new Size(300, 120);
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                
                var label = new Label() { Text = "Folder name:", Location = new Point(10, 15), Size = new Size(100, 23) };
                var textBox = new TextBox() { Text = "New Folder", Location = new Point(10, 40), Size = new Size(260, 23) };
                var buttonOk = new Button() { Text = "OK", Location = new Point(115, 70), Size = new Size(75, 23), DialogResult = DialogResult.OK };
                var buttonCancel = new Button() { Text = "Cancel", Location = new Point(195, 70), Size = new Size(75, 23), DialogResult = DialogResult.Cancel };
                
                form.Controls.Add(label);
                form.Controls.Add(textBox);
                form.Controls.Add(buttonOk);
                form.Controls.Add(buttonCancel);
                form.AcceptButton = buttonOk;
                form.CancelButton = buttonCancel;
                
                if (form.ShowDialog() == DialogResult.OK && !string.IsNullOrEmpty(textBox.Text))
                {
                    CreateFtpFolderAsync(textBox.Text);
                }
            }
        }
        
        private async void CreateFtpFolderAsync(string folderName)
        {
            try
            {
                var mainForm = FindForm() as MainForm;
                if (mainForm != null)
                {
                    var ftpServiceField = mainForm.GetType().GetField("_ftpService", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var currentPathField = mainForm.GetType().GetField("_currentRemoteFtpPath", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    if (ftpServiceField?.GetValue(mainForm) is GCodeSyncCore.Services.FtpService ftpService &&
                        currentPathField?.GetValue(mainForm) is string currentPath)
                    {
                        string ftpFolderPath = currentPath.TrimEnd('/') + "/" + folderName;
                        
                        bool success = await ftpService.CreateDirectoryAsync(ftpFolderPath);
                        
                        if (success)
                        {
                            MessageBox.Show($"Successfully created folder '{folderName}' on FTP server", 
                                "Create Folder Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            
                            // Refresh the FTP directory
                            RefreshFtpDirectory();
                        }
                        else
                        {
                            MessageBox.Show($"Failed to create folder '{folderName}' on FTP server", 
                                "Create Folder Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating FTP folder: {ex.Message}", "Create Folder Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private async void UploadFileToFtp(string localFilePath)
        {
            try
            {
                var mainForm = FindForm() as MainForm;
                if (mainForm != null)
                {
                    // Get FTP service and current remote path using reflection
                    var ftpServiceField = mainForm.GetType().GetField("_ftpService", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var currentPathField = mainForm.GetType().GetField("_currentRemoteFtpPath", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    if (ftpServiceField?.GetValue(mainForm) is GCodeSyncCore.Services.FtpService ftpService &&
                        currentPathField?.GetValue(mainForm) is string currentFtpPath)
                    {
                        string fileName = Path.GetFileName(localFilePath);
                        string remotePath = currentFtpPath.TrimEnd('/') + "/" + fileName;
                        
                        // Upload the file
                        bool success = await ftpService.UploadFileAsync(localFilePath, remotePath);
                        
                        if (success)
                        {
                            // Refresh the FTP directory
                            RefreshFtpDirectory();
                        }
                        else
                        {
                            MessageBox.Show($"Failed to upload '{fileName}' to FTP server. Check logs for details.", "Upload Failed", 
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error uploading file: {ex.Message}", "Upload Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private async void UploadFolderToFtp(string localFolderPath)
        {
            try
            {
                var mainForm = FindForm() as MainForm;
                if (mainForm != null)
                {
                    // Get FTP service and current remote path using reflection
                    var ftpServiceField = mainForm.GetType().GetField("_ftpService", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var currentPathField = mainForm.GetType().GetField("_currentRemoteFtpPath", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    if (ftpServiceField?.GetValue(mainForm) is GCodeSyncCore.Services.FtpService ftpService &&
                        currentPathField?.GetValue(mainForm) is string currentFtpPath)
                    {
                        string folderName = Path.GetFileName(localFolderPath);
                        string remoteFolderPath = currentFtpPath.TrimEnd('/') + "/" + folderName;
                        
                        // Confirm folder upload
                        var result = MessageBox.Show($"Upload folder '{folderName}' and all its contents to FTP server?\n\nThis will create the folder structure on the server.", 
                            "Confirm Folder Upload", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                        
                        if (result == DialogResult.Yes)
                        {
                            // Upload the entire folder
                            bool success = await UploadFolderRecursive(ftpService, localFolderPath, remoteFolderPath);
                            
                            if (success)
                            {
                                // Refresh the FTP directory
                                RefreshFtpDirectory();
                            }
                            else
                            {
                                MessageBox.Show($"Failed to upload folder '{folderName}' to FTP server. Check logs for details.", "Upload Failed", 
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error uploading folder: {ex.Message}", "Upload Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private async Task<bool> UploadFolderRecursive(GCodeSyncCore.Services.FtpService ftpService, string localPath, string remotePath)
        {
            try
            {
                // Create the remote directory
                bool dirCreated = await ftpService.CreateDirectoryAsync(remotePath);
                if (!dirCreated)
                {
                    return false;
                }
                
                // Upload all files in the directory
                foreach (string filePath in Directory.GetFiles(localPath))
                {
                    string fileName = Path.GetFileName(filePath);
                    string remoteFilePath = remotePath.TrimEnd('/') + "/" + fileName;
                    
                    bool fileUploaded = await ftpService.UploadFileAsync(filePath, remoteFilePath);
                    if (!fileUploaded)
                    {
                        return false;
                    }
                }
                
                // Recursively upload subdirectories
                foreach (string dirPath in Directory.GetDirectories(localPath))
                {
                    string dirName = Path.GetFileName(dirPath);
                    string remoteSubDir = remotePath.TrimEnd('/') + "/" + dirName;
                    
                    bool subdirUploaded = await UploadFolderRecursive(ftpService, dirPath, remoteSubDir);
                    if (!subdirUploaded)
                    {
                        return false;
                    }
                }
                
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public class ListViewDirectoryBrowser : ListViewBrowser
    {
        public ListViewDirectoryBrowser()
        {
            Columns.Add("Folder", 200, HorizontalAlignment.Left);
            Columns.Add("Modified", 120, HorizontalAlignment.Left);

            ListViewItemSorter = new DirectoryListViewColumnSorter();
            
            // Enable column sorting
            ColumnClick += new ColumnClickEventHandler((object? sender, ColumnClickEventArgs e) =>
            {
                var sorter = ListViewItemSorter as DirectoryListViewColumnSorter;
                if (sorter != null)
                {
                    if (e.Column == sorter.SortColumn)
                    {
                        // Same column clicked - reverse sort order
                        sorter.Order = sorter.Order == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
                    }
                    else
                    {
                        // New column clicked - default to ascending
                        sorter.SortColumn = e.Column;
                        sorter.Order = SortOrder.Ascending;
                    }
                    Sort();
                }
            });
            
            MouseDoubleClick += new MouseEventHandler((object? sender, MouseEventArgs e) =>
            {
                ListViewItem? item = FocusedItem;
                if (item != null && e.Button == MouseButtons.Left)
                {
                    string path = item.Name ?? "";
                    if (item.Text == "..")
                    {
                        Container?.GoToParentDirectory();
                    }
                    else if (Directory.Exists(path))
                    {
                        Container?.NavigateTo(path);
                    }
                }
            });
            
            DragDrop += new DragEventHandler((object? sender, DragEventArgs e) =>
            {
                string destinationDirectory = DirPath ?? "";
                if (!Directory.Exists(destinationDirectory))
                    return;

                string[]? sourceNames = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (sourceNames != null)
                {
                    foreach (string sourceName in sourceNames)
                    {
                        string fileName = Path.GetFileName(sourceName);
                        string destinationName = Path.Combine(destinationDirectory, fileName);
                        FileSystemHelper.OperateFileSystemItem(sourceName, destinationName, DragDropEffects.Copy);
                    }
                }
            });
        }
    }

    public class ListViewFileBrowser : ListViewBrowser
    {
        public ListViewFileBrowser()
        {
            Columns.Add("File", 200, HorizontalAlignment.Left);
            Columns.Add("Type", 80, HorizontalAlignment.Left);
            Columns.Add("Modified", 120, HorizontalAlignment.Left);
            Columns.Add("Size", 80, HorizontalAlignment.Right);

            ListViewItemSorter = new FileListViewColumnSorter();
            
            // Enable column sorting
            ColumnClick += new ColumnClickEventHandler((object? sender, ColumnClickEventArgs e) =>
            {
                var sorter = ListViewItemSorter as FileListViewColumnSorter;
                if (sorter != null)
                {
                    if (e.Column == sorter.SortColumn)
                    {
                        // Same column clicked - reverse sort order
                        sorter.Order = sorter.Order == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
                    }
                    else
                    {
                        // New column clicked - default to ascending
                        sorter.SortColumn = e.Column;
                        sorter.Order = SortOrder.Ascending;
                    }
                    Sort();
                }
            });
            
            MouseDoubleClick += new MouseEventHandler((object? sender, MouseEventArgs e) =>
            {
                ListViewItem? item = FocusedItem;
                if (item != null && e.Button == MouseButtons.Left)
                {
                    string path = item.Name ?? "";
                    if (File.Exists(path))
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Cannot open file: {ex.Message}", "Error", 
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
            });
            
            DragDrop += new DragEventHandler((object? sender, DragEventArgs e) =>
            {
                string destinationDirectory = DirPath ?? "";
                if (!Directory.Exists(destinationDirectory))
                    return;

                string[]? sourceNames = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (sourceNames != null)
                {
                    foreach (string sourceName in sourceNames)
                    {
                        string fileName = Path.GetFileName(sourceName);
                        string destinationName = Path.Combine(destinationDirectory, fileName);
                        FileSystemHelper.OperateFileSystemItem(sourceName, destinationName, DragDropEffects.Copy);
                    }
                }
            });
        }
    }

    public class DirectoryListViewColumnSorter : IComparer
    {
        public int SortColumn { get; set; } = 0;
        public SortOrder Order { get; set; } = SortOrder.Ascending;

        public int Compare(object? x, object? y)
        {
            if (x == null || y == null) return 0;
            
            int compareResult;
            ListViewItem listviewX, listviewY;

            listviewX = (ListViewItem)x;
            listviewY = (ListViewItem)y;

            // Always put ".." at the top
            if (listviewX.Text == "..")
                return -1;
            if (listviewY.Text == "..")
                return 1;

            switch (SortColumn)
            {
                case 1: // Modified date
                    DateTime dateX, dateY;
                    if (DateTime.TryParse(listviewX.SubItems[1].Text, out dateX) &&
                        DateTime.TryParse(listviewY.SubItems[1].Text, out dateY))
                    {
                        compareResult = DateTime.Compare(dateX, dateY);
                    }
                    else
                    {
                        compareResult = String.Compare(listviewX.SubItems[1].Text, listviewY.SubItems[1].Text);
                    }
                    break;
                default: // Name
                    compareResult = String.Compare(listviewX.Text, listviewY.Text);
                    break;
            }

            if (Order == SortOrder.Descending)
                return (-compareResult);
            else
                return compareResult;
        }
    }

    public class FileListViewColumnSorter : IComparer
    {
        public int SortColumn { get; set; } = 0;
        public SortOrder Order { get; set; } = SortOrder.Ascending;

        public int Compare(object? x, object? y)
        {
            if (x == null || y == null) return 0;
            
            int compareResult;
            ListViewItem listviewX, listviewY;

            listviewX = (ListViewItem)x;
            listviewY = (ListViewItem)y;

            switch (SortColumn)
            {
                case 1: // Type
                    compareResult = String.Compare(listviewX.SubItems[1].Text, listviewY.SubItems[1].Text);
                    break;
                case 2: // Modified date
                    DateTime dateX, dateY;
                    if (DateTime.TryParse(listviewX.SubItems[2].Text, out dateX) &&
                        DateTime.TryParse(listviewY.SubItems[2].Text, out dateY))
                    {
                        compareResult = DateTime.Compare(dateX, dateY);
                    }
                    else
                    {
                        compareResult = String.Compare(listviewX.SubItems[2].Text, listviewY.SubItems[2].Text);
                    }
                    break;
                case 3: // Size
                    long sizeX = 0, sizeY = 0;
                    string sizeTextX = listviewX.SubItems[3].Text.Split(' ')[0];
                    string sizeTextY = listviewY.SubItems[3].Text.Split(' ')[0];
                    
                    if (long.TryParse(sizeTextX.Replace(",", ""), out sizeX) &&
                        long.TryParse(sizeTextY.Replace(",", ""), out sizeY))
                    {
                        compareResult = sizeX.CompareTo(sizeY);
                    }
                    else
                    {
                        compareResult = String.Compare(listviewX.SubItems[3].Text, listviewY.SubItems[3].Text);
                    }
                    break;
                default: // Name
                    compareResult = String.Compare(listviewX.Text, listviewY.Text);
                    break;
            }

            if (Order == SortOrder.Descending)
                return (-compareResult);
            else
                return compareResult;
        }
    }
}