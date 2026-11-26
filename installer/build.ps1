# Google Calendar Desktop Widget
# Copyright (C) 2026 Mahdi Kazemiesfahani
# SPDX-License-Identifier: AGPL-3.0-or-later
#
# Builds the signed installer: publish -> sign the app -> Inno Setup (signs setup + uninstaller).
# Run from the repo root: powershell -File installer\build.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sign = Join-Path $PSScriptRoot 'signing\sign.ps1'

# the app's version (shown in Settings, compared by the update check) comes from the installer script
$version = [regex]::Match((Get-Content "$PSScriptRoot\CalendarWidget.iss" -Raw), '#define MyAppVersion "([^"]+)"').Groups[1].Value
if (-not $version) { throw 'MyAppVersion not found in CalendarWidget.iss' }

dotnet publish "$root\CalendarWidget" -c Release -r win-x64 --no-self-contained -o "$root\CalendarWidget\bin\publish" -p:Version=$version
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

& $sign "$root\CalendarWidget\bin\publish\CalendarWidget.exe" "$root\CalendarWidget\bin\publish\CalendarWidget.dll"

# Inno Setup calls the named tool "azsign" for the setup and the uninstaller ($f = file to sign)
$iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
& $iscc "/Sazsign=powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$sign`" `$f" "$PSScriptRoot\CalendarWidget.iss"
if ($LASTEXITCODE -ne 0) { throw 'ISCC failed (retry if it was "Resource update error (110)")' }

Get-ChildItem "$PSScriptRoot\Output\CalendarWidgetSetup-*.exe" | Sort-Object LastWriteTime | Select-Object -Last 1 |
    ForEach-Object { $s = Get-AuthenticodeSignature $_.FullName; "{0}: {1}, {2}" -f $_.Name, $s.Status, $s.SignerCertificate.Subject }
