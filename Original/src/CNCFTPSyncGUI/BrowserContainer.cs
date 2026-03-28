using System;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;

namespace CNCFTPSyncGUI
{
    public class BrowserContainer : SplitContainer
    {
        private ListViewDirectoryBrowser _directoryBrowser = null!;
        private ListViewFileBrowser _fileBrowser = null!;
        private ImageList _imageList = null!;
        private string _currentDirectory = "";
        private List<string> _navigationHistory = new List<string>();
        private int _navigationIndex = -1;
        private string _rootPath = "";

        public string DirPath
        {
            get => _currentDirectory;
        }
        
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string RootPath
        {
            get => _rootPath;
            set => _rootPath = value;
        }

        public event EventHandler<string>? DirectoryChanged;

        public BrowserContainer()
        {
            InitializeBrowser();
        }

        private void InitializeBrowser()
        {
            // Set up SplitContainer properties
            Orientation = Orientation.Horizontal;
            SplitterDistance = 150;
            Panel1MinSize = 120;
            Panel2MinSize = 120;
            BorderStyle = BorderStyle.Fixed3D;
            
            // Add significant padding to panels to prevent header cut-off
            Panel1.Padding = new Padding(8, 15, 8, 8);  // Much larger top padding
            Panel2.Padding = new Padding(8, 15, 8, 8);  // Much larger top padding
            
            // Create image list for file icons
            _imageList = new ImageList();
            _imageList.ImageSize = new Size(16, 16);
            _imageList.ColorDepth = ColorDepth.Depth32Bit;

            // Create directory browser (top panel) - back to simple docking with proper panel padding
            _directoryBrowser = new ListViewDirectoryBrowser();
            _directoryBrowser.SmallImageList = _imageList;
            _directoryBrowser.Dock = DockStyle.Fill;
            Panel1.Controls.Add(_directoryBrowser);

            // Create file browser (bottom panel) - back to simple docking with proper panel padding
            _fileBrowser = new ListViewFileBrowser();
            _fileBrowser.SmallImageList = _imageList;
            _fileBrowser.Dock = DockStyle.Fill;
            Panel2.Controls.Add(_fileBrowser);

            // Set up event handlers
            _directoryBrowser.ItemActivate += OnDirectoryItemActivate;
            _directoryBrowser.SelectedIndexChanged += OnDirectorySelectionChanged;
            _fileBrowser.ItemActivate += OnFileItemActivate;
            
            // Set up container resize handler for responsive column sizing
            Resize += OnContainerResize;
        }
        
        /// <summary>
        /// Preview files in the file browser without changing navigation
        /// </summary>
        public void PreviewFiles(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    // Load files in the bottom panel without changing the current directory navigation
                    LoadFilesFromPath(path);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error previewing files: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Load files from a specific path into the file browser without changing navigation
        /// </summary>
        private void LoadFilesFromPath(string path)
        {
            _fileBrowser.BeginUpdate();
            _fileBrowser.Items.Clear();

            try
            {
                foreach (string file in Directory.GetFiles(path))
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        var displayName = Path.GetFileName(file);
                        if (string.IsNullOrEmpty(displayName))
                            displayName = file;
                            
                        var item = new ListViewItem(displayName);
                        item.Name = file;
                        
                        // Add file size and modified date
                        item.SubItems.Add(FileSizeHelper.FormatSize(fileInfo.Length));
                        item.SubItems.Add(fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));

                        // Set icon
                        var icon = ShellInfoHelper.GetIconFromPath(file);
                        if (icon != null)
                        {
                            _imageList.Images.Add(file, icon);
                            item.ImageKey = file;
                        }

                        _fileBrowser.Items.Add(item);
                    }
                    catch
                    {
                        // Skip files that can't be accessed
                    }
                }
            }
            catch
            {
                // Handle directory access errors
            }
            finally
            {
                _fileBrowser.EndUpdate();
            }
        }

        public void NavigateTo(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return;

            try
            {
                _currentDirectory = Path.GetFullPath(path);
                
                // Add to navigation history
                if (_navigationIndex == -1 || _navigationHistory[_navigationIndex] != _currentDirectory)
                {
                    // Remove any forward history
                    if (_navigationIndex >= 0 && _navigationIndex < _navigationHistory.Count - 1)
                    {
                        _navigationHistory.RemoveRange(_navigationIndex + 1, _navigationHistory.Count - _navigationIndex - 1);
                    }
                    
                    _navigationHistory.Add(_currentDirectory);
                    _navigationIndex = _navigationHistory.Count - 1;
                }

                LoadDirectory();
                DirectoryChanged?.Invoke(this, _currentDirectory);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error navigating to {path}: {ex.Message}", "Navigation Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void GoToParentDirectory()
        {
            if (string.IsNullOrEmpty(_currentDirectory))
                return;

            try
            {
                var parent = Directory.GetParent(_currentDirectory);
                if (parent != null)
                {
                    // Check if parent is above the root boundary
                    string parentPath = parent.FullName;
                    
                    // Check FTP temp directory boundary first - never allow escape from FTP
                    bool isInFtpTemp = _currentDirectory.Contains("GCodeSync_FTP_Browser");
                    if (isInFtpTemp)
                    {
                        string ftpTempBase = Path.Combine(Path.GetTempPath(), "GCodeSync_FTP_Browser");
                        string normalizedFtpBase = Path.GetFullPath(ftpTempBase).TrimEnd(Path.DirectorySeparatorChar);
                        string normalizedParent = Path.GetFullPath(parentPath).TrimEnd(Path.DirectorySeparatorChar);
                        
                        // Block navigation outside FTP temp structure
                        if (!normalizedParent.StartsWith(normalizedFtpBase))
                        {
                            return; // Don't navigate outside FTP boundary
                        }
                    }
                    
                    // If root path is set, don't allow navigation above it
                    if (!string.IsNullOrEmpty(_rootPath))
                    {
                        string normalizedRoot = Path.GetFullPath(_rootPath).TrimEnd(Path.DirectorySeparatorChar);
                        string normalizedParent = Path.GetFullPath(parentPath).TrimEnd(Path.DirectorySeparatorChar);
                        
                        // Check if parent would be above root (shorter path or different branch)
                        if (normalizedParent.Length < normalizedRoot.Length || 
                            !normalizedParent.StartsWith(normalizedRoot))
                        {
                            // Don't navigate above root
                            return;
                        }
                    }
                    
                    NavigateTo(parentPath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error navigating to parent directory: {ex.Message}", "Navigation Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public bool CanGoBack()
        {
            return _navigationIndex > 0;
        }

        public bool CanGoForward()
        {
            return _navigationIndex >= 0 && _navigationIndex < _navigationHistory.Count - 1;
        }

        public void GoBack()
        {
            if (CanGoBack())
            {
                _navigationIndex--;
                _currentDirectory = _navigationHistory[_navigationIndex];
                LoadDirectory();
                DirectoryChanged?.Invoke(this, _currentDirectory);
            }
        }

        public void GoForward()
        {
            if (CanGoForward())
            {
                _navigationIndex++;
                _currentDirectory = _navigationHistory[_navigationIndex];
                LoadDirectory();
                DirectoryChanged?.Invoke(this, _currentDirectory);
            }
        }

        public new void Refresh()
        {
            if (!string.IsNullOrEmpty(_currentDirectory))
            {
                LoadDirectory();
            }
        }

        private void LoadDirectory()
        {
            if (string.IsNullOrEmpty(_currentDirectory) || !Directory.Exists(_currentDirectory))
                return;

            try
            {
                LoadDirectories();
                LoadFiles();
                
                // Trigger column resize for responsive layout
                TriggerColumnResize();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading directory {_currentDirectory}: {ex.Message}", "Directory Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadDirectories()
        {
            _directoryBrowser.BeginUpdate();
            _directoryBrowser.Items.Clear();

            try
            {
                // Add parent directory link (unless at root boundary)
                var parent = Directory.GetParent(_currentDirectory);
                if (parent != null)
                {
                    bool canGoToParent = true;
                    
                    // Check root boundary
                    if (!string.IsNullOrEmpty(_rootPath))
                    {
                        string normalizedRoot = Path.GetFullPath(_rootPath).TrimEnd(Path.DirectorySeparatorChar);
                        string normalizedCurrent = Path.GetFullPath(_currentDirectory).TrimEnd(Path.DirectorySeparatorChar);
                        
                        // Don't show ".." if we're at or above the root
                        if (normalizedCurrent.Length <= normalizedRoot.Length || 
                            !normalizedCurrent.StartsWith(normalizedRoot + Path.DirectorySeparatorChar))
                        {
                            canGoToParent = false;
                        }
                    }
                    
                    // Additional check: Don't show ".." in FTP temp directories - never allow escape from FTP
                    if (canGoToParent)
                    {
                        // Check if we're in an FTP temp directory - completely block parent navigation
                        bool isInFtpTemp = _currentDirectory.Contains("GCodeSync_FTP_Browser");
                        if (isInFtpTemp)
                        {
                            // Never allow navigation above FTP temp directory structure
                            // This prevents escaping to local file system (C:\Users\...\Temp)
                            string ftpTempBase = Path.Combine(Path.GetTempPath(), "GCodeSync_FTP_Browser");
                            string normalizedFtpBase = Path.GetFullPath(ftpTempBase).TrimEnd(Path.DirectorySeparatorChar);
                            string normalizedParent = Path.GetFullPath(parent.FullName).TrimEnd(Path.DirectorySeparatorChar);
                            
                            // If parent would be outside the FTP temp structure, block it
                            if (!normalizedParent.StartsWith(normalizedFtpBase))
                            {
                                canGoToParent = false;
                            }
                        }
                        
                        if (canGoToParent)
                        {
                            var parentItem = new ListViewItem("..");
                            parentItem.Name = parent.FullName;
                            parentItem.SubItems.Add("");
                            parentItem.ImageIndex = GetDirectoryImageIndex();
                            _directoryBrowser.Items.Add(parentItem);
                        }
                    }
                }

                // Add subdirectories
                foreach (string dir in Directory.GetDirectories(_currentDirectory))
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(dir);
                        var displayName = Path.GetFileName(dir);
                        if (string.IsNullOrEmpty(displayName))
                            displayName = dir;
                            
                        var item = new ListViewItem(displayName);
                        item.Name = dir;
                        item.SubItems.Add(TimeHelper.FormatDateTime(dirInfo.LastWriteTime));
                        item.ImageIndex = GetDirectoryImageIndex();
                        _directoryBrowser.Items.Add(item);
                    }
                    catch
                    {
                        // Skip directories we can't access
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we don't have permission to read
            }
            finally
            {
                _directoryBrowser.EndUpdate();
            }
        }

        private void LoadFiles()
        {
            _fileBrowser.BeginUpdate();
            _fileBrowser.Items.Clear();

            try
            {
                foreach (string file in Directory.GetFiles(_currentDirectory))
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        var displayName = Path.GetFileName(file);
                        if (string.IsNullOrEmpty(displayName))
                            displayName = file;
                            
                        var item = new ListViewItem(displayName);
                        item.Name = file;
                        item.SubItems.Add(Path.GetExtension(file).ToUpper().TrimStart('.') + " File");
                        item.SubItems.Add(TimeHelper.FormatDateTime(fileInfo.LastWriteTime));
                        item.SubItems.Add(FileSizeHelper.FormatSize(fileInfo.Length));
                        item.ImageIndex = GetFileImageIndex(file);
                        _fileBrowser.Items.Add(item);
                    }
                    catch
                    {
                        // Skip files we can't access
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip files we don't have permission to read
            }
            finally
            {
                _fileBrowser.EndUpdate();
            }
        }

        private int GetDirectoryImageIndex()
        {
            // Return folder icon index (would be loaded from system)
            return 0;
        }

        private int GetFileImageIndex(string filePath)
        {
            try
            {
                var icon = ShellInfoHelper.GetIconFromPath(filePath);
                if (icon != null)
                {
                    var iconKey = Path.GetExtension(filePath).ToLower();
                    if (!_imageList.Images.ContainsKey(iconKey))
                    {
                        _imageList.Images.Add(iconKey, icon.ToBitmap());
                    }
                    return _imageList.Images.IndexOfKey(iconKey);
                }
            }
            catch
            {
                // Fallback to default file icon
            }
            return 1;
        }

        private void OnDirectorySelectionChanged(object? sender, EventArgs e)
        {
            // Handle single-click for FTP folder preview
            if (_directoryBrowser.SelectedItems.Count > 0)
            {
                var item = _directoryBrowser.SelectedItems[0];
                string path = item.Name;
                
                // Check if this is an FTP directory and if it has subdirectories/files to preview
                if (Directory.Exists(path) && item.Text != "..")
                {
                    // Check if this is an FTP directory by looking for marker file
                    string markerFile = Path.Combine(path, "_FTP_DIR_MARKER.info");
                    if (File.Exists(markerFile))
                    {
                        // This is an FTP directory - trigger preview
                        var mainForm = FindForm() as MainForm;
                        if (mainForm != null)
                        {
                            mainForm.PreviewFtpFolderContents(path);
                        }
                    }
                    else
                    {
                        // Regular local directory - load files normally
                        PreviewFiles(path);
                    }
                }
            }
        }

        private void OnDirectoryItemActivate(object? sender, EventArgs e)
        {
            if (_directoryBrowser.SelectedItems.Count > 0)
            {
                var item = _directoryBrowser.SelectedItems[0];
                string path = item.Name;
                
                if (item.Text == "..")
                {
                    GoToParentDirectory();
                }
                else if (Directory.Exists(path))
                {
                    NavigateTo(path);
                }
            }
        }

        private void OnFileItemActivate(object? sender, EventArgs e)
        {
            if (_fileBrowser.SelectedItems.Count > 0)
            {
                var item = _fileBrowser.SelectedItems[0];
                string path = item.Name;
                
                if (File.Exists(path))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Cannot open file: {ex.Message}", "Error", 
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void OnContainerResize(object? sender, EventArgs e)
        {
            TriggerColumnResize();
        }

        private void TriggerColumnResize()
        {
            // Trigger resize on both ListViews to recalculate column widths
            _directoryBrowser?.ResizeColumns();
            _fileBrowser?.ResizeColumns();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _imageList?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}