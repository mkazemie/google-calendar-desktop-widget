// Google Calendar Desktop Widget
// Copyright (C) 2026 Mahdi Kazemiesfahani
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace CalendarWidget;

/// <summary>
/// Colors for the widget's own chrome (title bar, window frame, hover panel), derived from
/// the background color the calendar page actually shows — so the chrome follows Google
/// Calendar's light/dark theme instead of looking like a separate app glued on top.
/// </summary>
public sealed record Theme(Color Back, Color Fore, Color Hover, Color Accent)
{
    public static readonly Theme Dark = FromBackground(Color.FromArgb(32, 33, 36));

    public bool IsDark => Brightness(Back) < 128;

    public static Theme FromBackground(Color back)
    {
        back = Color.FromArgb(back.R, back.G, back.B);  // drop alpha/names: plain opaque RGB
        return Brightness(back) < 128
            ? new Theme(back, Color.FromArgb(232, 234, 237), Blend(back, Color.White, 0.12), Color.FromArgb(138, 180, 248))
            : new Theme(back, Color.FromArgb(60, 64, 67), Blend(back, Color.Black, 0.08), Color.FromArgb(26, 115, 232));
    }

    /// <summary>"#RRGGBB" for settings.json, so the next start shows the right colors at once.</summary>
    public string ToHex() => $"#{Back.R:X2}{Back.G:X2}{Back.B:X2}";

    /// <summary>Parse <see cref="ToHex"/> output; null for anything else.</summary>
    public static Theme? FromHex(string? hex)
    {
        if (hex is not { Length: 7 } || hex[0] != '#' || !int.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out int rgb))
            return null;
        return FromBackground(Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF));
    }

    // perceived brightness (ITU-R BT.601 weights), 0..255
    private static double Brightness(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;

    private static Color Blend(Color from, Color to, double amount) => Color.FromArgb(
        (int)Math.Round(from.R + (to.R - from.R) * amount),
        (int)Math.Round(from.G + (to.G - from.G) * amount),
        (int)Math.Round(from.B + (to.B - from.B) * amount));
}
