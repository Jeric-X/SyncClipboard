#define AppName "SyncClipboard"
#define AppPublisher "SyncClipboard"
#define AppId "{{8F7A3B2C-9D4E-5F6A-8B7C-3D2E1F0A9B8C}}"
#define AppExe "SyncClipboard.exe"

#ifndef SourceFolder
  #define SourceFolder "."
#endif

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#ifndef OutputDir
  #define OutputDir "output"
#endif

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
OutputDir={#OutputDir}
OutputBaseFilename={#AppName}-{#AppVersion}-installer
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\{#AppExe}
DisableProgramGroupPage=no
DisableWelcomePage=no
DisableDirPage=no
AlwaysShowDirOnReadyPage=yes
ShowLanguageDialog=auto
LanguageDetectionMethod=uilanguage
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
chinesesimplified.AppRunning=检测到 %1 正在运行。
chinesesimplified.NeedClose=安装前需要关闭应用程序。是否自动关闭？
chinesesimplified.UninstallNeedClose=卸载前需要关闭应用程序。是否自动关闭？
chinesesimplified.ManualClose=请手动关闭 %1 后重试。
chinesesimplified.OldVersionFound=检测到已安装的版本，是否先卸载？
chinesesimplified.KillFailed=无法自动关闭 %1。请手动关闭应用程序后重试。
chinesesimplified.ExistingInstall=检测到已安装的版本：%1%n安装路径：%2%n%n请选择操作：
chinesesimplified.UninstallFirst=卸载旧版本后安装
chinesesimplified.OverwriteInstall=覆盖安装（保留设置）
chinesesimplified.CancelInstall=取消安装

english.AppRunning=Detected that %1 is running.
english.NeedClose=The application needs to be closed before installation. Close automatically?
english.UninstallNeedClose=The application needs to be closed before uninstallation. Close automatically?
english.ManualClose=Please close %1 manually and try again.
english.OldVersionFound=An existing installation was detected. Uninstall it first?
english.KillFailed=Failed to automatically close %1. Please close the application manually and try again.
english.ExistingInstall=An existing installation was detected: %1%nInstallation path: %2%n%nPlease choose an action:
english.UninstallFirst=Uninstall old version then install
english.OverwriteInstall=Overwrite installation (keep settings)
english.CancelInstall=Cancel installation

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked; OnlyBelowVersion: 6.1; Check: not IsAdminInstallMode

[Files]
Source: "{#SourceFolder}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs uninsrestartdelete

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: quicklaunchicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
function IsAppRunning(const AppExe: String): Boolean;
var
  WMICmd: String;
  ResultCode: Integer;
begin
  Result := False;
  WMICmd := 'tasklist /FI "IMAGENAME eq ' + AppExe + '" /NH';
  if Exec('cmd.exe', '/C ' + WMICmd + ' | find /I "' + AppExe + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := (ResultCode = 0);
  end;
end;

function KillApp(const AppExe: String): Boolean;
var
  ResultCode: Integer;
  RetryCount: Integer;
  MaxRetries: Integer;
begin
  MaxRetries := 3;
  Result := False;
  
  for RetryCount := 1 to MaxRetries do
  begin
    Exec('taskkill.exe', '/F /IM ' + AppExe, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(1000);
    
    if not IsAppRunning(AppExe) then
    begin
      Result := True;
      Break;
    end;
  end;
end;

function TryCloseApp(const AppExe: String; const NeedCloseMsg: String): Boolean;
var
  Msg: String;
begin
  Result := False;
  
  Msg := FmtMessage(ExpandConstant('{cm:AppRunning}'), ['{#AppName}']);
  if MsgBox(Msg + #13#10 + #13#10 + ExpandConstant(NeedCloseMsg), 
            mbConfirmation, MB_YESNO) = IDYES then
  begin
    if KillApp(AppExe) then
    begin
      Result := True;
    end
    else
    begin
      MsgBox(FmtMessage(ExpandConstant('{cm:KillFailed}'), ['{#AppName}']), mbError, MB_OK);
      Result := False;
    end;
  end
  else
  begin
    MsgBox(FmtMessage(ExpandConstant('{cm:ManualClose}'), ['{#AppName}']), mbError, MB_OK);
    Result := False;
  end;
end;

var
  ShouldUninstallOld: Boolean;

function InitializeSetup(): Boolean;
var
  AppPath: String;
  OldVersion: String;
  OldInstallPath: String;
  UninstallPath: String;
  Msg: String;
  ResultCode: Integer;
begin
  Result := True;
  ShouldUninstallOld := False;
  
  if IsAppRunning('{#AppExe}') then
  begin
    Result := TryCloseApp('{#AppExe}', 'NeedClose');
    if not Result then
      Exit;
  end;
  
  if RegQueryStringValue(HKEY_LOCAL_MACHINE,
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppId}_is1',
    'UninstallString', UninstallPath) then
  begin
    UninstallPath := RemoveQuotes(UninstallPath);
    if FileExists(UninstallPath) then
    begin
      RegQueryStringValue(HKEY_LOCAL_MACHINE,
        'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppId}_is1',
        'DisplayVersion', OldVersion);
      
      RegQueryStringValue(HKEY_LOCAL_MACHINE,
        'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppId}_is1',
        'InstallLocation', OldInstallPath);
      
      if OldInstallPath = '' then
        OldInstallPath := ExtractFilePath(UninstallPath);
      
      Msg := FmtMessage(ExpandConstant('{cm:ExistingInstall}'), [OldVersion, OldInstallPath]) + #13#10 + #13#10 +
             '[是] - ' + ExpandConstant('{cm:UninstallFirst}') + #13#10 +
             '[否] - ' + ExpandConstant('{cm:OverwriteInstall}') + #13#10 +
             '[取消] - ' + ExpandConstant('{cm:CancelInstall}');
      
      case MsgBox(Msg, mbConfirmation, MB_YESNOCANCEL) of
        IDYES:
        begin
          ShouldUninstallOld := True;
          Result := True;
        end;
        IDNO:
        begin
          ShouldUninstallOld := False;
          Result := True;
        end;
        IDCANCEL:
        begin
          Result := False;
        end;
      end;
    end;
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  
  if IsAppRunning('{#AppExe}') then
  begin
    Result := TryCloseApp('{#AppExe}', 'UninstallNeedClose');
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  OldVersion: String;
  UninstallPath: String;
  ErrorCode: Integer;
begin
  if (CurStep = ssInstall) and ShouldUninstallOld then
  begin
    if RegQueryStringValue(HKEY_LOCAL_MACHINE,
      'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppId}_is1',
      'UninstallString', UninstallPath) then
    begin
      UninstallPath := RemoveQuotes(UninstallPath);
      if FileExists(UninstallPath) then
      begin
        Exec(UninstallPath, '/SILENT', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
      end;
    end;
  end;
end;
