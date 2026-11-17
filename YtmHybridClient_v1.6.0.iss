#define MyAppName "YTM Hybrid Client"
#define MyAppVersion "1.6.0"
#define MyAppPublisher "Hanzone"
#define MyAppURL "https://github.com/egehansimarikk/YtmHybridClient-Charp"
#define MyAppExeName "YtmHybridClient.exe"
#define ProjectDir "C:\Users\Egehan\source\repos\YtmHybridClient-Charp"
#define ReleaseDir "C:\Users\Egehan\source\repos\YtmHybridClient-Charp\bin\Release\net9.0-windows10.0.17763.0"
#define OutputDirPath "C:\Users\Egehan\source\repos\YtmHybridClient-Charp\Output"

[Setup]
AppId={{8E4F9A12-3B7C-4D9E-A512-9F8C7B6E1600}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
DisableProgramGroupPage=yes
OutputDir={#OutputDirPath}
OutputBaseFilename=YTM_Hybrid_Client_v{#MyAppVersion}_Setup
SetupIconFile={#ReleaseDir}\ytmhybridlogo.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardResizable=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
CloseApplicationsFilter=*.exe
RestartApplications=no
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startupicon"; Description: "Windows açılışında otomatik başlat / Start with Windows"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; Eski sürümden kalan tüm program dosyalarını ve alt klasörleri kurulumdan önce tamamen temizler
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "{#ReleaseDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,*.xml"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\ytmhybridlogo.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\ytmhybridlogo.ico"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\ytmhybridlogo.ico"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// Kurulum başlamadan önce çalışan YtmHybridClient.exe varsa zorla kapatır ve varsa eski Uninstaller'ı sessizce çalıştırır
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
  UninstallString: String;
  RegKey: String;
begin
  Result := True;

  // Arka planda/tepside açık kalmış eski örneği kapat
  Exec('taskkill.exe', '/F /IM YtmHybridClient.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // Önceki kurulumun kayıt defteri anahtarını kontrol et (HKCU ve HKLM)
  RegKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8E4F9A12-3B7C-4D9E-A512-9F8C7B6E1600}_is1';
  if RegQueryStringValue(HKCU, RegKey, 'UninstallString', UninstallString) or
     RegQueryStringValue(HKLM, RegKey, 'UninstallString', UninstallString) then
  begin
    UninstallString := RemoveQuotes(UninstallString);
    if FileExists(UninstallString) then
    begin
      Exec(UninstallString, '/VERYSILENT /NORESTART /SUPPRESSMSGBOXES', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;
  end;
end;