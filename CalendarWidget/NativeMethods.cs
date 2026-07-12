using System.Runtime.InteropServices;

namespace CalendarWidget;

internal static class NativeMethods
{
    public const int GWL_EXSTYLE = -20;

    public const long WS_EX_TRANSPARENT = 0x00000020;  // mouse passes through
    public const long WS_EX_NOACTIVATE = 0x08000000;   // never steals keyboard focus

    public const int WM_WINDOWPOSCHANGING = 0x0046;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOSIZE_NOMOVE_NOACTIVATE = 0x0013;

    public static readonly IntPtr HWND_BOTTOM = new(1);

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    public static void AddExStyle(IntPtr hwnd, long style)
    {
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        if ((ex & style) != style)
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex | style));
    }

    public static void RemoveExStyle(IntPtr hwnd, long style)
    {
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        if ((ex & style) != 0)
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex & ~style));
    }

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    /// <summary>
    /// Input hit-testing descends into child HWNDs. WebView2 hosts its input in its own
    /// child window tree (Chrome_WidgetWin_*), so unlike the Chrome PWA case (top-level
    /// handles all input), WS_EX_TRANSPARENT must be on EVERY window in the hit path.
    /// Windows that gain the style are recorded in <paramref name="applied"/> — some of
    /// Chrome's windows (e.g. the Intermediate D3D compositing surface) carry
    /// WS_EX_TRANSPARENT natively, and stripping it from them blanks the rendering.
    /// </summary>
    public static void EnableClickThrough(IntPtr root, ISet<IntPtr> applied)
    {
        Apply(root);
        EnumChildWindows(root, (h, _) => { Apply(h); return true; }, IntPtr.Zero);

        void Apply(IntPtr h)
        {
            long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
            if ((ex & WS_EX_TRANSPARENT) == 0)
            {
                SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex | WS_EX_TRANSPARENT));
                applied.Add(h);
            }
        }
    }

    /// <summary>Remove WS_EX_TRANSPARENT only from the windows EnableClickThrough added it to.</summary>
    public static void DisableClickThrough(ISet<IntPtr> applied)
    {
        foreach (var h in applied)
            RemoveExStyle(h, WS_EX_TRANSPARENT);  // destroyed handles fail silently, which is fine
        applied.Clear();
    }

    public static void SendToBottom(IntPtr hwnd) =>
        SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOSIZE_NOMOVE_NOACTIVATE);

    // custom title bar: window dragging + resize hit-test codes
    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public const int WM_NCHITTEST = 0x84;
    public const int WM_NCCALCSIZE = 0x83;
    public const int WM_GETMINMAXINFO = 0x24;
    public const int HTCLIENT = 1;
    public const uint SWP_NOZORDER_NOACTIVATE = 0x0014;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

    /// <summary>
    /// Fill MINMAXINFO so a maximized borderless window exactly covers the work area of
    /// the monitor it is on. Positions are relative to that monitor's origin — this is
    /// the multi-monitor-correct recipe (Form.MaximizedBounds gets secondary screens wrong).
    /// </summary>
    public static void FillMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2 /*MONITOR_DEFAULTTONEAREST*/), ref mi))
            return;
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        mmi.ptMaxPosition.X = mi.rcWork.Left - mi.rcMonitor.Left;
        mmi.ptMaxPosition.Y = mi.rcWork.Top - mi.rcMonitor.Top;
        mmi.ptMaxSize.X = mi.rcWork.Right - mi.rcWork.Left;
        mmi.ptMaxSize.Y = mi.rcWork.Bottom - mi.rcWork.Top;
        Marshal.StructureToPtr(mmi, lParam, false);
    }

    // window styles that enable native behaviors (Aero Snap, drag-to-top maximize)
    // on an otherwise borderless window; the visual frame is removed via WM_NCCALCSIZE
    public const int WS_CAPTION = 0x00C00000;
    public const int WS_THICKFRAME = 0x00040000;
    public const int WS_MAXIMIZEBOX = 0x00010000;
    public const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;
    public const int HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // Win11 niceties; both calls fail harmlessly (square corners / light titlebar) on older builds
    public static void ApplyRoundedCorners(IntPtr hwnd)
    {
        int pref = 2;  // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, 33, ref pref, sizeof(int));  // DWMWA_WINDOW_CORNER_PREFERENCE
    }

    public static void ApplyDarkTitleBar(IntPtr hwnd)
    {
        int on = 1;
        DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));  // DWMWA_USE_IMMERSIVE_DARK_MODE
    }
}
