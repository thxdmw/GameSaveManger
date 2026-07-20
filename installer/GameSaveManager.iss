#ifndef MyAppVersion
  #error "必须由 build-installer.ps1 提供 MyAppVersion"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif

#define MyAppName "GameSave Manager"
#define MyAppPublisher "GameSave Manager"
#define MyAppExeName "GameSaveManager.exe"

[Setup]
AppId={{6D8359E8-72A1-4E22-82CF-0E7E20B6B76F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\GameSaveManager
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\artifacts\installer
OutputBaseFilename=GameSaveManager-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesInstallIn64BitMode=x64compatible
#ifdef SignToolName
SignTool={#SignToolName}
SignedUninstaller=yes
#endif

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加选项："

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  RollbackDirectory: string;
  RollbackInstaller: string;
begin
  if CurStep <> ssPostInstall then
    Exit;

  RollbackDirectory := ExpandConstant('{localappdata}\GameSaveManager\rollback');
  RollbackInstaller := RollbackDirectory + '\GameSaveManager-Setup-{#MyAppVersion}.exe';
  if not ForceDirectories(RollbackDirectory) then
  begin
    Log('Unable to create rollback installer directory: ' + RollbackDirectory);
    Exit;
  end;
  if not CopyFile(ExpandConstant('{srcexe}'), RollbackInstaller, False) then
    Log('Unable to retain rollback installer: ' + RollbackInstaller);
end;
