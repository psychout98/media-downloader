#define MyAppName "Media Downloader"
#define MyAppVersion GetEnv('APP_VERSION')
#if MyAppVersion == ""
  #define MyAppVersion "0.0.1"
#endif
#define MyAppPublisher "psychout98"
#define MyAppURL "https://github.com/psychout98/media-downloader"
#define MyAppExeName "MediaDownloader.Wpf.exe"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=Output
OutputBaseFilename=MediaDownloader-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Components]
Name: "app"; Description: "Media Downloader Application"; Types: full compact custom; Flags: fixed
Name: "backend"; Description: "Backend Server + Web UI"; Types: full compact custom; Flags: fixed

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startmenu"; Description: "Create Start Menu shortcut"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checked
Name: "startup"; Description: "Start on Windows login"; GroupDescription: "Startup:"

[Files]
; WPF app
Source: "..\publish\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs; Components: app
; Backend + frontend
Source: "..\publish\backend\*"; DestDir: "{app}\backend"; Flags: ignoreversion recursesubdirs; Components: backend

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenu
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"; Tasks: startmenu
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Start on boot
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill"; Parameters: "/f /im {#MyAppExeName}"; Flags: runhidden
Filename: "taskkill"; Parameters: "/f /im MediaDownloader.Api.exe"; Flags: runhidden

[UninstallDelete]
Type: filesandordirs; Name: "{app}\backend\logs"

[Code]
var
  ConfigPage: TInputQueryWizardPage;
  TmdbKeyEdit: TNewEdit;
  RdKeyEdit: TNewEdit;
  MoviesDirEdit: TNewEdit;
  TvDirEdit: TNewEdit;
  ArchiveDirEdit: TNewEdit;

procedure InitializeWizard;
begin
  ConfigPage := CreateInputQueryPage(wpSelectTasks,
    'Configuration', 'Configure Media Downloader settings',
    'Enter your API keys and media directories. These can be changed later in Settings.');

  ConfigPage.Add('TMDB API Key:', False);
  ConfigPage.Add('Real-Debrid API Key:', False);
  ConfigPage.Add('Movies Directory:', False);
  ConfigPage.Add('TV Shows Directory:', False);
  ConfigPage.Add('Archive Directory:', False);

  ConfigPage.Values[0] := '';
  ConfigPage.Values[1] := '';
  ConfigPage.Values[2] := 'D:\Media\Movies';
  ConfigPage.Values[3] := 'D:\Media\TV';
  ConfigPage.Values[4] := 'D:\Media\Archive';
end;

procedure WriteEnvFile;
var
  EnvFile: String;
  Lines: TStringList;
  AppDataDir: String;
begin
  AppDataDir := ExpandConstant('{commonappdata}\MediaDownloader');
  ForceDirectories(AppDataDir);
  ForceDirectories(AppDataDir + '\posters');
  ForceDirectories(AppDataDir + '\staging');
  ForceDirectories(AppDataDir + '\logs');

  if ConfigPage.Values[2] <> '' then
    ForceDirectories(ConfigPage.Values[2]);
  if ConfigPage.Values[3] <> '' then
    ForceDirectories(ConfigPage.Values[3]);
  if ConfigPage.Values[4] <> '' then
    ForceDirectories(ConfigPage.Values[4]);

  EnvFile := ExpandConstant('{app}\backend\.env');
  Lines := TStringList.Create;
  try
    Lines.Add('TMDB_API_KEY=' + ConfigPage.Values[0]);
    Lines.Add('REAL_DEBRID_API_KEY=' + ConfigPage.Values[1]);
    Lines.Add('MOVIES_DIR=' + ConfigPage.Values[2]);
    Lines.Add('TV_DIR=' + ConfigPage.Values[3]);
    Lines.Add('ARCHIVE_DIR=' + ConfigPage.Values[4]);
    Lines.Add('APP_DATA_DIR=' + AppDataDir);
    Lines.Add('WATCH_THRESHOLD=0.85');
    Lines.Add('MPC_BE_URL=http://127.0.0.1:13579');
    Lines.Add('MPC_BE_EXE=C:\Program Files\MPC-BE x64\mpc-be64.exe');
    Lines.Add('HOST=0.0.0.0');
    Lines.Add('PORT=8000');
    Lines.Add('MAX_CONCURRENT_DOWNLOADS=2');
    Lines.Add('RD_POLL_INTERVAL=30');
    Lines.Add('GITHUB_REPO=psychout98/media-downloader');
    Lines.SaveToFile(EnvFile);
  finally
    Lines.Free;
  end;
end;

procedure AddFirewallException;
var
  ResultCode: Integer;
begin
  Exec('netsh', 'advfirewall firewall add rule name="Media Downloader Backend" dir=in action=allow protocol=TCP localport=8000 program="' + ExpandConstant('{app}\backend\MediaDownloader.Api.exe') + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure RemoveFirewallException;
var
  ResultCode: Integer;
begin
  Exec('netsh', 'advfirewall firewall delete rule name="Media Downloader Backend"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    WriteEnvFile;
    AddFirewallException;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RemoveFirewallException;
  end;
end;
