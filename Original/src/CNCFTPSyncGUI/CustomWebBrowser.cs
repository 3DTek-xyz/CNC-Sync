using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Text;
using System.Windows.Forms;

namespace CNCFTPSyncGUI
{
    public class CustomWebBrowser : WebBrowser
    {
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool SuppressNewWindows { get; set; } = true;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public event EventHandler<string>? NavigationBlocked;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public event EventHandler<ExecuteEventArgs>? ItemExecuted;

        private IntPtr m_ShellView;
        private ExplorerListView? m_Explorer;

        // P/Invoke declarations for SendMessage approach
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        private delegate int EnumChildProc(IntPtr hwnd, IntPtr lParam);

        private const int WM_COMMAND = 0x0111;
        private const int SHVIEW_REPORT = 0x702C;
        private const string SHELLVIEW_CLASS = "SHELLDLL_DefView";

        public CustomWebBrowser()
        {
            // Subscribe to Navigated event to set details view
            this.Navigated += CustomWebBrowser_Navigated;
        }

        private void CustomWebBrowser_Navigated(object? sender, WebBrowserNavigatedEventArgs e)
        {
            // Find shell view and set to details view
            m_ShellView = IntPtr.Zero;
            EnumChildWindows(this.Handle, EnumChildren, IntPtr.Zero);
            if (m_ShellView != IntPtr.Zero)
            {
                SendMessage(m_ShellView, WM_COMMAND, (IntPtr)SHVIEW_REPORT, (IntPtr)0);
                
                // Create explorer list view wrapper for event handling
                try
                {
                    m_Explorer = new ExplorerListView(m_ShellView);
                    m_Explorer.ItemExecuted += OnExplorerItemExecuted;
                }
                catch (Exception ex)
                {
                    // Explorer list view creation failed - not critical
                    System.Diagnostics.Debug.WriteLine($"ExplorerListView creation failed: {ex.Message}");
                }
            }
        }

        private int EnumChildren(IntPtr hwnd, IntPtr lParam)
        {
            int retval = 1;
            StringBuilder sb = new StringBuilder(SHELLVIEW_CLASS.Length + 1);
            int numChars = GetClassName(hwnd, sb, sb.Capacity);
            if (numChars == SHELLVIEW_CLASS.Length)
            {
                if (sb.ToString(0, numChars) == SHELLVIEW_CLASS)
                {
                    m_ShellView = hwnd;
                    retval = 0;
                }
            }
            return retval;
        }



        private void OnExplorerItemExecuted(object? sender, ExecuteEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(e.FilePath))
                {
                    // Get the current URL to construct the full path
                    if (this.Url != null)
                    {
                        string currentPath = this.Url.LocalPath;
                        string targetPath = Path.Combine(currentPath, e.FilePath);
                        
                        // Check if target is a directory
                        if (Directory.Exists(targetPath))
                        {
                            // Navigate to the folder within the same WebBrowser
                            this.Navigate("file:///" + targetPath.Replace('\\', '/'));
                        }
                        else if (File.Exists(targetPath))
                        {
                            // For files, we could open them or just ignore
                            System.Diagnostics.Debug.WriteLine($"File clicked: {targetPath}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Navigation error: {ex.Message}");
            }
            
            // Also notify external handlers
            ItemExecuted?.Invoke(this, e);
        }

        protected override void OnNewWindow(System.ComponentModel.CancelEventArgs e)
        {
            if (SuppressNewWindows)
            {
                e.Cancel = true;
                NavigationBlocked?.Invoke(this, "New window blocked");
            }
            base.OnNewWindow(e);
        }
    }

    // Event args for item execution
    public class ExecuteEventArgs : EventArgs
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        
        public ExecuteEventArgs(string filePath, string fileName)
        {
            FilePath = filePath;
            FileName = fileName;
        }
    }

    // Explorer ListView wrapper using NativeWindow
    internal class ExplorerListView : NativeWindow
    {
        public event EventHandler<ExecuteEventArgs>? ItemExecuted;

        private const int WM_NOTIFY = 0x004E;
        private const int NM_DBLCLK = -3;
        private const int LVM_GETITEMTEXT = 0x1000 + 45;

        [StructLayout(LayoutKind.Sequential)]
        private struct NMHDR
        {
            public IntPtr hwndFrom;
            public IntPtr idFrom;
            public int code;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct LVITEM
        {
            public uint mask;
            public int iItem;
            public int iSubItem;
            public uint state;
            public uint stateMask;
            public StringBuilder pszText;
            public int cchTextMax;
            public int iImage;
            public IntPtr lParam;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref LVITEM lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        private IntPtr m_ListView;

        public ExplorerListView(IntPtr shellViewHandle)
        {
            m_ListView = FindListView(shellViewHandle);
            if (m_ListView != IntPtr.Zero)
            {
                IntPtr parent = GetParent(m_ListView);
                if (parent != IntPtr.Zero)
                {
                    this.AssignHandle(parent);
                }
            }
        }

        private IntPtr FindListView(IntPtr shellView)
        {
            // In a real implementation, we would enumerate child windows to find the ListView
            // For now, we'll assume the shellView itself or direct child is the ListView
            return shellView;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NOTIFY && m_ListView != IntPtr.Zero)
            {
                try
                {
                    NMHDR nmhdr = (NMHDR)Marshal.PtrToStructure(m.LParam, typeof(NMHDR))!;
                    if (nmhdr.hwndFrom == m_ListView && nmhdr.code == NM_DBLCLK)
                    {
                        // Get selected item text
                        string? itemText = GetSelectedItemText();
                        if (!string.IsNullOrEmpty(itemText))
                        {
                            // Cancel the default behavior by not calling base.WndProc for this message
                            ItemExecuted?.Invoke(this, new ExecuteEventArgs(itemText, itemText));
                            return; // Don't call base.WndProc - prevents default folder opening
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"WndProc error: {ex.Message}");
                }
            }

            base.WndProc(ref m);
        }

        private string? GetSelectedItemText()
        {
            try
            {
                var item = new LVITEM
                {
                    mask = 0x0001, // LVIF_TEXT
                    iItem = 0,
                    iSubItem = 0,
                    pszText = new StringBuilder(260),
                    cchTextMax = 260
                };

                IntPtr result = SendMessage(m_ListView, LVM_GETITEMTEXT, IntPtr.Zero, ref item);
                return result != IntPtr.Zero ? item.pszText.ToString() : null;
            }
            catch
            {
                return null;
            }
        }
    }

    

    // COM interfaces and structures
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("bd3f23c0-d43e-11cf-893b-00aa00bdce1a")]
    public interface IDocHostUIHandler
    {
        [PreserveSig]
        int ShowContextMenu(int dwID, ref POINT pt, [In, MarshalAs(UnmanagedType.IUnknown)] object pcmdtReserved, [In, MarshalAs(UnmanagedType.IDispatch)] object pdispReserved);

        [PreserveSig]
        int GetHostInfo(ref DOCHOSTUIINFO info);

        [PreserveSig]
        int ShowUI(int dwID, [In, MarshalAs(UnmanagedType.IUnknown)] object activeObject, [In, MarshalAs(UnmanagedType.IUnknown)] object commandTarget, [In, MarshalAs(UnmanagedType.IUnknown)] object frame, [In, MarshalAs(UnmanagedType.IUnknown)] object doc);

        [PreserveSig]
        int HideUI();

        [PreserveSig]
        int UpdateUI();

        [PreserveSig]
        int EnableModeless([In, MarshalAs(UnmanagedType.Bool)] bool fEnable);

        [PreserveSig]
        int OnDocWindowActivate([In, MarshalAs(UnmanagedType.Bool)] bool fActivate);

        [PreserveSig]
        int OnFrameWindowActivate([In, MarshalAs(UnmanagedType.Bool)] bool fActivate);

        [PreserveSig]
        int ResizeBorder(ref RECT rect, [In, MarshalAs(UnmanagedType.IUnknown)] object doc, bool fFrameWindow);

        [PreserveSig]
        int TranslateAccelerator(ref MSG msg, ref Guid group, int nCmdID);

        [PreserveSig]
        int GetOptionKeyPath([Out, MarshalAs(UnmanagedType.LPWStr)] out string pbstrKey, int dw);

        [PreserveSig]
        int GetDropTarget([In, MarshalAs(UnmanagedType.IUnknown)] object pDropTarget, [Out, MarshalAs(UnmanagedType.IUnknown)] out object ppDropTarget);

        [PreserveSig]
        int GetExternal([Out, MarshalAs(UnmanagedType.IDispatch)] out object ppDispatch);

        [PreserveSig]
        int TranslateUrl(int dwTranslate, [In, MarshalAs(UnmanagedType.LPWStr)] string strURLIn, [Out, MarshalAs(UnmanagedType.LPWStr)] out string pstrURLOut);

        [PreserveSig]
        int FilterDataObject([In, MarshalAs(UnmanagedType.IUnknown)] object pDO, [Out, MarshalAs(UnmanagedType.IUnknown)] out object ppDORet);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DOCHOSTUIINFO
    {
        public uint cbSize;
        public DOCHOSTUIFLAG dwFlags;
        public DOCHOSTUIFLAG dwDoubleClick;
        [MarshalAs(UnmanagedType.BStr)] public string pchHostCss;
        [MarshalAs(UnmanagedType.BStr)] public string pchHostNS;
    }

    [Flags]
    public enum DOCHOSTUIFLAG : uint
    {
        DOCHOSTUIFLAG_DIALOG = 0x00000001,
        DOCHOSTUIFLAG_DISABLE_HELP_MENU = 0x00000002,
        DOCHOSTUIFLAG_NO3DBORDER = 0x00000004,
        DOCHOSTUIFLAG_SCROLL_NO = 0x00000008,
        DOCHOSTUIFLAG_DISABLE_SCRIPT_INACTIVE = 0x00000010,
        DOCHOSTUIFLAG_OPENNEWWIN = 0x00000020,
        DOCHOSTUIFLAG_DISABLE_OFFSCREEN = 0x00000040,
        DOCHOSTUIFLAG_FLAT_SCROLLBAR = 0x00000080,
        DOCHOSTUIFLAG_DIV_BLOCKDEFAULT = 0x00000100,
        DOCHOSTUIFLAG_ACTIVATE_CLIENTHIT_ONLY = 0x00000200,
        DOCHOSTUIFLAG_OVERRIDEBEHAVIORFACTORY = 0x00000400,
        DOCHOSTUIFLAG_CODEPAGELINKEDFONTS = 0x00000800,
        DOCHOSTUIFLAG_URL_ENCODING_DISABLE_UTF8 = 0x00001000,
        DOCHOSTUIFLAG_URL_ENCODING_ENABLE_UTF8 = 0x00002000,
        DOCHOSTUIFLAG_ENABLE_FORMS_AUTOCOMPLETE = 0x00004000,
        DOCHOSTUIFLAG_ENABLE_INPLACE_NAVIGATION = 0x00010000,
        DOCHOSTUIFLAG_IME_ENABLE_RECONVERSION = 0x00020000,
        DOCHOSTUIFLAG_THEME = 0x00040000,
        DOCHOSTUIFLAG_NOTHEME = 0x00080000,
        DOCHOSTUIFLAG_NOPICS = 0x00100000,
        DOCHOSTUIFLAG_NO3DOUTERBORDER = 0x00200000,
        DOCHOSTUIFLAG_DISABLE_EDIT_NS_FIXUP = 0x00400000,
        DOCHOSTUIFLAG_LOCAL_MACHINE_ACCESS_CHECK = 0x00800000,
        DOCHOSTUIFLAG_DISABLE_UNTRUSTEDPROTOCOL = 0x01000000,
        DOCHOSTUIFLAG_HOST_NAVIGATES = 0x02000000,
        DOCHOSTUIFLAG_ENABLE_REDIRECT_NOTIFICATION = 0x04000000,
        DOCHOSTUIFLAG_USE_WINDOWLESS_SELECTCONTROL = 0x08000000,
        DOCHOSTUIFLAG_USE_WINDOWED_SELECTCONTROL = 0x10000000,
        DOCHOSTUIFLAG_ENABLE_ACTIVEX_INACTIVATE_MODE = 0x20000000,
        DOCHOSTUIFLAG_DPI_AWARE = 0x40000000
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }
}