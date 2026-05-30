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

#ifndef TargetArch
  #define TargetArch "x64"
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
SetupIconFile=..\..\src\SyncClipboard.WinUI3\Assets\icon.ico
UninstallDisplayIcon={app}\{#AppExe}
DisableProgramGroupPage=no
DisableWelcomePage=no
DisableDirPage=no
AlwaysShowDirOnReadyPage=yes
ShowLanguageDialog=auto
LanguageDetectionMethod=uilanguage
ArchitecturesAllowed={#TargetArch}
ArchitecturesInstallIn64BitMode={#TargetArch}
MinVersion=10.0

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
chinesesimplified.AppRunning=检测到 %1 正在运行。
chinesesimplified.NeedClose=安装前需要关闭应用程序。是否自动关闭？
chinesesimplified.UninstallNeedClose=卸载前需要关闭应用程序。是否自动关闭？
chinesesimplified.ManualClose=请手动关闭 %1 后重试。
chinesesimplified.KillFailed=无法自动关闭 %1。请手动关闭应用程序后重试。
chinesesimplified.CreateDesktopIcon=创建桌面快捷方式(&D)
chinesesimplified.AdditionalIcons=附加图标:
chinesesimplified.LaunchProgram=运行 %1
chinesesimplified.UninstallProgram=卸载 %1
chinesesimplified.AlreadyInstalled=检测到 {#AppName} 已安装。%n%n已安装版本：%1%n安装路径：%2%n%n是否覆盖安装？
chinesesimplified.RemoveAppDataPrompt=是否同时删除以下应用数据和配置目录？

english.AppRunning=Detected that %1 is running.
english.NeedClose=The application needs to be closed before installation. Close automatically?
english.UninstallNeedClose=The application needs to be closed before uninstallation. Close automatically?
english.ManualClose=Please close %1 manually and try again.
english.KillFailed=Failed to automatically close %1. Please close the application manually and try again.
english.CreateDesktopIcon=Create a &desktop shortcut
english.AdditionalIcons=Additional icons:
english.LaunchProgram=Launch %1
english.UninstallProgram=Uninstall %1
english.AlreadyInstalled=Detected {#AppName} is already installed.%n%nInstalled version: %1%nInstall path: %2%n%nDo you want to overwrite the existing installation?
english.RemoveAppDataPrompt=Would you also like to remove the following application data and settings directories?

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceFolder}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs uninsrestartdelete

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  GOverwriteInstall: Boolean;
  GExistingInstallPath: String;
  GRemoveAppData: Boolean;

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

function GetInstalledInfo(var Version: String; var InstallPath: String): Boolean;
var
  RegKey: String;
begin
  RegKey := ExpandConstant('Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppId}_is1');
  Result := False;
  Version := '';
  InstallPath := '';
  if RegQueryStringValue(HKCU, RegKey, 'DisplayVersion', Version) then
  begin
    RegQueryStringValue(HKCU, RegKey, 'InstallLocation', InstallPath);
    Result := True;
  end
  else if RegQueryStringValue(HKLM, RegKey, 'DisplayVersion', Version) then
  begin
    RegQueryStringValue(HKLM, RegKey, 'InstallLocation', InstallPath);
    Result := True;
  end;
end;

function ShowAlreadyInstalledDialog(const InstalledVersion: String; const InstallPath: String): Integer;
var
  Msg: String;
begin
  Msg := ExpandConstant('{cm:AlreadyInstalled}');
  StringChange(Msg, '%1', InstalledVersion);
  StringChange(Msg, '%2', InstallPath);
  if MsgBox(Msg, mbConfirmation, MB_YESNO) = IDNO then
    Result := 3  // Cancel
  else
    Result := 1; // Overwrite
end;

function InitializeSetup(): Boolean;
var
  InstalledVersion, InstallPath: String;
  Choice: Integer;
begin
  Result := True;

  if GetInstalledInfo(InstalledVersion, InstallPath) then
  begin
    Choice := ShowAlreadyInstalledDialog(InstalledVersion, InstallPath);
    if Choice = 1 then
    begin
      GOverwriteInstall := True;
      GExistingInstallPath := InstallPath;
    end
    else
    begin
      Result := False;
      Exit;
    end;
  end;

  if IsAppRunning('{#AppExe}') then
  begin
    Result := TryCloseApp('{#AppExe}', '{cm:NeedClose}');
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;

  if IsAppRunning('{#AppExe}') then
  begin
    Result := TryCloseApp('{#AppExe}', '{cm:UninstallNeedClose}');
  end;
end;

procedure InitializeWizard;
begin
  if GOverwriteInstall and (GExistingInstallPath <> '') then
    WizardForm.DirEdit.Text := GExistingInstallPath;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if (PageID = wpSelectDir) and GOverwriteInstall and (GExistingInstallPath <> '') then
    Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Msg: String;
  DataDir: String;
  GlobalDir: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\{#AppName}');
    GlobalDir := ExpandConstant('{userappdata}\{#AppName}_global');
    Msg := ExpandConstant('{cm:RemoveAppDataPrompt}') + #13#10#13#10 +
           DataDir + #13#10 + GlobalDir;
    GRemoveAppData := MsgBox(Msg, mbConfirmation, MB_YESNO) = IDYES;
  end;
  if CurUninstallStep = usPostUninstall then
  begin
    if GRemoveAppData then
    begin
      DataDir := ExpandConstant('{userappdata}\{#AppName}');
      GlobalDir := ExpandConstant('{userappdata}\{#AppName}_global');
      if DirExists(DataDir) then
        DelTree(DataDir, True, True, True);
      if DirExists(GlobalDir) then
        DelTree(GlobalDir, True, True, True);
    end;
  end;
end;
