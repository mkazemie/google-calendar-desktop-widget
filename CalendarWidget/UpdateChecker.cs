// Google Calendar Desktop Widget
// Copyright (C) 2026 Mahdi Kazemiesfahani
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace CalendarWidget;

/// <summary>
/// Asks GitHub for the latest release (at most once a day) so Settings and the tray can
/// offer a newer version. Sends nothing about the user: a plain anonymous request to
/// GitHub's public API, the same one any browser makes for the releases page.
/// </summary>
public static class UpdateChecker
{
    public const string RepoUrl = "https://github.com/mkazemie/google-calendar-desktop-widget";
    private const string LatestApi = "https://api.github.com/repos/mkazemie/google-calendar-desktop-widget/releases/latest";

    /// <summary>This build's version (from the project's Version property).</summary>
    public static Version Current { get; } =
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? new Version(v.Major, v.Minor, v.Build) : new Version(0, 0, 0);

    public sealed record Release(Version Version, string PageUrl);

    /// <summary>The latest published release if it's newer than this build; null otherwise or on any failure.</summary>
    public static async Task<Release?> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("GoogleCalendarDesktopWidget/" + Current);  // GitHub requires one
            using var doc = JsonDocument.Parse(await http.GetStringAsync(LatestApi));
            var tag = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v', 'V');
            var page = doc.RootElement.GetProperty("html_url").GetString();
            if (Version.TryParse(tag, out var latest) && page is not null && latest > Current)
                return new Release(latest, page);
        }
        catch
        {
            // offline, rate-limited or GitHub changed: just don't offer an update this time
        }
        return null;
    }
}
