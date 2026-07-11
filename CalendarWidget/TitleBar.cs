namespace CalendarWidget;

/// <summary>
/// Custom caption strip shown in interactive mode. The form is always borderless,
/// so this bar IS the title bar: app icon + title (drag area) on the left, and the
/// widget controls (click-through toggle, settings) integrated next to close.
/// </summary>
public class TitleBar : Panel
{
    public const int BarHeight = 34;
    private const int BtnW = 46;

    private static readonly Color BarBack = Color.FromArgb(32, 33, 36);
    private static readonly Color HoverBack = Color.FromArgb(60, 64, 67);
    private static readonly Color CloseHover = Color.FromArgb(196, 43, 28);  // Win11 close-button red
    private static readonly Color Fore = Color.FromArgb(232, 234, 237);

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 2;

    public TitleBar(Action onToggle, Action onSettings)
    {
        Dock = DockStyle.Top;
        Height = BarHeight;
        BackColor = BarBack;

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
        var btnToggle = MakeButton("\uE962", iconFont, HoverBack);  // mouse: enable click-through
        var btnMenu = MakeButton("\uE700", iconFont, HoverBack);    // hamburger: settings
        var btnClose = MakeButton("\uE8BB", iconFont, CloseHover);  // ChromeClose glyph
        btnToggle.Click += (_, _) => onToggle();
        btnMenu.Click += (_, _) => onSettings();
        btnClose.Click += (_, _) => Application.Exit();

        var tips = new ToolTip();
        tips.SetToolTip(btnToggle, "Back to widget mode (clicks pass through)");
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

        // dragging the bar (or the title/icon on it) moves the window
        MouseDown += StartDrag;
        title.MouseDown += StartDrag;
        iconBox.MouseDown += StartDrag;
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
        if (e.Button != MouseButtons.Left || FindForm() is not { } form)
            return;
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(form.Handle, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
    }
}
