#define AppName "Command Palette"
#define AppVersion "0.1.0"
#define AppExeName "CommandPalette.exe"
#define PublishDir "..\CommandPalette\bin\Release\net10.0-windows\win-x64\publish"

#if !FileExists(PublishDir + "\" + AppExeName)
  #error "The win-x64 publish output was not found. Publish the application before compiling the installer."
#endif

[Setup]
AppId={{1C5D85A5-82AD-469D-815F-7BEC915895E5}
AppName={#AppName}
AppPublisher=macedov
AppPublisherURL=https://github.com/macedov
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
DefaultDirName={localappdata}\Programs\CommandPalette
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir=output
OutputBaseFilename=CommandPalette-Setup-{#AppVersion}
SetupIconFile=..\CommandPalette\CommandPalette.ico
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter={#AppExeName}
RestartApplications=no
SetupMutex=CommandPaletteSetup-1C5D85A5-82AD-469D-815F-7BEC915895E5
UsePreviousAppDir=yes
VersionInfoCompany=macedov
VersionInfoVersion=0.1.0.0
VersionInfoProductVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  { The app owns this value; uninstall only removes a stale startup entry. }
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(
      HKEY_CURRENT_USER,
      'Software\Microsoft\Windows\CurrentVersion\Run',
      'CommandPalette');
end;
