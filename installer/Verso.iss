; Inno Setup script for Verso.   iscc installer\Verso.iss /DAppVersion=0.1.0
;
; Per-user install (no administrator prompt): Verso only ever touches the current user's desktop, registry hive and
; %APPDATA%. User data in %APPDATA%\Verso is deliberately left alone by the uninstaller.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\Verso"
#endif

[Setup]
AppId={{B6B3D1B2-5F3E-4C36-9E1D-6A1E2F6B7A10}
AppName=Verso
AppVersion={#AppVersion}
AppVerName=Verso {#AppVersion}
AppPublisher=Verso
DefaultDirName={autopf}\Verso
DefaultGroupName=Verso
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
SetupIconFile=..\assets\verso.ico
UninstallDisplayIcon={app}\Verso.exe
OutputDir=..\artifacts\installer
OutputBaseFilename=Verso-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; a running Verso holds its own files open; ask to close it instead of failing half way
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "startup"; Description: "Start Verso when I sign in to Windows"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\Verso"; Filename: "{app}\Verso.exe"

[Registry]
; the same value the tray's "Start with Windows" switch writes (Hosting/Autostart.cs)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Verso"; \
  ValueData: """{app}\Verso.exe"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\Verso.exe"; Description: "Start Verso"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; A killed process cannot put back what it hid, so stop Verso, then have a fresh copy restore the taskbar and icons.
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM Verso.exe"; Flags: runhidden; RunOnceId: "StopVerso"
Filename: "{app}\Verso.exe"; Parameters: "--restore-desktop"; Flags: runhidden waituntilterminated; RunOnceId: "RestoreDesktop"
