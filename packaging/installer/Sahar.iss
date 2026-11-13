; سَحَر · Sahar — the installer (owner roadmap item: installer + auto-update, 2026-11-13).
; Built by packaging/installer/build.ps1 with the pinned official Inno Setup 6.7.4:
;   ISCC /DAppVersion=0.11.0 /DAppNumeric=0.11.0 /DEngineDir=... /DReadmeFile=... /DOutputDir=... Sahar.iss
; Per user, no admin: %LocalAppData%\Programs\Sahar. Settings (%AppData%\Sahar) and the log (%LocalAppData%\Sahar)
; are never touched by an install or an upgrade; uninstall asks before removing them (default: keep).
; A running Sahar is closed with her own pipe quit ("Sahar.Engine.exe --quit"), never killed.
; Silent upgrade (the in-app update): /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- — she is started again after.

#ifndef AppVersion
  #error Pass /DAppVersion=<version>
#endif
#ifndef AppNumeric
  #define AppNumeric AppVersion
#endif
#ifndef EngineDir
  #define EngineDir "..\..\artifacts\engine"
#endif
#ifndef ReadmeFile
  #define ReadmeFile "..\README-test-build.txt"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\artifacts\installer"
#endif

[Setup]
AppId={{B24750DD-9A27-4894-8A97-D099CBA9B19C}
AppName=سَحَر · Sahar
AppVersion={#AppVersion}
AppVerName=سَحَر · Sahar {#AppVersion}
AppPublisher=Sahar
VersionInfoVersion={#AppNumeric}
DefaultDirName={userpf}\Sahar
PrivilegesRequired=lowest
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
DisableWelcomePage=yes
CloseApplications=no
RestartApplications=no
ShowLanguageDialog=no
LanguageDetectionMethod=none
MinVersion=10.0.19041
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName=سَحَر · Sahar
UninstallDisplayIcon={app}\Sahar.Engine.exe
OutputDir={#OutputDir}
OutputBaseFilename=Sahar-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes

[Languages]
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
arabic.StartWithWindows=شغّل سَحَر لما يشتغل الويندوز
english.StartWithWindows=Start Sahar with Windows
arabic.LaunchSahar=شغّل سَحَر هلق
english.LaunchSahar=Start Sahar now
arabic.StillRunning=سَحَر لسا شغّالة وما قدرت سكّرها.%n%nسكّرها من أيقونتها جنب الساعة («إغلاق») وجرّب مرة تانية.
english.StillRunning=Sahar is still running and could not be closed.%n%nQuit her from her icon by the clock and try again.
arabic.RemoveData=بدك تمسح إعدادات سَحَر وملاحظاتها وسجلّها كمان؟%n%nإذا اخترت «لا» بيضلّوا، ولما ترجع تثبّتها بترجع متل ما كانت.
english.RemoveData=Also delete Sahar's settings, notes and log?%n%nChoose No to keep them for a later install.

[Tasks]
Name: "startup"; Description: "{cm:StartWithWindows}"

[Files]
Source: "{#EngineDir}\Sahar.Engine.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ReadmeFile}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\سحر"; Filename: "{app}\Sahar.Engine.exe"; WorkingDir: "{app}"; Comment: "سَحَر · Sahar"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Sahar"; ValueData: """{app}\Sahar.Engine.exe"""; Tasks: startup; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Sahar"; Tasks: not startup; Flags: deletevalue

[Run]
Filename: "{app}\Sahar.Engine.exe"; Description: "{cm:LaunchSahar}"; WorkingDir: "{app}"; Flags: nowait postinstall

[UninstallDelete]
Type: dirifempty; Name: "{app}"

[Code]
const
  EngineMutex = 'Local\Sahar.Engine';

{ Asks a running Sahar to quit with her own pipe command and waits for her (Exe --quit waits up to 20 s).
  True when she is not running afterwards. Never kills her: a save is never cut short. }
function QuitSahar(const Exe: String): Boolean;
var
  Code: Integer;
begin
  if not CheckForMutexes(EngineMutex) then
  begin
    Result := True;
    Exit;
  end;
  Log('Sahar is running: asking her to quit');
  if not Exec(Exe, '--quit', '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Log('could not run ' + Exe + ' --quit')
  else
    Log('--quit exit code ' + IntToStr(Code));
  Result := not CheckForMutexes(EngineMutex);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if CheckForMutexes(EngineMutex) then
  begin
    { The new engine's --quit (works for any older Sahar: the pipe quit is as old as the pipe). }
    ExtractTemporaryFile('Sahar.Engine.exe');
    if not QuitSahar(ExpandConstant('{tmp}\Sahar.Engine.exe')) then
      Result := CustomMessage('StillRunning');
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  if not QuitSahar(ExpandConstant('{app}\Sahar.Engine.exe')) then
  begin
    SuppressibleMsgBox(CustomMessage('StillRunning'), mbError, MB_OK, IDOK);
    Result := False;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  { Her settings, notes and log stay unless you say yes; a silent uninstall always keeps them. }
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    if MsgBox(CustomMessage('RemoveData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      DelTree(ExpandConstant('{userappdata}\Sahar'), True, True, True);
      DelTree(ExpandConstant('{localappdata}\Sahar'), True, True, True);
    end;
end;
