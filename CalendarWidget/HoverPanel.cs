// Google Calendar Desktop Widget
// Copyright (C) 2026 Mahdi Kazemiesfahani
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace CalendarWidget;

/// <summary>
/// Small power + settings pill revealed when the mouse reaches the configured
/// screen corner. The main window can't get hover events while click-through,
/// so MainForm polls the cursor position and shows/hides this panel.
/// </summary>
public class HoverPanel : Form
{
    // 96-DPI design size; scaled to the target monitor's DPI (see SizeFor)
    private const int LogicalW = 96;
    private const int LogicalH = 30;

    private readonly Button btnToggle;
    private readonly Button btnMenu;
    private int layoutDpi;
    private Theme theme = Theme.Dark;
    private bool clickThrough;

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
        AutoScaleMode = AutoScaleMode.None;  // ApplyScale lays the panel out for its DPI
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;

        btnToggle = MakeIconButton("");  // mouse glyph: do clicks pass through?
        btnMenu = MakeIconButton("");    // hamburger glyph
        btnToggle.Click += (_, _) => onToggle();
        btnMenu.Click += (_, _) => onSettings();

        var tips = new ToolTip();
        tips.SetToolTip(btnToggle, "Toggle click-through (pass clicks to the desktop or not)");
        tips.SetToolTip(btnMenu, "Settings");

        Controls.Add(btnToggle);
        Controls.Add(btnMenu);
        ApplyScale(DeviceDpi);
        ApplyTheme(theme);
    }

    /// <summary>Panel size in pixels on a monitor with this DPI.</summary>
    public static Size SizeFor(int dpi) => new(UiScale.Px(LogicalW, dpi), UiScale.Px(LogicalH, dpi));

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.ApplyRoundedCorners(Handle);
    }

    // MainForm places the panel with ShowAt; cancel WinForms' own rescale on top of that
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        e.Cancel = true;
        base.OnDpiChanged(e);
        ApplyScale(e.DeviceDpiNew);
    }

    /// <summary>Show at <paramref name="bounds"/>, laid out for that monitor's DPI.</summary>
    public void ShowAt(Rectangle bounds, int dpi)
    {
        ApplyScale(dpi);
        // create the window before sizing it: creation clamps it to the system minimum
        // window size (~136x39 at 100 %, bigger than the panel); later resizes aren't clamped
        if (!IsHandleCreated)
            CreateHandle();
        Bounds = bounds;
        if (!Visible)
            Show();  // no-activate: ShowWithoutActivation
    }

    private void ApplyScale(int dpi)
    {
        if (dpi == layoutDpi)
            return;  // ShowAt runs on every reveal; only a new DPI needs a new layout
        layoutDpi = dpi;
        var size = SizeFor(dpi);
        ClientSize = size;
        btnToggle.Bounds = new Rectangle(0, 0, size.Width / 2, size.Height);
        btnMenu.Bounds = new Rectangle(size.Width / 2, 0, size.Width - size.Width / 2, size.Height);
        btnToggle.Font = btnMenu.Font = UiScale.IconFont(11f, dpi);
    }

    /// <summary>Match the calendar's light/dark theme.</summary>
    public void ApplyTheme(Theme t)
    {
        theme = t;
        BackColor = t.Back;
        foreach (var b in new[] { btnToggle, btnMenu })
        {
            b.BackColor = t.Back;
            b.ForeColor = t.Fore;
            b.FlatAppearance.MouseOverBackColor = t.Hover;
            b.FlatAppearance.MouseDownBackColor = t.Hover;
        }
        UpdateState(clickThrough);
    }

    /// <summary>Tint the mouse icon while click-through is active so the widget state is visible at a glance.</summary>
    public void UpdateState(bool clickThrough)
    {
        this.clickThrough = clickThrough;
        btnToggle.ForeColor = clickThrough ? theme.Accent : theme.Fore;
    }

    private static Button MakeIconButton(string glyph)
    {
        var b = new Button
        {
            Text = glyph,
            FlatStyle = FlatStyle.Flat,
            TabStop = false,
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }
}
