; سَحَر · Sahar — the installer (owner roadmap item: installer + auto-update, 2026-11-13).
; Built by packaging/installer/build.ps1 with the pinned official Inno Setup 6.7.4:
;   ISCC /DAppVersion=0.14.0 /DAppNumeric=0.14.0 /DEngineDir=... /DRuntimeDir=... /DReadmeFile=... /DThirdPartyFile=... /DOutputDir=... Sahar.iss
; 0.14.0: beside the engine, the settings app (Sahar.Settings\, self-contained, web\ inside), her brain's process
; (Sahar.Mind.exe) and the pinned llama.cpp runtime (runtime\vulkan, runtime\cpu). Models are never bundled: she
; downloads them in the app; CUDA only on his yes (both into %LocalAppData%\Sahar).
; Per user, no admin: %LocalAppData%\Programs\Sahar. Settings (%AppData%\Sahar) and the log (%LocalAppData%\Sahar)
; are never touched by an install or an upgrade; uninstall asks before removing them (default: keep).
; A running Sahar is closed with her own pipe quit ("Sahar.Engine.exe --quit"), never killed; the same --quit closes an
; open settings window (it saves first) and waits for her brain's process.
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
#ifndef RuntimeDir
  #define RuntimeDir EngineDir + "\runtime"
#endif
#ifndef ThirdPartyFile
  #define ThirdPartyFile "..\THIRD-PARTY.txt"
#endif
#ifndef ReadmeFile
  #define ReadmeFile "..\README-test-build.txt"
#endif
#ifndef IconFile
  #define IconFile "..\..\src\Sahar.Engine\assets\sahar.ico"
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
SetupIconFile={#IconFile}
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
arabic.Downgrade=عندك نسخة أحدث من سَحَر مثبّتة (%1).%n%nبدك تبدّلها بهالنسخة الأقدم (%2)؟
english.Downgrade=A newer Sahar is installed (%1).%n%nReplace it with this older version (%2)?

[Tasks]
Name: "startup"; Description: "{cm:StartWithWindows}"

[Files]
Source: "{#EngineDir}\Sahar.Engine.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#EngineDir}\Sahar.Mind.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#EngineDir}\Sahar.Settings\*"; DestDir: "{app}\Sahar.Settings"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RuntimeDir}\vulkan\*"; DestDir: "{app}\runtime\vulkan"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RuntimeDir}\cpu\*"; DestDir: "{app}\runtime\cpu"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#ThirdPartyFile}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ReadmeFile}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\سحر"; Filename: "{app}\Sahar.Engine.exe"; WorkingDir: "{app}"; Comment: "سَحَر · Sahar"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Sahar"; ValueData: """{app}\Sahar.Engine.exe"""; Tasks: startup; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Sahar"; Tasks: not startup; Flags: deletevalue

[Run]
Filename: "{app}\Sahar.Engine.exe"; Description: "{cm:LaunchSahar}"; WorkingDir: "{app}"; Flags: nowait postinstall

[UninstallRun]
Filename: "{app}\Sahar.Engine.exe"; Parameters: "--unlink-claude"; Flags: runhidden; RunOnceId: "SaharUnlinkClaude"

[UninstallDelete]
Type: dirifempty; Name: "{app}"
; The in-app update's downloads (each in its own random folder).
Type: filesandordirs; Name: "{%TEMP}\Sahar-update"

[Code]
const
  EngineMutex = 'Local\Sahar.Engine';
  SettingsMutex = 'Local\Sahar.Settings';   { the settings app (0.12): its window is closed by the same --quit }
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{B24750DD-9A27-4894-8A97-D099CBA9B19C}_is1';

var
  QuitByUs: Boolean;     { this setup closed a running Sahar }
  Finished: Boolean;     { the files are in place }
  WasRunning: String;    { the exe she ran from (the installed one, or a zip folder's) }

{ One number of "a.b.c" (a "-prerelease" is ignored for this comparison). }
function VersionPart(var S: String): Integer;
var
  P: Integer;
begin
  P := Pos('.', S);
  if P = 0 then
  begin
    Result := StrToIntDef(S, 0);
    S := '';
  end
  else
  begin
    Result := StrToIntDef(Copy(S, 1, P - 1), 0);
    Delete(S, 1, P);
  end;
end;

function CompareVersions(A, B: String): Integer;
var
  I, X, Y: Integer;
begin
  if Pos('-', A) > 0 then A := Copy(A, 1, Pos('-', A) - 1);
  if Pos('-', B) > 0 then B := Copy(B, 1, Pos('-', B) - 1);
  Result := 0;
  for I := 1 to 3 do
  begin
    X := VersionPart(A);
    Y := VersionPart(B);
    if X <> Y then
    begin
      if X > Y then Result := 1 else Result := -1;
      Exit;
    end;
  end;
end;

{ An older setup over a newer Sahar asks first (default: no; a silent one stops). }
function InitializeSetup(): Boolean;
var
  Installed: String;
begin
  Result := True;
  if RegQueryStringValue(HKCU, UninstallKey, 'DisplayVersion', Installed) and (CompareVersions(Installed, '{#AppVersion}') > 0) then
  begin
    Log('installed ' + Installed + ' is newer than {#AppVersion}');
    Result := SuppressibleMsgBox(FmtMessage(CustomMessage('Downgrade'), [Installed, '{#AppVersion}']),
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES;
  end;
end;

{ Asks a running Sahar (and an open settings window) to quit with her own pipe command and waits (Exe --quit waits
  up to 20 s for her, 10 s for the window). True when neither is running afterwards. Never kills: a save is never cut short. }
function QuitSahar(const Exe: String): Boolean;
var
  Code: Integer;
begin
  if not CheckForMutexes(EngineMutex + ',' + SettingsMutex) then
  begin
    Result := True;
    Exit;
  end;
  Log('Sahar or her settings are running: asking them to quit');
  if not Exec(Exe, '--quit', '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Log('could not run ' + Exe + ' --quit')
  else
    Log('--quit exit code ' + IntToStr(Code));
  Result := not CheckForMutexes(EngineMutex + ',' + SettingsMutex);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
  Lines: TArrayOfString;
  Note: String;
  EngineWas: Boolean;
begin
  Result := '';
  if CheckForMutexes(EngineMutex + ',' + SettingsMutex) then
  begin
    EngineWas := CheckForMutexes(EngineMutex);
    { The new engine's --quit (works for any older Sahar: the pipe quit is as old as the pipe); --remember notes
      which exe she ran from, so a setup that does not finish can start her again. }
    ExtractTemporaryFile('Sahar.Engine.exe');
    Note := ExpandConstant('{tmp}\sahar-was-running.txt');
    Log('Sahar is running: asking her to quit');
    if not Exec(ExpandConstant('{tmp}\Sahar.Engine.exe'), '--quit --remember "' + Note + '"', '', SW_HIDE, ewWaitUntilTerminated, Code) then
      Log('could not run --quit')
    else
      Log('--quit exit code ' + IntToStr(Code));
    if LoadStringsFromFile(Note, Lines) and (GetArrayLength(Lines) > 0) then WasRunning := Trim(Lines[0]);
    { The settings window closes with the same --quit (it saves first); only Sahar herself is started again. }
    if CheckForMutexes(EngineMutex + ',' + SettingsMutex) then
      Result := CustomMessage('StillRunning')
    else
      QuitByUs := EngineWas;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then Finished := True;
end;

{ A setup that closed her and did not finish (an error, a cancel, a file in use) starts her again as she was. }
procedure DeinitializeSetup();
var
  Code: Integer;
  Exe: String;
begin
  if QuitByUs and not Finished then
  begin
    Exe := WasRunning;
    if (Exe = '') or not FileExists(Exe) then Exe := ExpandConstant('{app}\Sahar.Engine.exe');
    if FileExists(Exe) then
    begin
      Log('the install did not finish: starting Sahar again');
      Exec(Exe, '', ExtractFileDir(Exe), SW_SHOWNORMAL, ewNoWait, Code);
    end;
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
