# Google Calendar Desktop Widget
# Copyright (C) 2026 Mahdi Kazemiesfahani
# SPDX-License-Identifier: AGPL-3.0-or-later
#
# Signs files with Azure Artifact Signing (account mkazemiesigning, profile widgetsigning).
# Needs the Artifact Signing Client Tools, the Windows SDK SignTool and a current `az login`.
# Usage: sign.ps1 <file> [<file> ...]   (also called by Inno Setup for the setup and uninstaller)

param([Parameter(Mandatory, ValueFromRemainingArguments)][string[]]$Files)
$ErrorActionPreference = 'Stop'

$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.*\x64\signtool.exe" |
    Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
$dlib = "$env:LOCALAPPDATA\Microsoft\MicrosoftArtifactSigningClientTools\Azure.CodeSigning.Dlib.dll"
$metadata = Join-Path $PSScriptRoot 'metadata.json'
if (-not $signtool) { throw 'SignTool not found (install the Windows SDK signing tools).' }
if (-not (Test-Path $dlib)) { throw "Artifact Signing dlib not found: $dlib" }
$env:Path += ";$env:ProgramFiles\Microsoft SDKs\Azure\CLI2\wbin"  # az, for AzureCliCredential

foreach ($file in $Files) {
    # /tr timestamp is essential: Artifact Signing certificates are valid for only 3 days
    & $signtool sign /fd SHA256 /tr http://timestamp.acs.microsoft.com /td SHA256 /dlib $dlib /dmdf $metadata $file | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Signing failed: $file (run 'az login' if the sign-in expired)" }
    $sig = Get-AuthenticodeSignature $file
    if ($sig.Status -ne 'Valid' -or -not $sig.TimeStamperCertificate) { throw "Signature check failed: $file ($($sig.Status))" }
    Write-Host "Signed: $file"
}
