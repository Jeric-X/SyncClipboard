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
ShowLanguageDialog=auto
LanguageDetectionMethod=uilanguage

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked; OnlyBelowVersion: 6.1; Check: not IsAdminInstallMode

[Files]
Source: "{#SourceFolder}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: quicklaunchicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  OldVersion: String;
  UninstallPath: String;
  ErrorCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    if RegQueryStringValue(HKEY_LOCAL_MACHINE,
      'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppId}_is1',
      'UninstallString', UninstallPath) then
    begin
      UninstallPath := RemoveQuotes(UninstallPath);
      if FileExists(UninstallPath) then
      begin
        UninstallPath := ExtractFilePath(UninstallPath);
        if MsgBox('检测到已安装的版本，是否先卸载？', mbConfirmation, MB_YESNO) = IDYES then
        begin
          UninstallPath := UninstallPath + 'unins000.exe';
          Exec(UninstallPath, '/SILENT', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
        end;
      end;
    end;
  end;
end;
