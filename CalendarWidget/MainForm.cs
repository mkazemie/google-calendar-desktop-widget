using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CalendarWidget;

public class MainForm : Form
{
    private const string CalendarUrl = "https://calendar.google.com/calendar/u/0/r";

    private readonly AppSettings settings = AppSettings.Load();
    private readonly bool firstRun = AppSettings.IsFirstRun;
    private readonly WebView2 webView = new() { Dock = DockStyle.Fill };
    private readonly NotifyIcon tray = new();
    private readonly System.Windows.Forms.Timer hoverPoll = new() { Interval = 100 };
    private readonly HoverPanel hoverPanel;
    private readonly TitleBar titleBar;
    private SettingsForm? settingsForm;

    private bool isClickThrough;
    private DateTime? hoverHideDeadline;  // set while the panel is visible but the mouse has left it
    private int guardTick;                // periodic re-assert of click-through styles
    private readonly HashSet<IntPtr> clickThroughApplied = [];  // windows WE made transparent

    public MainForm()
    {
        Text = "Google Calendar Widget";
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
            tray.BalloonTipTitle = "Calendar widget";
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
                "Calendar widget", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            // only this window's tree goes transparent; the owned TitleBar stays clickable
            NativeMethods.EnableClickThrough(Handle, clickThroughApplied);
            Opacity = settings.Transparency / 255.0;
            NativeMethods.SendToBottom(Handle);
        }
        else
        {
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
        // while keeping the WS_CAPTION/WS_THICKFRAME behaviors (snap, maximize)
        if (m.Msg == NativeMethods.WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
        {
            m.Result = IntPtr.Zero;
            return;
        }

        // maximize = exactly the work area of the monitor the window is currently on
        if (m.Msg == NativeMethods.WM_GETMINMAXINFO && m.LParam != IntPtr.Zero)
        {
            NativeMethods.FillMinMaxInfo(Handle, m.LParam);
            m.Result = IntPtr.Zero;
            return;
        }

        // borderless resize: the padding frame around the webview doubles as resize grips
        if (m.Msg == NativeMethods.WM_NCHITTEST && !isClickThrough)
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

    public void SetTransparency(int alpha)
    {
        settings.Transparency = Math.Clamp(alpha, 80, 255);
        if (isClickThrough)
            Opacity = settings.Transparency / 255.0;
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

        // every ~2s, re-assert click-through on the whole child tree: Chrome can recreate
        // its input windows (renderer restart, navigation) and WinForms style updates can
        // wipe manually-set ex-styles. AddExStyle no-ops when the bit is already set.
        if (++guardTick >= 20)
        {
            guardTick = 0;
            NativeMethods.EnableClickThrough(Handle, clickThroughApplied);
        }

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
        tray.Text = "Google Calendar widget";
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
        if (isClickThrough)
            SaveBounds();
        settings.Save();
        tray.Visible = false;
        tray.Dispose();
        base.OnFormClosed(e);
    }
}
