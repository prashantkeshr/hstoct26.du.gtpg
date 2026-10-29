; Dhurta Host — Windows installer script
; Build locally:  iscc /DMyAppVersion=0.1.0 installer\dhurta-host-setup.iss
; CI runs this automatically via .github/workflows/release.yml on a v* tag push.

#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif

#define MyAppName      "Dhurta Host"
#define MyAppPublisher "Dhurta"
#define MyAppURL       "https://github.com/prashantkeshr/dhurta-host"
#define MyAppExeName   "DhurtaHost.exe"

[Setup]
AppId={{FA3C8E02-D4B7-4A9F-B521-3C6E9D2F0A84}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\DhurtaHost
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=dist
OutputBaseFilename=DhurtaHost-Setup-v{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64os
ArchitecturesAllowed=x64os
MinVersion=10.0.19041
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} — browser-based remote desktop

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "startup";     Description: "Start automatically when Windows starts"; GroupDescription: "Startup:"

[Files]
; All runtime files (self-contained publish — includes .NET runtime DLLs)
; appsettings.json excluded here and handled separately below
Source: "publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion; Excludes: "appsettings.json"

; Default config — preserve user settings (TURN creds, FPS, etc.) on upgrade
Source: "publish\appsettings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist

[Icons]
Name: "{group}\{#MyAppName}";             Filename: "{app}\{#MyAppExeName}"
Name: "{commondesktop}\{#MyAppName}";     Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{group}\Uninstall {#MyAppName}";   Filename: "{uninstallexe}"

[Registry]
; HKCU run key — starts the tray agent with Windows (no admin required at login time)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "DhurtaHost"; \
  ValueData: """{app}\{#MyAppExeName}"""; \
  Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; \
  Description: "Launch {#MyAppName} now"; \
  Flags: nowait postinstall skipifsilent
