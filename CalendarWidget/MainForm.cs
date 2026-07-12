using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CalendarWidget;

public class MainForm : Form
{
    private const string CalendarUrl = "https://calendar.google.com/calendar/u/0/r";

    private readonly AppSettings settings = AppSettings.Load();
    private readonly bool firstRun = AppSettings.IsFirstRun;
    private readonly WebView2 webView = new()
    {
        Dock = DockStyle.Fill,
        DefaultBackgroundColor = Color.FromArgb(32, 33, 36),  // no white flash while pages load
    };
    private readonly NotifyIcon tray = new();
    private readonly System.Windows.Forms.Timer hoverPoll = new() { Interval = 100 };
    private readonly HoverPanel hoverPanel;
    private readonly TitleBar titleBar;
    private SettingsForm? settingsForm;

    private bool isClickThrough;
    private DateTime? hoverHideDeadline;  // set while the panel is visible but the mouse has left it
    private int guardTick;                // periodic re-assert of click-through styles
    private int styleBurst;               // fast re-assert right after enabling (Chrome recreates windows)
    private int attachCheckTick;          // throttle for the behind-icons re-attach watchdog
    private bool attachedToDesktop;       // reparented into WorkerW (behind the desktop icons)
    private IntPtr attachedWorkerW;       // the specific WorkerW we parented into; its destruction = orphaned
    private Rectangle boundsBeforeAttach; // screen bounds to restore on detach (child coords are parent-relative)
    private bool maximizedBeforeAttach;   // attached windows are always Normal; re-maximize on detach
    private Rectangle normalBoundsBeforeAttach;  // the pre-maximize rect to give back to RestoreBounds
    private readonly HashSet<IntPtr> clickThroughApplied = [];  // windows WE made transparent

    public MainForm()
    {
        Text = "Google Calendar Desktop Widget";
        FormBorderStyle = FormBorderStyle.None;  // always borderless; TitleBar is the caption in interactive mode
        BackColor = Color.FromArgb(32, 33, 36);  // shows as the frame around the webview in interactive mode
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = InitialBounds();
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* keep default */ }

        // the bar is a separate owned window floating over the form's top edge; the padding
        // reserves that strip so the calendar never renders underneath it
        titleBar = new TitleBar(this, ToggleClickThrough, ShowSettings, ToggleMaximize);
        Padding = new Padding(0, TitleBar.BarHeight, 0, 0);
        Controls.Add(webView);

        hoverPanel = new HoverPanel(ToggleClickThrough, ShowSettings);

        SetupTray();
        hoverPoll.Tick += (_, _) => CheckMouseHover();
        hoverPoll.Start();
    }

    // native window behaviors (Aero Snap, drag-to-top / double-click maximize) require these
    // styles; the frame and caption they'd normally draw are removed in WM_NCCALCSIZE below
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME | NativeMethods.WS_MAXIMIZEBOX;
            return cp;
        }
    }

    public void ToggleMaximize() =>
        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        titleBar?.Reposition();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        titleBar?.Reposition();
    }

    private Rectangle InitialBounds()
    {
        if (settings.WinW > 0)
            return new Rectangle(settings.WinX, settings.WinY, settings.WinW, settings.WinH);

        // default: right side of the primary work area
        var wa = Screen.PrimaryScreen!.WorkingArea;
        int w = Math.Min(1000, wa.Width / 2);
        return new Rectangle(wa.Right - w - 20, wa.Top + 20, w, wa.Height - 40);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        titleBar.Show(this);  // owned: always floats directly above the main window
        titleBar.Reposition();

        // first run: stay interactive so the user can sign in to Google;
        // afterwards the widget always starts in click-through mode
        SetClickThrough(!firstRun);
        if (firstRun)
        {
            tray.BalloonTipTitle = "Google Calendar Desktop Widget";
            tray.BalloonTipText = "Sign in to Google Calendar, then use the tray menu or the "
                + "hover panel (bottom-right of the screen) to enable click-through mode.";
            tray.ShowBalloonTip(10000);
        }

        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, AppSettings.WebViewDataFolder);
            await webView.EnsureCoreWebView2Async(env);
            webView.CoreWebView2.Navigate(CalendarUrl);

            // WebView2's child windows didn't exist during the startup toggle; cover them now
            if (isClickThrough)
                NativeMethods.EnableClickThrough(Handle, clickThroughApplied);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to start the embedded browser (WebView2 runtime missing?):\n\n" + ex.Message,
                "Google Calendar Desktop Widget", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Exit();
        }
    }

    // ---------------- click-through / interactive ----------------

    public void ToggleClickThrough() => SetClickThrough(!isClickThrough);

    private void SetClickThrough(bool enable)
    {
        isClickThrough = enable;
        if (enable)
        {
            SaveBounds();  // no-op while maximized: the last normal bounds stay saved
            Padding = new Padding(0, TitleBar.BarHeight, 0, 0);  // widget mode: no resize frame
            NativeMethods.AddExStyle(Handle, NativeMethods.WS_EX_NOACTIVATE);
            // opacity first: going layered makes Chrome recreate its input windows, which
            // would shed a just-applied WS_EX_TRANSPARENT. Then style the whole tree and
            // keep re-asserting on every poll tick (~100ms) for the next ~2s to catch any
            // window Chrome recreates asynchronously — only this window's tree goes
            // transparent; the owned TitleBar stays clickable.
            Opacity = settings.Transparency / 255.0;
            NativeMethods.EnableClickThrough(Handle, clickThroughApplied);
            styleBurst = 20;
            NativeMethods.SendToBottom(Handle);
            if (settings.BehindDesktopIcons)
                AttachToDesktop();
        }
        else
        {
            DetachFromDesktop();  // interactive mode is always a normal top-level window
            styleBurst = 0;
            NativeMethods.DisableClickThrough(clickThroughApplied);
            NativeMethods.RemoveExStyle(Handle, NativeMethods.WS_EX_NOACTIVATE);
            Opacity = 1.0;
            Padding = new Padding(6, TitleBar.BarHeight, 6, 6);  // side/bottom frame = resize grips
            hoverPanel.Hide();
            Activate();
        }
        hoverPanel.UpdateState(enable);
        titleBar.UpdateState(enable);
        titleBar.Reposition();
        settings.Save();
    }

    // pin the window to the bottom of the Z-order: any attempt to raise it
    // (taskbar click, alt-tab, shell repositioning) is rewritten to HWND_BOTTOM
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_WINDOWPOSCHANGING && isClickThrough && m.LParam != IntPtr.Zero)
        {
            var wp = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(m.LParam);
            wp.hwndInsertAfter = NativeMethods.HWND_BOTTOM;
            wp.flags &= ~NativeMethods.SWP_NOZORDER;
            Marshal.StructureToPtr(wp, m.LParam, false);
        }

        // claim the caption/frame area as client: removes the native title bar visuals
        // while keeping the WS_CAPTION/WS_THICKFRAME behaviors (snap, maximize).
        // When maximized, Windows hangs the invisible frame outside the monitor, so the
        // client must be inset by the frame thickness or content edges get cut off.
        if (m.Msg == NativeMethods.WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
        {
            if (NativeMethods.IsZoomed(Handle) && m.LParam != IntPtr.Zero)
            {
                var rc = Marshal.PtrToStructure<NativeMethods.RECT>(m.LParam);
                var (fx, fy) = NativeMethods.MaximizedFrameThickness(Handle);
                rc.Left += fx;
                rc.Top += fy;
                rc.Right -= fx;
                rc.Bottom -= fy;
                Marshal.StructureToPtr(rc, m.LParam, false);
            }
            m.Result = IntPtr.Zero;
            return;
        }

        // borderless resize: the padding frame around the webview doubles as resize grips
        if (m.Msg == NativeMethods.WM_NCHITTEST && !isClickThrough && !NativeMethods.IsZoomed(Handle))
        {
            base.WndProc(ref m);
            if ((int)m.Result == NativeMethods.HTCLIENT)
            {
                int x = unchecked((short)(long)m.LParam);
                int y = unchecked((short)((long)m.LParam >> 16));
                var pt = PointToClient(new Point(x, y));
                const int grip = 8;
                bool left = pt.X < grip, right = pt.X >= ClientSize.Width - grip;
                bool top = pt.Y < grip, bottom = pt.Y >= ClientSize.Height - grip;
                int hit =
                    top && left ? NativeMethods.HTTOPLEFT :
                    top && right ? NativeMethods.HTTOPRIGHT :
                    bottom && left ? NativeMethods.HTBOTTOMLEFT :
                    bottom && right ? NativeMethods.HTBOTTOMRIGHT :
                    left ? NativeMethods.HTLEFT :
                    right ? NativeMethods.HTRIGHT :
                    top ? NativeMethods.HTTOP :
                    bottom ? NativeMethods.HTBOTTOM :
                    NativeMethods.HTCLIENT;
                m.Result = new IntPtr(hit);
            }
            return;
        }

        // a display-config change (resolution, monitor add/remove/power) recreates the
        // shell's WorkerW; re-attach promptly rather than waiting for the 1s watchdog
        if (m.Msg == NativeMethods.WM_DISPLAYCHANGE && settings.BehindDesktopIcons && isClickThrough)
            BeginInvoke(EnsureDesktopAttachment);

        base.WndProc(ref m);
    }

    // WinForms recreates the handle for some property changes; re-apply manual styles if so
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.ApplyRoundedCorners(Handle);  // borderless windows get square corners by default
        if (isClickThrough)
        {
            NativeMethods.AddExStyle(Handle, NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_NOACTIVATE);
            NativeMethods.SendToBottom(Handle);
        }
    }

    public void SetTransparencyPercent(int percent)
    {
        settings.TransparencyPercent = percent;
        if (isClickThrough)
            Opacity = settings.Transparency / 255.0;
        settings.Save();
    }

    // ---------------- live-wallpaper mode (behind desktop icons) ----------------

    /// <summary>Reparent into the shell's WorkerW layer so the widget renders UNDER the desktop icons.</summary>
    private void AttachToDesktop()
    {
        if (attachedToDesktop)
            return;
        IntPtr workerW = NativeMethods.FindDesktopWorkerW();
        if (workerW == IntPtr.Zero)
            return;  // shell didn't cooperate; stay a normal bottom-pinned window

        NativeMethods.SetRedraw(Handle, false);  // freeze painting so the DPI rescale doesn't flash

        // a child window must not be "maximized": drop to Normal sized to the work area
        // (no invisible-frame overhang, no WM_NCCALCSIZE maximize inset) and re-maximize
        // on detach. RestoreBounds keeps the pre-maximize rect for later.
        maximizedBeforeAttach = WindowState == FormWindowState.Maximized;
        if (maximizedBeforeAttach)
        {
            normalBoundsBeforeAttach = RestoreBounds;
            var wa = Screen.FromControl(this).WorkingArea;
            WindowState = FormWindowState.Normal;
            Bounds = wa;
        }
        boundsBeforeAttach = Bounds;

        uint dpiBefore = NativeMethods.GetDpiForWindow(Handle);
        var pt = boundsBeforeAttach.Location;
        NativeMethods.SetParent(Handle, workerW);
        NativeMethods.ScreenToClient(workerW, ref pt);  // child coordinates are WorkerW-relative
        NativeMethods.SetWindowPos(Handle, IntPtr.Zero, pt.X, pt.Y,
            boundsBeforeAttach.Width, boundsBeforeAttach.Height, NativeMethods.SWP_NOZORDER_NOACTIVATE);
        attachedToDesktop = true;
        attachedWorkerW = workerW;

        // reparenting swaps the DPI context to the primary monitor's: WebView2 re-zooms
        // and WinForms may rescale layout. After the DPI messages drain, compensate the
        // browser zoom and re-assert physical geometry so nothing shifts or resizes, then
        // resume painting in one clean repaint (no visible zoom-in/zoom-out).
        BeginInvoke(() =>
        {
            if (!attachedToDesktop)
            {
                NativeMethods.SetRedraw(Handle, true);
                return;
            }
            uint dpiAfter = Math.Max(1, NativeMethods.GetDpiForWindow(Handle));
            try { webView.ZoomFactor = dpiBefore / (double)dpiAfter; } catch { }
            var p2 = boundsBeforeAttach.Location;
            NativeMethods.ScreenToClient(workerW, ref p2);
            NativeMethods.SetWindowPos(Handle, IntPtr.Zero, p2.X, p2.Y,
                boundsBeforeAttach.Width, boundsBeforeAttach.Height, NativeMethods.SWP_NOZORDER_NOACTIVATE);
            Padding = new Padding(0, TitleBar.BarHeight, 0, 0);
            titleBar.Reposition();
            NativeMethods.SetRedraw(Handle, true);
            NativeMethods.RepaintAll(Handle);
        });
    }

    private void DetachFromDesktop()
    {
        if (!attachedToDesktop)
            return;
        NativeMethods.SetRedraw(Handle, false);
        NativeMethods.SetParent(Handle, IntPtr.Zero);
        attachedToDesktop = false;
        attachedWorkerW = IntPtr.Zero;
        try { webView.ZoomFactor = 1.0; } catch { }
        Bounds = boundsBeforeAttach;
        if (maximizedBeforeAttach)
        {
            Bounds = normalBoundsBeforeAttach;  // seed RestoreBounds with the original rect
            WindowState = FormWindowState.Maximized;
            maximizedBeforeAttach = false;
        }
        // same DPI-context swap in reverse; re-assert geometry after messages drain
        BeginInvoke(() =>
        {
            if (attachedToDesktop)
            {
                NativeMethods.SetRedraw(Handle, true);
                return;
            }
            if (WindowState == FormWindowState.Normal)
                Bounds = boundsBeforeAttach;
            Padding = isClickThrough ? new Padding(0, TitleBar.BarHeight, 0, 0) : new Padding(6, TitleBar.BarHeight, 6, 6);
            titleBar.Reposition();
            NativeMethods.SetRedraw(Handle, true);
            NativeMethods.RepaintAll(Handle);
        });
    }

    /// <summary>
    /// Watchdog: the shell recreates its WorkerW on display changes / monitor power events
    /// (common with multiple monitors), which orphans our reparented window — it survives
    /// but floats at stale WorkerW-relative coordinates, so it appears to vanish. Detect the
    /// orphaning and re-attach to the fresh WorkerW. Runs from the 100 ms poll, throttled.
    ///
    /// The orphaning signal is the DESTRUCTION of the WorkerW we attached to
    /// (`IsWindow(attachedWorkerW)`), NOT `GetParent(Handle)`: a WinForms window keeps its
    /// top-level style after SetParent, so GetParent returns 0 even while correctly parented
    /// — checking it would re-attach every tick and flicker the WebView white.
    /// </summary>
    private void EnsureDesktopAttachment()
    {
        if (!settings.BehindDesktopIcons || !isClickThrough)
            return;
        if (attachedToDesktop && NativeMethods.IsWindow(attachedWorkerW))
            return;  // the WorkerW we're parented into is still alive — nothing to do

        if (attachedToDesktop)
        {
            // our WorkerW was destroyed: return to a valid top-level state at known-good bounds
            NativeMethods.SetParent(Handle, IntPtr.Zero);
            attachedToDesktop = false;
            attachedWorkerW = IntPtr.Zero;
            Bounds = boundsBeforeAttach;
        }
        AttachToDesktop();
    }

    public void SetBehindDesktopIcons(bool behind)
    {
        settings.BehindDesktopIcons = behind;
        if (isClickThrough)
        {
            if (behind)
            {
                AttachToDesktop();
            }
            else
            {
                DetachFromDesktop();
                NativeMethods.SendToBottom(Handle);
            }
        }
        settings.Save();
    }

    public void SetCornerPanelEnabled(bool enabled)
    {
        settings.CornerPanelEnabled = enabled;
        if (!enabled)
            hoverPanel.Hide();
        settings.Save();
    }

    // ---------------- hover panel ----------------

    private Rectangle GetPanelRect()
    {
        var wa = Screen.FromControl(this).WorkingArea;
        int x = settings.PanelCorner is "BottomLeft" or "TopLeft"
            ? wa.Left + 40
            : wa.Right - HoverPanel.PanelW - 40;
        int y = settings.PanelCorner is "TopLeft" or "TopRight"
            ? wa.Top + 30
            : wa.Bottom - HoverPanel.PanelH - 30;
        return new Rectangle(x, y, HoverPanel.PanelW, HoverPanel.PanelH);
    }

    public void SetPanelCorner(string corner)
    {
        settings.PanelCorner = corner;
        settings.Save();
        if (hoverPanel.Visible)
            hoverPanel.Hide();  // next hover poll re-shows it at the new corner
    }

    private void CheckMouseHover()
    {
        // interactive mode: controls are integrated in the title bar, no corner-hover logic
        if (!isClickThrough)
            return;

        // ~every 1s, make sure we're still behind the icons (the shell can orphan us)
        if (settings.BehindDesktopIcons && ++attachCheckTick >= 10)
        {
            attachCheckTick = 0;
            EnsureDesktopAttachment();
        }

        // right after enabling: re-assert on every tick until the burst runs out, so
        // windows Chrome recreates for layered rendering go transparent within ~100ms
        if (styleBurst > 0)
        {
            styleBurst--;
            NativeMethods.EnableClickThrough(Handle, clickThroughApplied);
        }
        // every ~2s after that: Chrome can still recreate input windows (renderer restart,
        // navigation) and WinForms style updates can wipe manually-set ex-styles.
        // EnableClickThrough no-ops for windows that already carry the style.
        else if (++guardTick >= 20)
        {
            guardTick = 0;
            NativeMethods.EnableClickThrough(Handle, clickThroughApplied);
        }

        if (!settings.CornerPanelEnabled)
            return;

        var rect = GetPanelRect();

        if (rect.Contains(Cursor.Position))
        {
            hoverHideDeadline = null;
            if (!hoverPanel.Visible)
            {
                hoverPanel.Location = rect.Location;
                hoverPanel.Show();  // no-activate: HoverPanel overrides ShowWithoutActivation
            }
        }
        else if (hoverPanel.Visible)
        {
            hoverHideDeadline ??= DateTime.Now.AddSeconds(2);
            if (DateTime.Now >= hoverHideDeadline)
            {
                hoverPanel.Hide();
                hoverHideDeadline = null;
            }
        }
    }

    // ---------------- Google account ----------------

    /// <summary>Signed-in check: Google's session cookies exist in the widget's profile.</summary>
    public async Task<bool> IsSignedInAsync()
    {
        if (webView.CoreWebView2 is null)
            return false;
        var cookies = await webView.CoreWebView2.CookieManager.GetCookiesAsync("https://calendar.google.com");
        return cookies.Any(c => c.Name is "SID" or "__Secure-1PSID" or "__Secure-3PSID");
    }

    /// <summary>Clear the embedded browser profile (cookies, storage) and return to the sign-in page.</summary>
    public async Task SignOutAsync()
    {
        if (webView.CoreWebView2 is null)
            return;
        await webView.CoreWebView2.Profile.ClearBrowsingDataAsync();
        EnsureInteractive();  // show the signed-out page so the user can sign back in
        webView.CoreWebView2.Navigate(CalendarUrl);
    }

    /// <summary>
    /// Sign out via Google's logout endpoint, keeping the account on Google's chooser
    /// so signing back in doesn't require retyping the address (often just the password).
    /// </summary>
    public async Task SignOutKeepAccountAsync()
    {
        if (webView.CoreWebView2 is null)
            return;
        EnsureInteractive();
        webView.CoreWebView2.Navigate("https://accounts.google.com/Logout");
        await Task.Delay(2500);  // let the logout round-trip settle before heading back
        webView.CoreWebView2.Navigate(CalendarUrl);
    }

    /// <summary>Bring the widget to interactive mode on the calendar page, which redirects to Google sign-in.</summary>
    public void BeginSignIn()
    {
        EnsureInteractive();
        webView.CoreWebView2?.Navigate(CalendarUrl);
    }

    private void EnsureInteractive()
    {
        if (isClickThrough)
            SetClickThrough(false);
        Activate();
    }

    // ---------------- tray + settings ----------------

    private void SetupTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Toggle interaction", null, (_, _) => ToggleClickThrough());
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Application.Exit());

        tray.Icon = Icon ?? SystemIcons.Application;
        tray.Text = "Google Calendar Desktop Widget";
        tray.ContextMenuStrip = menu;
        tray.Visible = true;
        tray.DoubleClick += (_, _) => ShowSettings();
    }

    public void ShowSettings()
    {
        settingsForm ??= new SettingsForm(this, settings);
        settingsForm.Show();
        settingsForm.Activate();
    }

    private void SaveBounds()
    {
        if (WindowState != FormWindowState.Normal)
            return;
        settings.WinX = Bounds.X;
        settings.WinY = Bounds.Y;
        settings.WinW = Bounds.Width;
        settings.WinH = Bounds.Height;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        hoverPoll.Stop();
        // while attached, Bounds are WorkerW-relative and the window can't have moved
        // since attach anyway — the entering-widget-mode SaveBounds already captured them
        if (isClickThrough && !attachedToDesktop)
            SaveBounds();
        settings.Save();
        tray.Visible = false;
        tray.Dispose();
        base.OnFormClosed(e);
    }
}
