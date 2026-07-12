; Inno Setup script for the Calendar Widget.
; Ships the small framework-dependent build; if the machine lacks the .NET 8
; Desktop Runtime or the WebView2 runtime, they are downloaded and installed
; during setup instead of being bundled into the exe.

#define MyAppName "Google Calendar Desktop Widget"
#define MyAppVersion "1.0.0"
#define MyAppExeName "CalendarWidget.exe"

[Setup]
AppId={{7B2E9C41-5A83-4F1D-9E6B-D3C8A0F47E21}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\CalendarWidget
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=CalendarWidgetSetup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
; admin rights: the runtime installers are machine-wide anyway
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\CalendarWidget\app.ico
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\CalendarWidget\bin\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  // aka.ms link always resolves to the latest 8.0.x desktop runtime
  DotNetURL = 'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe';
  // Evergreen WebView2 bootstrapper (tiny; itself downloads the runtime)
  WebView2URL = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';

var
  DownloadPage: TDownloadWizardPage;

function IsDotNet8DesktopInstalled: Boolean;
var
  FindRec: TFindRec;
begin
  Result := FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), FindRec);
  if Result then
    FindClose(FindRec);
end;

function IsWebView2Installed: Boolean;
var
  Version: string;
begin
  // per-machine, then per-user registration of the Evergreen runtime
  Result := (RegQueryStringValue(HKLM,
      'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
      'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'))
    or (RegQueryStringValue(HKCU,
      'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
      'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing),
    SetupMessage(msgPreparingDesc), nil);
end;

function RunInstaller(const FileName, Args, Name: string): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{tmp}\' + FileName), Args, '', SW_SHOW,
    ewWaitUntilTerminated, ResultCode)
    and ((ResultCode = 0) or (ResultCode = 3010));  // 3010 = success, reboot required
  if not Result then
    MsgBox(Name + ' installation failed (exit code ' + IntToStr(ResultCode) + ').',
      mbCriticalError, MB_OK);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  NeedDotNet, NeedWebView: Boolean;
begin
  Result := True;
  if CurPageID <> wpReady then
    Exit;

  NeedDotNet := not IsDotNet8DesktopInstalled;
  NeedWebView := not IsWebView2Installed;
  if not (NeedDotNet or NeedWebView) then
    Exit;

  DownloadPage.Clear;
  if NeedDotNet then
    DownloadPage.Add(DotNetURL, 'dotnet8-desktop-runtime.exe', '');
  if NeedWebView then
    DownloadPage.Add(WebView2URL, 'webview2-bootstrapper.exe', '');

  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
    except
      if not DownloadPage.AbortedByUser then
        SuppressibleMsgBox(AddPeriod(GetExceptionMessage), mbCriticalError, MB_OK, IDOK);
      Result := False;
      Exit;
    end;

    if NeedDotNet and not RunInstaller('dotnet8-desktop-runtime.exe',
        '/install /quiet /norestart', '.NET 8 Desktop Runtime') then
    begin
      Result := False;
      Exit;
    end;

    if NeedWebView and not RunInstaller('webview2-bootstrapper.exe',
        '/silent /install', 'WebView2 Runtime') then
    begin
      Result := False;
      Exit;
    end;
  finally
    DownloadPage.Hide;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // user data (settings + Google session in the WebView2 profile) lives in
  // %LOCALAPPDATA%\CalendarWidget, outside {app}; offer to wipe it on uninstall
  if CurUninstallStep = usPostUninstall then
    if MsgBox('Also remove saved settings and the Google sign-in data?',
        mbConfirmation, MB_YESNO) = IDYES then
      DelTree(ExpandConstant('{localappdata}\CalendarWidget'), True, True, True);
end;
