#define AppName "SafeSnap"
#define AppVersion "1.2.2"
#define AppPublisher "Charles Wells"
#define AppExe "SafeSnap.exe"
; Named event a running SafeSnap listens on; setting it makes the app exit cleanly (see App.ExitEventName).
#define ExitEvent "SafeSnap.SingleInstance.Exit"
; Single-instance mutex held by a running SafeSnap (see App.SingleInstanceMutexName).
#define InstanceMutex "SafeSnap.SingleInstance"

[Setup]
AppId={{A6CFA962-1A62-40FB-A6B5-6E0530BBD2D2}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/sirchas/SafeSnap
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=Output
OutputBaseFilename=SafeSnap-Setup-{#AppVersion}
SetupIconFile=..\src\SafeSnap\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; The code section asks a running SafeSnap to exit cleanly first. Anything still holding the exe
; (e.g. 1.2.1, which has no exit signal) is closed by Inno's Restart Manager support.
; Silent installs close it automatically; interactive installs show the close-apps page
; with "Automatically close" selected. SafeSnap is relaunched by the Run entries, not by Restart Manager.
CloseApplications=force
RestartApplications=no
LicenseFile=..\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startupicon"; Description: "Start SafeSnap in the tray when I sign in to Windows"; GroupDescription: "Startup:"

[Files]
Source: "..\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon
; Startup shortcut: launches hidden in the tray. Inno removes it on uninstall.
Name: "{autostartup}\{#AppName}"; Filename: "{app}\{#AppExe}"; Parameters: "--tray"; Comment: "Start SafeSnap in the tray"; Tasks: startupicon

[Run]
; Interactive: "Launch SafeSnap" on the Finish page, ticked by default, opens the window.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; Silent installs and upgrades: relaunch quietly in the tray.
Filename: "{app}\{#AppExe}"; Parameters: "--tray"; Flags: nowait skipifnotsilent runasoriginaluser

[Code]
const
  EVENT_MODIFY_STATE = $0002;

{ Win32 calls only; no shell commands. }
function OpenEvent(dwDesiredAccess: Cardinal; bInheritHandle: Boolean; lpName: String): Cardinal;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(hEvent: Cardinal): Boolean;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(hObject: Cardinal): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

{ A running SafeSnap (window or tray) holds its single-instance mutex. }
function SafeSnapRunning(): Boolean;
begin
  Result := CheckForMutexes('{#InstanceMutex}');
end;

{ Ask a running SafeSnap to exit cleanly through its exit event. Versions before 1.2.2
  don't listen for it; Inno's Restart Manager support (CloseApplications=force) closes
  those, and anything that doesn't exit in time, right after this. }
procedure AskSafeSnapToExit();
var
  H: Cardinal;
  I: Integer;
begin
  if not SafeSnapRunning() then
  begin
    Log('SafeSnap is not running.');
    Exit;
  end;

  H := OpenEvent(EVENT_MODIFY_STATE, False, '{#ExitEvent}');
  if H = 0 then
  begin
    Log('SafeSnap is running without an exit signal (older version); Restart Manager will close it.');
    Exit;
  end;

  Log('SafeSnap is running; asking it to exit.');
  SetEvent(H);
  CloseHandle(H);
  for I := 1 to 50 do
  begin
    if not SafeSnapRunning() then
      Break;
    Sleep(100);
  end;

  if SafeSnapRunning() then
    Log('SafeSnap did not exit in time; Restart Manager will close it.')
  else
  begin
    { The mutex is released just before the process ends; give it a moment to unlock the exe. }
    Sleep(1000);
    Log('SafeSnap exited cleanly.');
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  AskSafeSnapToExit();
  NeedsRestart := False;
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  AskSafeSnapToExit();
  Result := True;
end;
