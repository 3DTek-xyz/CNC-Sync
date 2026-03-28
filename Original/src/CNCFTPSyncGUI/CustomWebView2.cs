using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using System.Windows.Forms;
using System.Text;

namespace CNCFTPSyncGUI
{
    public class CustomWebView2 : WebView2
    {
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public event EventHandler<string>? NavigationBlocked;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public event EventHandler<ExecuteEventArgs>? ItemExecuted;

        private string _currentPath;
        private bool _isInitialized = false;

        public CustomWebView2()
        {
            this.CoreWebView2InitializationCompleted += CustomWebView2_CoreWebView2InitializationCompleted;
        }

        private async void CustomWebView2_CoreWebView2InitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (e.IsSuccess && CoreWebView2 != null)
            {
                _isInitialized = true;
                
                // Configure WebView2 settings for better control
                CoreWebView2.Settings.IsWebMessageEnabled = true;
                CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
                CoreWebView2.Settings.AreHostObjectsAllowed = false;
                CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
                CoreWebView2.Settings.IsStatusBarEnabled = false;
                CoreWebView2.Settings.AreDevToolsEnabled = false;
                CoreWebView2.Settings.IsSwipeNavigationEnabled = false;
                CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
                
                // Handle navigation events
                CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
                CoreWebView2.DOMContentLoaded += CoreWebView2_DOMContentLoaded;
                CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
                CoreWebView2.WindowCloseRequested += CoreWebView2_WindowCloseRequested;
                
                // Inject JavaScript for file operations
                CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                CoreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;
            }
        }

        // Add Navigate method for compatibility with old WebBrowser API
        public async void Navigate(string url)
        {
            if (url.StartsWith("file://") || Directory.Exists(url))
            {
                await NavigateToPath(url);
            }
            else
            {
                Source = new Uri(url);
            }
        }

        private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            // Handle local file navigation
            if (e.Uri.StartsWith("file://"))
            {
                e.Cancel = true; // Cancel default file navigation behavior
                string localPath = Uri.UnescapeDataString(e.Uri.Substring(7));
                
                // Handle directory navigation internally
                if (Directory.Exists(localPath))
                {
                    _ = NavigateToPath(localPath);
                }
                else if (File.Exists(localPath))
                {
                    // File clicked - trigger ItemExecuted event
                    ItemExecuted?.Invoke(this, new ExecuteEventArgs(localPath, Path.GetFileName(localPath)));
                }
            }
            else if (e.Uri.StartsWith("http://") || e.Uri.StartsWith("https://"))
            {
                // Block external HTTP navigation to prevent opening external browser
                e.Cancel = true;
            }
        }

        private async void CoreWebView2_DOMContentLoaded(object? sender, CoreWebView2DOMContentLoadedEventArgs e)
        {
            if (CoreWebView2 != null)
            {
                // Inject JavaScript for enhanced file browser functionality
                string script = @"
                document.addEventListener('click', function(e) {
                    const target = e.target.closest('a');
                    if (target && target.href) {
                        e.preventDefault();
                        window.chrome.webview.postMessage({
                            type: 'navigate',
                            url: target.href,
                            text: target.textContent
                        });
                    }
                });
                ";
                
                await CoreWebView2.ExecuteScriptAsync(script);
                
                // Listen for messages from JavaScript
                CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
            }
        }

        private async void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string message = e.TryGetWebMessageAsString();
                // Parse JSON message and handle navigation
                if (message.Contains("\"type\":\"navigate\""))
                {
                    // Simple JSON parsing for navigation
                    if (message.Contains("file://"))
                    {
                        int startIndex = message.IndexOf("file://");
                        int endIndex = message.IndexOf("\"", startIndex);
                        if (endIndex > startIndex)
                        {
                            string url = message.Substring(startIndex, endIndex - startIndex);
                            string localPath = Uri.UnescapeDataString(url.Substring(7));
                            
                            if (File.Exists(localPath))
                            {
                                ItemExecuted?.Invoke(this, new ExecuteEventArgs(localPath, Path.GetFileName(localPath)));
                            }
                            else if (Directory.Exists(localPath))
                            {
                                await NavigateToPath(localPath);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebView2 message error: {ex.Message}");
            }
        }

        private void CoreWebView2_WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            // Handle custom file browser requests
            if (e.Request.Uri.Contains("custom-file-browser"))
            {
                string html = GenerateFileBrowserHtml(_currentPath ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
                
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
                var response = CoreWebView2?.Environment.CreateWebResourceResponse(
                    stream,
                    200,
                    "OK",
                    "Content-Type: text/html; charset=utf-8");
                
                if (response != null)
                {
                    e.Response = response;
                }
            }
        }

        public async Task NavigateToPath(string path)
        {
            if (!_isInitialized)
            {
                await EnsureCoreWebView2Async(null);
            }
            
            _currentPath = path;
            
            if (Directory.Exists(path))
            {
                // Generate custom HTML for file browsing
                string html = GenerateFileBrowserHtml(path);
                NavigateToString(html);
            }
            else if (path.StartsWith("ftp://"))
            {
                // For FTP, we'll create a custom interface
                string html = GenerateFtpBrowserHtml(path);
                NavigateToString(html);
            }
        }

        private string GenerateFileBrowserHtml(string path)
        {
            StringBuilder html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html><head>");
            html.AppendLine("<title>File Browser</title>");
            html.AppendLine("<style>");
            html.AppendLine("body { font-family: Segoe UI, sans-serif; margin: 10px; }");
            html.AppendLine(".folder { color: #0066cc; cursor: pointer; margin: 2px 0; }");
            html.AppendLine(".file { color: #333; margin: 2px 0; }");
            html.AppendLine(".icon { display: inline-block; width: 20px; margin-right: 5px; }");
            html.AppendLine("a { text-decoration: none; }");
            html.AppendLine("a:hover { text-decoration: underline; }");
            html.AppendLine(".path { background: #f0f0f0; padding: 10px; margin-bottom: 10px; border-radius: 4px; }");
            html.AppendLine("</style>");
            html.AppendLine("</head><body>");
            
            html.AppendLine($"<div class='path'>📂 {path}</div>");
            
            try
            {
                // Add parent directory link if not root
                DirectoryInfo dirInfo = new DirectoryInfo(path);
                if (dirInfo.Parent != null)
                {
                    string parentPath = dirInfo.Parent.FullName;
                    html.AppendLine($"<div class='folder'><a href='file:///{parentPath.Replace('\\', '/')}'>");
                    html.AppendLine($"<span class='icon'>📁</span>.. (Parent Directory)</a></div>");
                }
                
                // List directories
                foreach (var dir in Directory.GetDirectories(path))
                {
                    string dirName = Path.GetFileName(dir);
                    string dirPath = dir.Replace('\\', '/');
                    html.AppendLine($"<div class='folder'><a href='file:///{dirPath}'>");
                    html.AppendLine($"<span class='icon'>📁</span>{dirName}</a></div>");
                }
                
                // List files
                foreach (var file in Directory.GetFiles(path))
                {
                    string fileName = Path.GetFileName(file);
                    string filePath = file.Replace('\\', '/');
                    html.AppendLine($"<div class='file'><a href='file:///{filePath}'>");
                    html.AppendLine($"<span class='icon'>📄</span>{fileName}</a></div>");
                }
            }
            catch (Exception ex)
            {
                html.AppendLine($"<div style='color: red;'>Error loading directory: {ex.Message}</div>");
            }
            
            html.AppendLine("</body></html>");
            return html.ToString();
        }

        private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            // Prevent opening new windows/external browser - handle navigation internally
            e.Handled = true;
            
            // Navigate to the URL within this WebView2 instead of opening externally
            if (!string.IsNullOrEmpty(e.Uri))
            {
                // If it's a file:// URL, navigate internally
                if (e.Uri.StartsWith("file://"))
                {
                    _ = NavigateToPath(e.Uri);
                }
                else
                {
                    // For other URLs, navigate within this WebView2
                    CoreWebView2?.Navigate(e.Uri);
                }
            }
        }

        private void CoreWebView2_WindowCloseRequested(object? sender, object e)
        {
            // Allow WebView2 close requests - don't block them
            // This prevents interference with form closing
        }

        private string GenerateFtpBrowserHtml(string ftpUrl)
        {
            StringBuilder html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html><head>");
            html.AppendLine("<title>FTP Browser</title>");
            html.AppendLine("<style>");
            html.AppendLine("body { font-family: Segoe UI, sans-serif; margin: 10px; }");
            html.AppendLine(".loading { text-align: center; margin: 50px; }");
            html.AppendLine("</style>");
            html.AppendLine("</head><body>");
            html.AppendLine($"<div class='loading'>Loading FTP directory: {ftpUrl}<br>Please wait...</div>");
            html.AppendLine("</body></html>");
            return html.ToString();
        }
    }

    // ExecuteEventArgs already exists in CustomWebBrowser.cs - reusing that class
}