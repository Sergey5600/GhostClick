; GhostClick installer (Inno Setup 6.3+).
;
; Takes the published app folder and produces one GhostClick-Setup-<version>.exe that:
;   - installs for the current user, no administrator prompt, into %LocalAppData%\Programs
;     (like Discord or the VS Code user installer);
;   - adds GhostClick to the Start menu and, unless unticked, to the desktop;
;   - installs the Microsoft Edge WebView2 Runtime if the machine has none;
;   - asks for a running GhostClick to be closed before upgrading or uninstalling it;
;   - when GhostClick runs it for an update (/UPDATE=1), waits for GhostClick to exit and,
;     with /RELAUNCH=1, starts it again at the end;
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

; WizardImageSizes, WizardSmallImageSizes and the defaults (assets/build-installer-art.sh).
#include "art\sizes.iss"

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
; The pictures are drawn for the wizard's exact size (see [Code]); a wizard the user could
; resize would have them resampled again.
WizardResizable=no
; What Setup shows if [Code] cannot do better; the other sizes are in [Files].
WizardImageFile=art\WizardImage-{#DefaultWizardImage}.bmp
WizardSmallImageFile=art\WizardSmallImage-{#DefaultWizardSmallImage}.bmp
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
; Unpacked only while Setup runs (see [Code]). Listed first: with solid compression, getting
; a file out means unpacking everything stored before it.
Source: "art\WizardImage-*.bmp"; Flags: dontcopy
Source: "art\WizardSmallImage-*.bmp"; Flags: dontcopy
Source: "{#WebView2Setup}"; Flags: dontcopy
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "{#AppName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "{#AppName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; An update GhostClick started itself («Перезапустить»): it comes back once installed.
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: RelaunchAfterUpdate

[UninstallDelete]
; The interface's browser cache (WebView2 user data). Profiles live elsewhere and are only
; removed if the user says so.
Type: filesandordirs; Name: "{localappdata}\{#AppName}"

[Code]
const
  WebView2ClientKey = 'Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

{ GhostClick runs its update's installer as it exits (Program.cs). The app's mutex can
  outlive the moment the installer starts by a little: wait for it here, before Setup checks
  AppMutex and would ask to close GhostClick - which, silent, would mean giving up. }
function InitializeSetup: Boolean;
var
  Attempt: Integer;
begin
  Result := True;
  if ExpandConstant('{param:UPDATE|0}') <> '1' then
    Exit;
  for Attempt := 1 to 60 do
  begin
    if not CheckForMutexes('{#AppName}.SingleInstance') then
      Break;
    Sleep(500);
  end;
end;

function RelaunchAfterUpdate: Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH|0}') = '1';
end;

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

{ Setup picks a wizard picture before it enlarges the modern wizard to 120 % and then
  resamples it to its box, which blurs it; the header picture's box is 55x58, not square,
  so a square icon came out squashed. art\ has each picture drawn for the exact box of
  each display scale (installer/wizard-sizes.ps1), so once the wizard has its final size,
  show the one that matches. }

{ From Sizes ("WxH WxH ...", smallest first): the smallest that covers the box, or with
  Inside, the largest that fits inside it. }
function PickPictureSize(const Sizes: String; const BoxWidth, BoxHeight: Integer;
  const Inside: Boolean; var Width, Height: Integer): Boolean;
var
  Rest, Size: String;
  P, W, H: Integer;
begin
  Result := False;
  Rest := Trim(Sizes) + ' ';
  while Rest <> '' do begin
    P := Pos(' ', Rest);
    Size := Copy(Rest, 1, P - 1);
    Delete(Rest, 1, P);
    P := Pos('x', Size);
    W := StrToIntDef(Copy(Size, 1, P - 1), 0);
    H := StrToIntDef(Copy(Size, P + 1, Length(Size)), 0);
    if Inside then begin
      if (W <= BoxWidth) and (H <= BoxHeight) then begin
        Width := W;
        Height := H;
        Result := True;
      end;
    end else if (W >= BoxWidth) and (H >= BoxHeight) then begin
      Width := W;
      Height := H;
      Result := True;
      Exit;
    end;
  end;
end;

function BitBlt(DestDC: LongWord; X, Y, Width, Height: Integer; SrcDC: LongWord;
  XSrc, YSrc: Integer; Rop: LongWord): BOOL; external 'BitBlt@gdi32.dll stdcall';

{ Shows the best picture for Image's box pixel for pixel. The header icon is centred on the
  white header. A side picture drawn for a slightly larger box loses a few pixels of
  background at the edges; one much larger (an unusual display scale) is left to Setup to
  scale down. }
procedure ShowWizardPicture(const Image: TBitmapImage; const Prefix, Sizes: String;
  const Inside: Boolean);
var
  W, H, BoxWidth, BoxHeight: Integer;
  Name, How: String;
  Picture, Fitted: TBitmap;
begin
  BoxWidth := Image.Width;
  BoxHeight := Image.Height;
  if not PickPictureSize(Sizes, BoxWidth, BoxHeight, Inside, W, H) then begin
    Log(Format('Wizard picture %s: no picture for a %dx%d box', [Prefix, BoxWidth, BoxHeight]));
    Exit;
  end;

  Name := Format('%s-%dx%d.bmp', [Prefix, W, H]);
  ExtractTemporaryFile(Name);
  Picture := TBitmap.Create;
  Fitted := TBitmap.Create;
  try
    Picture.LoadFromFile(ExpandConstant('{tmp}\') + Name);
    if Inside or ((W = BoxWidth) and (H = BoxHeight)) then begin
      Image.Stretch := False;
      Image.Center := True;
      Image.Bitmap := Picture;
      How := 'shown pixel for pixel';
    end else if (W - BoxWidth > BoxWidth div 16) or (H - BoxHeight > BoxHeight div 16) then begin
      Image.Bitmap := Picture;
      How := 'scaled by Setup';
    end else begin
      Fitted.Width := BoxWidth;
      Fitted.Height := BoxHeight;
      BitBlt(Fitted.Canvas.Handle, 0, 0, BoxWidth, BoxHeight, Picture.Canvas.Handle,
        (W - BoxWidth) div 2, (H - BoxHeight) div 2, $00CC0020 { SRCCOPY });
      Image.Stretch := False;
      Image.Bitmap := Fitted;
      How := 'cropped to fit, shown pixel for pixel';
    end;
    Log(Format('Wizard picture %s: %dx%d box, %s %s', [Prefix, BoxWidth, BoxHeight, Name, How]));
  finally
    Fitted.Free;
    Picture.Free;
  end;
end;

var
  WizardPicturesShown: Boolean;

procedure CurPageChanged(CurPageID: Integer);
begin
  { The welcome page comes first and the wizard has its final size by then. }
  if (CurPageID = wpWelcome) and not WizardSilent and not WizardPicturesShown then begin
    WizardPicturesShown := True;
    ShowWizardPicture(WizardForm.WizardBitmapImage, 'WizardImage', '{#WizardImageSizes}', False);
    ShowWizardPicture(WizardForm.WizardBitmapImage2, 'WizardImage', '{#WizardImageSizes}', False);
    ShowWizardPicture(WizardForm.WizardSmallBitmapImage, 'WizardSmallImage', '{#WizardSmallImageSizes}', True);
  end;
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
