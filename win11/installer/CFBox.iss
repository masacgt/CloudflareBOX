#ifndef MyAppVersion
  #define MyAppVersion "dev"
#endif

#define MyAppName "CFBox"
#define InternalInstallDir "CloudflareBOX"

[Setup]
AppId={{8B6C901D-4F2F-4D8A-8E26-4CA6E7BB9961}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=masacgt
DefaultDirName={autopf}\CFBox Installer
DisableProgramGroupPage=yes
PrivilegesRequired=admin
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
Compression=lzma2
SolidCompression=yes
OutputDir=..\installer-output
OutputBaseFilename=CFBox-Setup
SetupIconFile=..\..\assets\cfbox-icon.ico
UninstallDisplayName=CFBox
UninstallDisplayIcon={app}\cfbox-icon.ico
CloseApplications=yes
RestartApplications=no

[Files]
Source: "..\package\*"; DestDir: "{tmp}\CFBoxPayload"; Flags: recursesubdirs createallsubdirs deleteafterinstall
Source: "uninstall.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\assets\cfbox-icon.ico"; DestDir: "{app}"; Flags: ignoreversion

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{tmp}\CFBoxPayload\install.ps1"" -InstallRoot ""{autopf}\{#InternalInstallDir}"" -InstallerRoot ""{tmp}\CFBoxPayload"""; WorkingDir: "{tmp}\CFBoxPayload"; StatusMsg: "CFBoxをインストールしています..."; Flags: waituntilterminated

[Icons]
Name: "{autoprograms}\CFBox"; Filename: "{autopf}\{#InternalInstallDir}\tray\CloudflareBox.Tray.exe"; WorkingDir: "{autopf}\{#InternalInstallDir}\tray"
Name: "{autoprograms}\CFBoxをアンインストール"; Filename: "{uninstallexe}"

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\uninstall.ps1"" -InstallRoot ""{autopf}\{#InternalInstallDir}"""; Flags: runhidden waituntilterminated; RunOnceId: "CFBoxCleanup"
