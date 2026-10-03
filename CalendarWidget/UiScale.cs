// Google Calendar Desktop Widget
// Copyright (C) 2026 Mahdi Kazemiesfahani
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Drawing.Text;

namespace CalendarWidget;

/// <summary>
/// Per-monitor DPI helpers. All layout is designed at 96 DPI (100 %) and scaled to the
/// DPI of the monitor a window is on, so it keeps its proportions at 125/150/200 %.
/// </summary>
internal static class UiScale
{
    /// <summary>Scale a 96-DPI design value to <paramref name="dpi"/>.</summary>
    public static int Px(int logical, int dpi) => (int)Math.Round(logical * dpi / 96.0);

    /// <summary>
    /// Segoe UI sized for <paramref name="dpi"/>. Pixel units, not points: WinForms converts
    /// point sizes with the SYSTEM DPI, which is wrong on any monitor whose scale differs
    /// from the primary's — text then outgrows (or shrinks inside) a layout scaled correctly.
    /// Replaced fonts are left to the GC, never disposed: a control handed a font EQUAL to
    /// its current one keeps the old object, which would then be a disposed font in use.
    /// </summary>
    public static Font Font(float points, int dpi, FontStyle style = FontStyle.Regular) =>
        new("Segoe UI", points * dpi / 72f, style, GraphicsUnit.Pixel);

    /// <summary>Icon glyph font: Segoe Fluent Icons on Windows 11, MDL2 Assets on Windows 10.</summary>
    public static Font IconFont(float points, int dpi) =>
        new(IconFamily.Value, points * dpi / 72f, GraphicsUnit.Pixel);

    private static readonly Lazy<string> IconFamily = new(() =>
    {
        using var installed = new InstalledFontCollection();
        return installed.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    });
}
