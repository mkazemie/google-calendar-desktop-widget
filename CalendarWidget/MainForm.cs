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
    private SettingsForm? settingsForm;

    private bool isClickThrough;
    private DateTime? hoverHideDeadline;  // set while the panel is visible but the mouse has left it
    private int guardTick;                // periodic re-assert of click-through styles
    private readonly HashSet<IntPtr> clickThroughApplied = [];  // windows WE made transparent

    public MainForm()
    {
        Text = "Google Calendar Widget";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = InitialBounds();

        Controls.Add(webView);
        hoverPanel = new HoverPanel(ToggleClickThrough, ShowSettings);

        SetupTray();
        hoverPoll.Tick += (_, _) => CheckMouseHover();
        hoverPoll.Start();
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
            webView.CoreWebView2.NavigationCompleted += (_, _) => ApplyDim();
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
            // capture bounds while still bordered so the sizable frame is what the user resized
            FormBorderStyle = FormBorderStyle.None;
            SaveBounds();
            NativeMethods.AddExStyle(Handle, NativeMethods.WS_EX_NOACTIVATE);
            NativeMethods.EnableClickThrough(Handle, clickThroughApplied);  // form + WebView2 child windows
            Opacity = settings.Transparency / 255.0;
            NativeMethods.SendToBottom(Handle);
        }
        else
        {
            NativeMethods.DisableClickThrough(clickThroughApplied);
            NativeMethods.RemoveExStyle(Handle, NativeMethods.WS_EX_NOACTIVATE);
            Opacity = 1.0;
            FormBorderStyle = FormBorderStyle.Sizable;  // titlebar lets the user move/resize
            Activate();
        }
        hoverPanel.UpdateState(enable);
        ApplyDim();
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
        base.WndProc(ref m);
    }

    // WinForms recreates the handle for some property changes; re-apply manual styles if so
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (isClickThrough)
        {
            NativeMethods.AddExStyle(Handle, NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_NOACTIVATE);
            NativeMethods.SendToBottom(Handle);
        }
    }

    // ---------------- dim (poor man's dark mode) ----------------

    // Instead of the AHK overlay window, darken the page itself: we own the browser,
    // so a CSS brightness filter does the job with no extra window to keep aligned.
    public async void ApplyDim()
    {
        if (webView.CoreWebView2 is null)
            return;
        int dim = isClickThrough ? settings.Dim : 0;
        string filter = dim > 0 ? $"brightness({100 - dim}%)" : "";
        try
        {
            await webView.ExecuteScriptAsync($"document.documentElement.style.filter = '{filter}';");
        }
        catch
        {
            // navigation raced the script; NavigationCompleted will re-apply
        }
    }

    public void SetTransparency(int alpha)
    {
        settings.Transparency = Math.Clamp(alpha, 80, 255);
        if (isClickThrough)
            Opacity = settings.Transparency / 255.0;
        settings.Save();
    }

    public void SetDim(int dim)
    {
        settings.Dim = Math.Clamp(dim, 0, 90);
        ApplyDim();
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
        // every ~2s, re-assert click-through on the whole child tree: Chrome can recreate
        // its input windows (renderer restart, navigation) and WinForms style updates can
        // wipe manually-set ex-styles. AddExStyle no-ops when the bit is already set.
        if (++guardTick >= 20)
        {
            guardTick = 0;
            if (isClickThrough)
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

    // ---------------- tray + settings ----------------

    private void SetupTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Toggle interaction", null, (_, _) => ToggleClickThrough());
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Application.Exit());

        tray.Icon = System.Drawing.SystemIcons.Application;
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
