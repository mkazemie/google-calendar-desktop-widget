// Google Calendar Desktop Widget
// Copyright (C) 2026 Mahdi Kazemiesfahani
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace CalendarWidget;

/// <summary>
/// The widget's title bar, visible in BOTH modes. It is a separate top-level window
/// owned by MainForm (owned windows always float directly above their owner), so it is
/// never subject to the click-through styling applied to the main window's tree — the
/// controls keep working while the calendar underneath passes clicks to the desktop.
/// Dragging it forwards a native caption drag to the owner, so Aero Snap works.
/// In widget mode the window clips itself (Region) to just its buttons: the rest of the
/// strip shows the main window beneath and passes clicks through like the calendar does.
/// </summary>
public class TitleBar : Form
{
    public const int BarHeight = 34;
    private const int BtnW = 46;

    private static readonly Color BarBack = Color.FromArgb(32, 33, 36);
    private static readonly Color HoverBack = Color.FromArgb(60, 64, 67);
    private static readonly Color CloseHover = Color.FromArgb(196, 43, 28);  // Win11 close-button red
    private static readonly Color Fore = Color.FromArgb(232, 234, 237);
    private static readonly Color IconActive = Color.FromArgb(138, 180, 248);

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 2;

    private readonly Form owner;
    private readonly Action onToggleMaximize;
    private readonly Button btnToggle;
    private readonly Button btnMenu;
    private readonly Button btnClose;
    private bool clickThrough;

    // the bar must never steal focus from the owner or anything else
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= unchecked((int)NativeMethods.WS_EX_NOACTIVATE);
            return cp;
        }
    }

    public TitleBar(Form owner, Action onToggle, Action onSettings, Action onToggleMaximize)
    {
        this.owner = owner;
        this.onToggleMaximize = onToggleMaximize;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        BackColor = BarBack;
        Height = BarHeight;

        var iconBox = new PictureBox
        {
            Bounds = new Rectangle(12, (BarHeight - 16) / 2, 16, 16),
            SizeMode = PictureBoxSizeMode.Zoom,
        };
        try { iconBox.Image = Icon.ExtractAssociatedIcon(Application.ExecutablePath)?.ToBitmap(); } catch { }

        var title = new Label
        {
            Text = "Google Calendar Desktop Widget",
            ForeColor = Fore,
            Font = new Font("Segoe UI", 9.5f),
            AutoSize = true,
            Location = new Point(36, (BarHeight - 17) / 2),
            BackColor = Color.Transparent,
        };

        var iconFont = HoverPanel.CreateIconFont(10f);
        btnToggle = MakeButton("", iconFont, HoverBack);      // mouse: toggle click-through
        btnMenu = MakeButton("", iconFont, HoverBack);        // hamburger: settings
        btnClose = MakeButton("", iconFont, CloseHover);  // ChromeClose glyph
        btnToggle.Click += (_, _) => onToggle();
        btnMenu.Click += (_, _) => onSettings();
        btnClose.Click += (_, _) => Application.Exit();

        var tips = new ToolTip();
        tips.SetToolTip(btnToggle, "Toggle click-through (pass clicks to the desktop or not)");
        tips.SetToolTip(btnMenu, "Settings");
        tips.SetToolTip(btnClose, "Exit widget");

        Controls.AddRange([iconBox, title, btnToggle, btnMenu, btnClose]);

        // right-align the buttons whenever the bar resizes with the window
        Resize += (_, _) => LayoutButtons();

        // dragging the bar (or the title/icon on it) moves the owner window
        MouseDown += StartDrag;
        title.MouseDown += StartDrag;
        iconBox.MouseDown += StartDrag;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.ApplyRoundedCorners(Handle);
    }

    /// <summary>
    /// Snap the bar onto the top edge of the owner window. Called for every move during a
    /// drag, so it uses raw SetWindowPos — the WinForms Bounds setter would run the full
    /// managed layout pipeline per pixel and make dragging visibly laggy.
    /// </summary>
    public void Reposition()
    {
        if (!IsHandleCreated || !owner.IsHandleCreated)
            return;
        // track the CLIENT origin, not Bounds: when maximized the window rect overhangs
        // the monitor by the invisible frame, but the client rect stays fully visible
        var origin = owner.PointToScreen(Point.Empty);
        NativeMethods.SetWindowPos(Handle, IntPtr.Zero, origin.X, origin.Y, owner.ClientSize.Width, BarHeight,
            NativeMethods.SWP_NOZORDER_NOACTIVATE);
    }

    /// <summary>
    /// Widget mode: tint the mouse icon; hide close; disable drag/maximize; shrink to the
    /// buttons and take the widget's opacity so the bar reads as part of the calendar.
    /// </summary>
    public void UpdateState(bool clickThrough, double widgetOpacity)
    {
        this.clickThrough = clickThrough;
        btnToggle.ForeColor = clickThrough ? IconActive : Fore;
        btnClose.Visible = !clickThrough;
        LayoutButtons();
        Opacity = clickThrough ? widgetOpacity : 1.0;
    }

    private void LayoutButtons()
    {
        // decided by mode, not Button.Visible: that reads false until the bar is first shown
        Button[] shown = clickThrough ? [btnToggle, btnMenu] : [btnToggle, btnMenu, btnClose];
        for (int i = 0; i < shown.Length; i++)
            shown[i].Location = new Point(Width - (shown.Length - i) * BtnW, 0);

        // widget mode: the window is ONLY its buttons — outside them hit-testing falls
        // through to whatever is below (the click-through calendar, desktop icons in
        // live-wallpaper mode). A region, not HTTRANSPARENT: that only forwards clicks to
        // windows on this thread, never to the desktop.
        Region = clickThrough
            ? new Region(new Rectangle(Width - shown.Length * BtnW, 0, shown.Length * BtnW, BarHeight))
            : null;
    }

    private Button MakeButton(string glyph, Font font, Color hover)
    {
        var b = new Button
        {
            Size = new Size(BtnW, BarHeight),
            Text = glyph,
            Font = font,
            FlatStyle = FlatStyle.Flat,
            BackColor = BarBack,
            ForeColor = Fore,
            TabStop = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = hover;
        b.FlatAppearance.MouseDownBackColor = hover;
        return b;
    }

    private void StartDrag(object? sender, MouseEventArgs e)
    {
        // widget mode: the bar only hosts the toggle/settings buttons — the pinned
        // widget must not be movable or maximizable by accident
        if (clickThrough || e.Button != MouseButtons.Left)
            return;
        if (e.Clicks == 2)
        {
            onToggleMaximize();  // native caption double-click behavior
            return;
        }
        // native caption drag on the OWNER: gives live move + Aero Snap (drag-to-top/edges)
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(owner.Handle, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
    }
}
