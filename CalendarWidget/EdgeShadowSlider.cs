// Google Calendar Desktop Widget
// Copyright (C) 2026 Mahdi Kazemiesfahani
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing.Drawing2D;

namespace CalendarWidget;

/// <summary>
/// Two-thumb slider for the edge shadows. The track stands for the calendar's width: the
/// left thumb marks where the left shadow ends and the right thumb where the right one
/// begins, so each side's width is its thumb's distance from that end (at the end = off).
/// The thumbs stay <see cref="MinGap"/> apart, so they never overlap and some calendar is
/// always left clear. Arrow keys nudge whichever thumb was used last.
/// </summary>
public class EdgeShadowSlider : Control
{
    public const int MinGap = 10;  // % of the calendar kept clear between the two shadows

    // 96-DPI design metrics; everything is drawn scaled to the control's DPI
    private const int ThumbR = 8;
    private const int Track = 6;
    public const int LogicalHeight = 2 * (ThumbR + 4);  // room for the focus ring at the track ends

    private static readonly Color TrackColor = Color.FromArgb(60, 64, 67);
    private static readonly Color Accent = Color.FromArgb(138, 180, 248);

    private int left, right;
    private bool dragging;
    private bool activeLeft = true;  // the thumb a drag or the arrow keys move

    /// <summary>Raised when the user moves a thumb (not for programmatic changes).</summary>
    public event EventHandler? ValuesChanged;

    public EdgeShadowSlider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Height = LogicalHeight;
        Cursor = Cursors.Hand;
    }

    private float K => DeviceDpi / 96f;
    private float Pad => (ThumbR + 4) * K;

    public int LeftPercent
    {
        get => left;
        set { left = Math.Clamp(value, 0, 100 - MinGap - right); Invalidate(); }
    }

    public int RightPercent
    {
        get => right;
        set { right = Math.Clamp(value, 0, 100 - MinGap - left); Invalidate(); }
    }

    private float XAt(int percent) => Pad + percent * (Width - 2 * Pad) / 100f;

    private int PercentAt(int x) =>
        Math.Clamp((int)Math.Round((x - Pad) * 100f / Math.Max(1f, Width - 2 * Pad)), 0, 100);

    private void SetFromUser(int newLeft, int newRight)
    {
        newLeft = Math.Clamp(newLeft, 0, 100 - MinGap - right);
        newRight = Math.Clamp(newRight, 0, 100 - MinGap - newLeft);
        if (newLeft == left && newRight == right)
            return;
        left = newLeft;
        right = newRight;
        Invalidate();
        ValuesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void MoveActiveThumbTo(int x)
    {
        int p = PercentAt(x);
        if (activeLeft)
            SetFromUser(p, right);
        else
            SetFromUser(left, 100 - p);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;
        Focus();
        // grab the nearer thumb and jump it to the click, like a TrackBar's thumb
        activeLeft = Math.Abs(e.X - XAt(left)) <= Math.Abs(e.X - XAt(100 - right));
        dragging = true;
        MoveActiveThumbTo(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragging)
            MoveActiveThumbTo(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        dragging = false;
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int step = e.KeyCode switch { Keys.Left => -1, Keys.Right => 1, _ => 0 };
        if (step == 0)
            return;
        // both thumbs move in screen direction: the right shadow grows as its thumb goes left
        if (activeLeft)
            SetFromUser(left + step, right);
        else
            SetFromUser(left, right - step);
        e.Handled = true;
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float cy = Height / 2f;
        float x0 = XAt(0), x1 = XAt(100), xl = XAt(left), xr = XAt(100 - right);

        using (var track = new Pen(TrackColor, Track * K) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(track, x0, cy, x1, cy);

        // each shaded span fades from its edge toward its thumb, previewing the shadow itself
        DrawFade(g, x0, xl, cy);
        DrawFade(g, x1, xr, cy);

        DrawThumb(g, xl, cy, ring: Focused && activeLeft);
        DrawThumb(g, xr, cy, ring: Focused && !activeLeft);
    }

    private void DrawFade(Graphics g, float edge, float thumb, float cy)
    {
        if (Math.Abs(thumb - edge) < 1)
            return;
        using var brush = new LinearGradientBrush(new PointF(edge, cy), new PointF(thumb, cy),
            Accent, Color.FromArgb(0, Accent)) { WrapMode = WrapMode.TileFlipX };  // flip: the rounded cap past the edge stays opaque
        using var pen = new Pen(brush, Track * K) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, edge, cy, thumb, cy);
    }

    private void DrawThumb(Graphics g, float x, float cy, bool ring)
    {
        float r = ThumbR * K;
        using (var fill = new SolidBrush(Accent))
            g.FillEllipse(fill, x - r, cy - r, 2 * r, 2 * r);
        if (!ring)
            return;
        float rr = (ThumbR + 3) * K;
        using var pen = new Pen(Color.FromArgb(140, Accent), 1.5f * K);
        g.DrawEllipse(pen, x - rr, cy - rr, 2 * rr, 2 * rr);
    }
}
