; GhostClick installer (Inno Setup 6.3+).
;
; Takes the published app folder and produces one GhostClick-Setup-<version>.exe that:
;   - installs for the current user, no administrator prompt, into %LocalAppData%\Programs
;     (like Discord or the VS Code user installer);
;   - adds GhostClick to the Start menu and, unless unticked, to the desktop;
;   - installs the Microsoft Edge WebView2 Runtime if the machine has none;
;   - asks for a running GhostClick to be closed before upgrading or uninstalling it;
;   - registers an uninstaller that also offers to remove the user's profiles.
;
; CI builds it (.github/workflows/windows-build.yml). By hand, from the repository root in cmd:
;   dotnet publish src/GhostClick.App -c Release -r win-x64 --self-contained true -p:DebugType=none -p:PublishDocumentationFiles=false -o publish/app
;   curl.exe -L -o installer\MicrosoftEdgeWebview2Setup.exe "https://go.microsoft.com/fwlink/p/?LinkId=2124703"
;   "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" installer\GhostClick.iss /DAppVersion=1.0.0

#define AppName "GhostClick"
#define AppExe "GhostClick.exe"
#define AppUrl "https://github.com/Sergey5600/GhostClick"

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
; Windows wants four numbers in the file version resource.
#ifndef FileVersion
  #define FileVersion "0.0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\app"
#endif
#ifndef OutputDir
  #define OutputDir "..\publish\installer"
#endif
#ifndef WebView2Setup
  #define WebView2Setup "MicrosoftEdgeWebview2Setup.exe"
#endif
#define WebView2SetupName ExtractFileName(WebView2Setup)

[Setup]
; Never change the AppId: it is how an upgrade finds the installed copy.
AppId={{6E27B7AA-6BAA-4941-93BB-7998B449A0AA}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#FileVersion}
VersionInfoProductName={#AppName}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup

; Per-user only; {autopf} is then %LocalAppData%\Programs. There is deliberately no
; all-users mode: a later per-user upgrade would not find such a copy and would install a
; second one next to it.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

; Welcome -> folder (first install only) -> desktop shortcut -> ready («Установить») ->
; install -> finish.
WizardStyle=modern
DisableWelcomePage=no
DisableDirPage=auto
ShowLanguageDialog=no
WizardImageFile=art\WizardImage-100.bmp,art\WizardImage-125.bmp,art\WizardImage-150.bmp,art\WizardImage-175.bmp,art\WizardImage-200.bmp
WizardSmallImageFile=art\WizardSmallImage-100.bmp,art\WizardSmallImage-125.bmp,art\WizardSmallImage-150.bmp,art\WizardSmallImage-175.bmp,art\WizardSmallImage-200.bmp
SetupIconFile=..\assets\AppIcon.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}

; GhostClick holds this mutex while it runs (see Program.cs): setup and the uninstaller ask
; for it to be closed first, so no file is replaced or removed under a running clicker.
AppMutex=GhostClick.SingleInstance
SetupMutex=GhostClick.Setup
CloseApplications=yes
RestartApplications=no

OutputDir={#OutputDir}
OutputBaseFilename=GhostClick-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes

[Languages]
; The first entry is the fallback: Russian, like the app itself. English only on English Windows.
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
russian.InstallingWebView2=Установка компонента Microsoft Edge WebView2…
english.InstallingWebView2=Installing Microsoft Edge WebView2…
russian.WebView2Failed=Не удалось установить Microsoft Edge WebView2, а без него не откроется интерфейс GhostClick.%n%nУстановите его с сайта Microsoft: https://go.microsoft.com/fwlink/p/?LinkId=2124703
english.WebView2Failed=Microsoft Edge WebView2 could not be installed, and the GhostClick interface needs it.%n%nInstall it from Microsoft: https://go.microsoft.com/fwlink/p/?LinkId=2124703
russian.RemoveProfiles=Удалить также ваши профили GhostClick (назначенные клавиши, точки маршрута, настройки)?%n%nЕсли вы собираетесь установить GhostClick снова, их лучше оставить.
english.RemoveProfiles=Also remove your GhostClick profiles (assigned keys, route points, settings)?%n%nKeep them if you plan to install GhostClick again.

[Messages]
; Replaces the stock "close all other applications" welcome text.
russian.WelcomeLabel2=Будет установлен [name/ver].%n%nGhostClick нажимает назначенные клавиши и кнопки мыши за вас: по таймеру, по точкам маршрута, с горячими клавишами старта, паузы и аварийной остановки.
english.WelcomeLabel2=This will install [name/ver] on your computer.%n%nGhostClick presses the keys and mouse buttons you assign for you: on a timer, along a route of points, with hotkeys to start, pause and stop it.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Unpacked only when WebView2 is missing (see [Code]).
Source: "{#WebView2Setup}"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "{#AppName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "{#AppName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; The interface's browser cache (WebView2 user data). Profiles live elsewhere and are only
; removed if the user says so.
Type: filesandordirs; Name: "{localappdata}\{#AppName}"

[Code]
const
  WebView2ClientKey = 'Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

function HasWebView2Version(RootKey: Integer; SubKey: String): Boolean;
var
  Version: String;
begin
  Result := RegQueryStringValue(RootKey, SubKey, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0');
end;

{ The Evergreen runtime registers per machine (under WOW6432Node) or per user. }
function IsWebView2Installed: Boolean;
begin
  Result := HasWebView2Version(HKLM, 'SOFTWARE\WOW6432Node\' + WebView2ClientKey) or
            HasWebView2Version(HKCU, 'Software\' + WebView2ClientKey);
end;

procedure InstallWebView2;
var
  ResultCode: Integer;
begin
  WizardForm.StatusLabel.Caption := CustomMessage('InstallingWebView2');
  ExtractTemporaryFile('{#WebView2SetupName}');

  if not Exec(ExpandConstant('{tmp}\{#WebView2SetupName}'), '/silent /install', '', SW_HIDE,
              ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    SuppressibleMsgBox(CustomMessage('WebView2Failed'), mbError, MB_OK, IDOK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and not IsWebView2Installed then
    InstallWebView2;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Cache, Profiles: String;
  Attempt: Integer;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  { The page's browser processes outlive GhostClick by a moment and can still hold the cache
    when [UninstallDelete] runs right after the app was closed: retry for up to 10 seconds. }
  Cache := ExpandConstant('{localappdata}\{#AppName}');
  for Attempt := 1 to 20 do
  begin
    if not DirExists(Cache) then
      Break;
    if DelTree(Cache, True, True, True) then
      Break;
    Sleep(500);
  end;

  { Profiles are the user's work: ask, default to keeping them, and keep them without asking
    in a silent uninstall - including the /SILENT one Windows tools run, which does not
    suppress message boxes. }
  Profiles := ExpandConstant('{userappdata}\{#AppName}');
  if UninstallSilent or not DirExists(Profiles) then
    Exit;
  if MsgBox(CustomMessage('RemoveProfiles'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    DelTree(Profiles, True, True, True);
end;
