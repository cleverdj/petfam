; PetFam installer (Inno Setup 6).
; Built by build\build-installer.ps1, which publishes the app first and passes the version:
;   ISCC.exe /DAppVersion=1.0.0 installer\PetFam.iss
;
; Installs for the current user only (no administrator prompt) into %LOCALAPPDATA%\Programs\PetFam.
; The pets themselves live in %LOCALAPPDATA%\PetFam and are never touched by installing, updating or uninstalling.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "PetFam"
#define AppExe "PetFam.exe"

[Setup]
AppId={{7D3B6E2A-4C1F-4B8E-9A55-2F1C0E7B9D41}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=PetFam
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\artifacts
OutputBaseFilename=PetFam-Setup-{#AppVersion}
SetupIconFile=..\src\PetFam.App\Assets\petfam.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
; Close a running PetFam before updating or removing it.
CloseApplications=force
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[CustomMessages]
english.AutoStart=Start PetFam when I sign in to Windows (recommended)
korean.AutoStart=Windows에 로그인하면 PetFam 자동으로 시작 (권장)
english.StartGroup=Starting:
korean.StartGroup=시작:

[Tasks]
Name: "autostart"; Description: "{cm:AutoStart}"; GroupDescription: "{cm:StartGroup}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\artifacts\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Start at sign-in (per user). Removed again on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; \
    ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/im {#AppExe} /f"; Flags: runhidden; RunOnceId: "StopPetFam"
