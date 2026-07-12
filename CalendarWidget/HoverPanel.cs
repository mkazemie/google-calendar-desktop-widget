// Google Calendar Desktop Widget
// Copyright (C) 2026 Mahdi Kazemiesfahani
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing.Text;

namespace CalendarWidget;

/// <summary>
/// Small power + settings pill revealed when the mouse reaches the configured
/// screen corner. The main window can't get hover events while click-through,
/// so MainForm polls the cursor position and shows/hides this panel.
/// </summary>
public class HoverPanel : Form
{
    // compact enough to sit on the window's title bar in interactive mode
    public const int PanelW = 96;
    public const int PanelH = 30;

    private static readonly Color PanelBack = Color.FromArgb(32, 33, 36);
    private static readonly Color HoverBack = Color.FromArgb(60, 64, 67);
    private static readonly Color IconIdle = Color.FromArgb(232, 234, 237);
    private static readonly Color IconActive = Color.FromArgb(138, 180, 248);

    private readonly Button btnToggle;

    // shown next to a click-through widget: it must never steal focus
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

    public HoverPanel(Action onToggle, Action onSettings)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = PanelBack;
        ClientSize = new Size(PanelW, PanelH);

        var iconFont = CreateIconFont(11f);
        btnToggle = MakeIconButton("\uE962", 0, iconFont);              // mouse glyph: do clicks pass through?
        var btnMenu = MakeIconButton("\uE700", PanelW / 2, iconFont);   // hamburger glyph
        btnToggle.Click += (_, _) => onToggle();
        btnMenu.Click += (_, _) => onSettings();

        var tips = new ToolTip();
        tips.SetToolTip(btnToggle, "Toggle click-through (pass clicks to the desktop or not)");
        tips.SetToolTip(btnMenu, "Settings");

        Controls.Add(btnToggle);
        Controls.Add(btnMenu);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.ApplyRoundedCorners(Handle);
    }

    /// <summary>Tint the mouse icon while click-through is active so the widget state is visible at a glance.</summary>
    public void UpdateState(bool clickThrough) => btnToggle.ForeColor = clickThrough ? IconActive : IconIdle;

    private Button MakeIconButton(string glyph, int x, Font font)
    {
        var b = new Button
        {
            Bounds = new Rectangle(x, 0, PanelW / 2, PanelH),
            Text = glyph,
            Font = font,
            FlatStyle = FlatStyle.Flat,
            BackColor = PanelBack,
            ForeColor = IconIdle,
            TabStop = false,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = HoverBack;
        b.FlatAppearance.MouseDownBackColor = HoverBack;
        return b;
    }

    internal static Font CreateIconFont(float size)
    {
        // Segoe Fluent Icons ships with Windows 11; MDL2 Assets is the Windows 10 fallback
        using var installed = new InstalledFontCollection();
        string name = installed.Families.Any(f => f.Name == "Segoe Fluent Icons")
            ? "Segoe Fluent Icons"
            : "Segoe MDL2 Assets";
        return new Font(name, size);
    }
}
