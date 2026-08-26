#define MyAppName "NetPulse Monitor"
#define MyAppVersion "1.0.25"
#define MyAppPublisher "CosmicOlorin"
#define MyAppExeName "NetPulse Monitor.exe"

[Setup]
AppId={{7C4EE3E3-84E9-48E3-AEDB-510DDDC3EC98}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\NetPulse Monitor
DefaultGroupName=NetPulse Monitor
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=NetPulse-Monitor-Setup-{#MyAppVersion}-win-x64
SetupIconFile=..\Assets\NetPulse.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} installer
VersionInfoCopyright=Copyright (c) 2026 CosmicOlorin

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\artifacts\publish\win-x64\NetPulse Monitor.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\NetPulse Monitor"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\NetPulse Monitor"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch NetPulse Monitor"; Flags: nowait postinstall skipifsilent
