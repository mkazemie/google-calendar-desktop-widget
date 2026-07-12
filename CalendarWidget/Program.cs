// Google Calendar Desktop Widget
// Copyright (C) 2026 Mahdi Kazemiesfahani
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace CalendarWidget;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // re-running the app silently replaces nothing; the second instance just exits
        using var mutex = new Mutex(true, "CalendarWidget_SingleInstance", out bool createdNew);
        if (!createdNew)
            return;

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
