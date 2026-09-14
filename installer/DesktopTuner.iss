#define AppName "Desktop Tuner"
#ifndef AppVersion
#define AppVersion "0.1.0"
#endif
#define AppPublisher "Desktop Tuner"
#define AppExeName "DesktopTuner.exe"

[Setup]
AppId={{38D1A02D-B530-4D83-B9EA-980984A724B6}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\DesktopTuner
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir=..\artifacts\installer
OutputBaseFilename=DesktopTuner-Setup-win-x64
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Files]
Source: "..\artifacts\DesktopTuner\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{app}\{#AppExeName}"; Parameters: "--remove-folder-shell-integration"; Flags: runhidden waituntilterminated
