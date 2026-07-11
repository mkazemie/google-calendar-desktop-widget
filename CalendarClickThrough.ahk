#Requires AutoHotkey v2.0
#SingleInstance Force  ; re-running the script silently replaces the old instance
; === Google Calendar as Transparent Desktop Widget (Windows 11 SAFE: NO REPARENT) ===
; Places the Calendar PWA at the absolute bottom of the Z-order (under all normal windows),
; with click-through by default, a hover panel (ON/OFF + settings), a tray menu, and
; persisted settings in CalendarClickThrough.ini next to this script.

; Button placement, hover detection and tooltips all use screen coordinates
CoordMode("Mouse", "Screen")
CoordMode("ToolTip", "Screen")

; ---------------- SETTINGS ----------------
; PWA window titles start with "Google Calendar". Regular browser windows are excluded
; by their " - Google Chrome" / " - Microsoft Edge" title suffix, so a normal browser
; tab on calendar.google.com never gets hijacked.
SetTitleMatchMode("RegEx")
titleNeedle    := "^Google Calendar"
excludeBrowser := " - (Google Chrome|Microsoft.{0,3}Edge)$"  ; Edge puts invisible chars before "Edge"
preferEdge     := false             ; set true if you installed the PWA with Edge
calendarUrl    := "https://calendar.google.com/calendar/u/0/r"

matchChrome := titleNeedle " ahk_exe chrome\.exe"
matchEdge   := titleNeedle " ahk_exe msedge\.exe"

; -------- persisted user settings --------
global iniFile    := A_ScriptDir "\CalendarClickThrough.ini"
global alphaLevel := IniRead(iniFile, "Settings", "Transparency", 220) + 0  ; 80..255
global dimLevel   := IniRead(iniFile, "Settings", "Dim", 0) + 0             ; 0..90 (%)
global autoLaunch := IniRead(iniFile, "Settings", "AutoLaunch", 0) + 0
global startupLnk := A_Startup "\CalendarClickThrough.lnk"

; ------------- FIND (OR LAUNCH) THE WINDOW -------------
if autoLaunch && !WinExist(matchChrome, , excludeBrowser) && !WinExist(matchEdge, , excludeBrowser)
    Run((preferEdge ? "msedge.exe" : "chrome.exe") " --new-window --app=" calendarUrl)

if preferEdge {
    hwnd := WinWait(matchEdge, , autoLaunch ? 20 : 10, excludeBrowser)
} else {
    hwnd := WinWait(matchChrome, , autoLaunch ? 20 : 5, excludeBrowser)
    if !hwnd
        hwnd := WinWait(matchEdge, , 10, excludeBrowser)
}
if !hwnd {
    MsgBox("Couldn't find a Calendar PWA window. Open it and try again,`n"
         . "or enable 'Open Google Calendar when this script starts' in Settings.")
    ExitApp()
}
Sleep(400)

; ------------ INITIAL EFFECTS --------------
global isClickThrough := true
global btnVisible := false
global hoverTimer := 0

; restore the calendar to a normal window if this script exits for any reason
OnExit(RestoreCalendar)

MakeClickThrough(hwnd)
WinSetTransparent(alphaLevel, hwnd)

; -------- Dim overlay (poor man's dark mode) --------
; A black, click-through, layered window kept exactly over the calendar and one
; z-order step above it (still under everything else), so only the widget darkens.
global overlayGui := Gui("-Caption +ToolWindow +E0x20 +E0x8000000")  ; TRANSPARENT + NOACTIVATE
overlayGui.BackColor := "000000"

; -------- Z-order upkeep: event hooks instead of a fast polling watchdog --------
; EVENT_SYSTEM_FOREGROUND (0x3): something came to the front -> re-assert bottom.
; EVENT_OBJECT_DESTROY (0x8001, scoped to the calendar's process): window closed -> exit.
global winEventCb     := CallbackCreate(WinEventProc, , 7)
global hookForeground := DllCall("SetWinEventHook", "uint", 0x3, "uint", 0x3,
    "ptr", 0, "ptr", winEventCb, "uint", 0, "uint", 0, "uint", 0, "ptr")
global hookDestroy    := DllCall("SetWinEventHook", "uint", 0x8001, "uint", 0x8001,
    "ptr", 0, "ptr", winEventCb, "uint", WinGetPID(hwnd), "uint", 0, "uint", 0, "ptr")

EnforceBottom()
ApplyDim()
; slow fallback: catches anything the hooks miss and keeps the overlay aligned
SetTimer(SlowMaintenance, 5000)

; -------- Hover panel: ON/OFF + hamburger for settings --------
global btnW := 105, btnH := 25
global btnGui := Gui("+AlwaysOnTop -Caption +ToolWindow")
btnGui.BackColor := "Gray"
btnGui.MarginX := 0, btnGui.MarginY := 0   ; window size == controls, so the hover rect matches
btnGui.SetFont("s8")
btnGui.Add("Button", "x0 y0 w80 h" btnH, "ON/OFF").OnEvent("Click", ToggleClickThrough)
btnGui.SetFont("s10")
btnGui.Add("Button", "x80 y0 w25 h" btnH, "☰").OnEvent("Click", (*) => ShowSettings())

; place the panel at the bottom-right of whichever monitor the calendar is on
GetWorkArea(hwnd, &waLeft, &waTop, &waRight, &waBottom)
global btnX := waRight - btnW - 40, btnY := waBottom - btnH - 30
btnGui.Show("x" btnX " y" btnY " Hide")

SetTimer(CheckMouseHover, 100)

; -------- Settings window (opened by the hamburger / tray) --------
global settingsGui := Gui("+AlwaysOnTop -MinimizeBox -MaximizeBox", "Calendar Widget Settings")
settingsGui.SetFont("s10")
settingsGui.Add("Text", "xm", "Transparency:")
sldAlpha := settingsGui.Add("Slider", "xm w250 Range80-255 AltSubmit ToolTip", alphaLevel)
sldAlpha.OnEvent("Change", OnAlphaChange)
cbStartup := settingsGui.Add("Checkbox", "xm Checked" (FileExist(startupLnk) ? 1 : 0), "Start with Windows")
cbStartup.OnEvent("Click", OnStartupToggle)
cbLaunch := settingsGui.Add("Checkbox", "xm Checked" (autoLaunch ? 1 : 0), "Open Google Calendar when this script starts")
cbLaunch.OnEvent("Click", OnAutoLaunchToggle)
settingsGui.SetFont("s9 c606060")
settingsGui.Add("Text", "xm w270", "Tip: for a dark theme, enable dark mode once inside "
    . "Google Calendar's own settings (gear icon).")
settingsGui.SetFont("s10 cDefault")
settingsGui.Add("Button", "xm w130", "Exit && restore").OnEvent("Click", (*) => ExitApp())
settingsGui.OnEvent("Close", (*) => settingsGui.Hide())
settingsGui.OnEvent("Escape", (*) => settingsGui.Hide())

; -------- Tray menu --------
A_TrayMenu.Delete()
A_TrayMenu.Add("Toggle interaction", ToggleClickThrough)
A_TrayMenu.Add("Settings…", (*) => ShowSettings())
A_TrayMenu.Add()
A_TrayMenu.Add("Exit && restore calendar", (*) => ExitApp())
A_TrayMenu.Default := "Settings…"
A_IconTip := "Google Calendar widget"

; ==================== FUNCTIONS ====================

MakeClickThrough(winHwnd) {
    ; WS_EX_TRANSPARENT lets mouse pass through; WS_EX_NOACTIVATE avoids focus steals;
    ; WS_EX_LAYERED is required for transparency
    exStyle := DllCall("GetWindowLongPtr", "ptr", winHwnd, "int", -20, "ptr")
    newStyle := exStyle | 0x20 | 0x08000000 | 0x00080000  ; TRANSPARENT + NOACTIVATE + LAYERED
    DllCall("SetWindowLongPtr", "ptr", winHwnd, "int", -20, "ptr", newStyle, "ptr")
}

MakeInteractive(winHwnd) {
    ; remove TRANSPARENT so it takes clicks, and NOACTIVATE so it can take keyboard focus
    exStyle := DllCall("GetWindowLongPtr", "ptr", winHwnd, "int", -20, "ptr")
    newStyle := exStyle & ~(0x20 | 0x08000000)
    DllCall("SetWindowLongPtr", "ptr", winHwnd, "int", -20, "ptr", newStyle, "ptr")
}

SendToBottom(winHwnd) {
    ; HWND_BOTTOM = 1; keep it behind everything, but visible
    ; Flags: 0x0013 = SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE
    DllCall("SetWindowPos", "ptr", winHwnd, "ptr", 1, "int", 0, "int", 0, "int", 0, "int", 0, "uint", 0x13)
}

EnforceBottom() {
    ; overlay first, calendar second: calendar ends up bottom-most with the
    ; overlay directly above it, both under every normal window
    if (dimLevel > 0 && isClickThrough)
        SendToBottom(overlayGui.Hwnd)
    SendToBottom(hwnd)
}

WinEventProc(hHook, event, eHwnd, idObject, idChild, idThread, time) {
    if (event = 0x8001) {  ; EVENT_OBJECT_DESTROY
        if (eHwnd = hwnd && idObject = 0)   ; OBJID_WINDOW: the calendar itself closed
            SetTimer(() => ExitApp(), -1)   ; exit outside the hook callback
        return
    }
    ; EVENT_SYSTEM_FOREGROUND: something was raised; re-assert bottom
    if isClickThrough
        EnforceBottom()
}

SlowMaintenance() {
    if !WinExist("ahk_id " hwnd)
        ExitApp()
    if isClickThrough {
        SyncOverlayRect()
        EnforceBottom()
    }
}

SyncOverlayRect() {
    if !(dimLevel > 0 && isClickThrough)
        return
    WinGetPos(&cx, &cy, &cw, &ch, hwnd)
    overlayGui.Show("x" cx " y" cy " w" cw " h" ch " NoActivate")
}

ApplyDim() {
    if (dimLevel > 0 && isClickThrough) {
        SyncOverlayRect()
        WinSetTransparent(Round(dimLevel * 2.55), overlayGui)
        EnforceBottom()
    } else {
        overlayGui.Hide()
    }
}

RestoreCalendar(*) {
    global hookForeground, hookDestroy
    DllCall("UnhookWinEvent", "ptr", hookForeground)
    DllCall("UnhookWinEvent", "ptr", hookDestroy)
    if !WinExist("ahk_id " hwnd)
        return
    exStyle := DllCall("GetWindowLongPtr", "ptr", hwnd, "int", -20, "ptr")
    newStyle := exStyle & ~(0x20 | 0x08000000 | 0x00080000)  ; clear TRANSPARENT + NOACTIVATE + LAYERED
    DllCall("SetWindowLongPtr", "ptr", hwnd, "int", -20, "ptr", newStyle, "ptr")
    WinSetTransparent("Off", hwnd)
}

GetWorkArea(winHwnd, &left, &top, &right, &bottom) {
    ; work area (excludes taskbar) of the monitor containing the window
    hMon := DllCall("MonitorFromWindow", "ptr", winHwnd, "uint", 2, "ptr")  ; MONITOR_DEFAULTTONEAREST
    mi := Buffer(40, 0)
    NumPut("uint", 40, mi)
    DllCall("GetMonitorInfo", "ptr", hMon, "ptr", mi)
    left := NumGet(mi, 20, "int"), top := NumGet(mi, 24, "int")
    right := NumGet(mi, 28, "int"), bottom := NumGet(mi, 32, "int")
}

ToggleClickThrough(*) {
    global isClickThrough
    if !WinExist("ahk_id " hwnd)
        ExitApp()
    isClickThrough := !isClickThrough
    if !isClickThrough {
        overlayGui.Hide()
        MakeInteractive(hwnd)
        WinSetTransparent(255, hwnd)
        WinActivate(hwnd)  ; bring it forward so it isn't stuck under other windows while you use it
        ToolTip("Interaction Enabled", btnX - 60, btnY - 35)
    } else {
        MakeClickThrough(hwnd)
        WinSetTransparent(alphaLevel, hwnd)
        ApplyDim()
        EnforceBottom()
        ToolTip("Click-Through Enabled", btnX - 80, btnY - 35)
    }
    SetTimer(() => ToolTip(), -1100)
}

ShowSettings() {
    settingsGui.Show()
}

OnAlphaChange(ctrl, *) {
    global alphaLevel := ctrl.Value
    if isClickThrough
        WinSetTransparent(alphaLevel, hwnd)
    IniWrite(alphaLevel, iniFile, "Settings", "Transparency")
}

OnStartupToggle(ctrl, *) {
    if ctrl.Value
        FileCreateShortcut(A_ScriptFullPath, startupLnk, A_ScriptDir)
    else
        try FileDelete(startupLnk)
}

OnAutoLaunchToggle(ctrl, *) {
    global autoLaunch := ctrl.Value
    IniWrite(autoLaunch, iniFile, "Settings", "AutoLaunch")
}

CheckMouseHover() {
    global btnVisible, hoverTimer
    MouseGetPos(&mx, &my)
    isHover := (mx >= btnX && mx < btnX + btnW && my >= btnY && my < btnY + btnH)

    if isHover {
        if !btnVisible {
            btnGui.Show("x" btnX " y" btnY " NoActivate")
            btnVisible := true
        }
        if hoverTimer {
            SetTimer(hoverTimer, 0)
            hoverTimer := 0  ; forget the cancelled timer, or the button gets stuck visible
        }
    } else if btnVisible && !hoverTimer {
        hoverTimer := (*) => HideButton()
        SetTimer(hoverTimer, -2000)
    }
}

HideButton() {
    global btnVisible, hoverTimer
    btnGui.Hide()
    btnVisible := false
    hoverTimer := 0
}
