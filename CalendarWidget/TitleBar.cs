namespace CalendarWidget;

/// <summary>
/// The widget's title bar, visible in BOTH modes. It is a separate top-level window
/// owned by MainForm (owned windows always float directly above their owner), so it is
/// never subject to the click-through styling applied to the main window's tree — the
/// controls keep working while the calendar underneath passes clicks to the desktop.
/// Dragging it forwards a native caption drag to the owner, so Aero Snap works.
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
            Text = "Google Calendar",
            ForeColor = Fore,
            Font = new Font("Segoe UI", 9.5f),
            AutoSize = true,
            Location = new Point(36, (BarHeight - 17) / 2),
            BackColor = Color.Transparent,
        };

        var iconFont = HoverPanel.CreateIconFont(10f);
        btnToggle = MakeButton("", iconFont, HoverBack);      // mouse: toggle click-through
        var btnMenu = MakeButton("", iconFont, HoverBack);    // hamburger: settings
        var btnClose = MakeButton("", iconFont, CloseHover);  // ChromeClose glyph
        btnToggle.Click += (_, _) => onToggle();
        btnMenu.Click += (_, _) => onSettings();
        btnClose.Click += (_, _) => Application.Exit();

        var tips = new ToolTip();
        tips.SetToolTip(btnToggle, "Toggle click-through (pass clicks to the desktop or not)");
        tips.SetToolTip(btnMenu, "Settings");
        tips.SetToolTip(btnClose, "Exit widget");

        Controls.AddRange([iconBox, title, btnToggle, btnMenu, btnClose]);

        // right-align the buttons whenever the bar resizes with the window
        Resize += (_, _) =>
        {
            btnClose.Location = new Point(Width - BtnW, 0);
            btnMenu.Location = new Point(Width - 2 * BtnW, 0);
            btnToggle.Location = new Point(Width - 3 * BtnW, 0);
        };

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

    /// <summary>Snap the bar onto the top edge of the owner window.</summary>
    public void Reposition() =>
        Bounds = new Rectangle(owner.Left, owner.Top, owner.Width, BarHeight);

    /// <summary>Tint the mouse icon while click-through is active.</summary>
    public void UpdateState(bool clickThrough) => btnToggle.ForeColor = clickThrough ? IconActive : Fore;

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
        if (e.Button != MouseButtons.Left)
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
