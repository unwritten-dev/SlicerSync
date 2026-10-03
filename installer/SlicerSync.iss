; Inno-Setup-Skript für SlicerSync.
; Installiert die App und lädt bei Bedarf Git for Windows (inkl. Git Credential Manager)
; herunter und installiert es still. Wird von build.ps1 aufgerufen, das die aktuelle
; Git-Version per /D übergibt; die Werte unten sind nur der Rückfall.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef GitVersion
  #define GitVersion "2.56.0"
#endif
#ifndef GitUrl
  #define GitUrl "https://github.com/git-for-windows/git/releases/download/v2.56.0.windows.1/Git-2.56.0-64-bit.exe"
#endif
#ifndef GitSha256
  #define GitSha256 "bfe94e7b419b16eee9fecbd1253a98e3d4f49ba8f029630549052278ffe286a6"
#endif

[Setup]
AppId={{6F1C2B7E-5A4D-4C1B-9E3A-2D7F5C8B1A90}
AppName=SlicerSync
AppVersion={#AppVersion}
AppPublisher=SlicerSync
DefaultDirName={autopf}\SlicerSync
DefaultGroupName=SlicerSync
DisableProgramGroupPage=yes
PrivilegesRequired=admin
; Der HKCU-Eintrag dient nur dem Aufräumen des Autostarts (siehe [Registry]).
UsedUserAreasWarning=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=SlicerSync-Setup-{#AppVersion}
SetupIconFile=..\assets\SlicerSync.ico
UninstallDisplayIcon={app}\SlicerSync.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
LicenseFile=..\LICENSE

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "Desktop-Verknüpfung erstellen"; GroupDescription: "Zusätzliche Verknüpfungen:"; Flags: unchecked

[Files]
Source: "..\publish\SlicerSync.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion isreadme
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\SlicerSync"; Filename: "{app}\SlicerSync.exe"
Name: "{autodesktop}\SlicerSync"; Filename: "{app}\SlicerSync.exe"; Tasks: desktopicon

[Registry]
; Autostart-Eintrag legt die App selbst an (Einstellung „Mit Windows starten“); beim Deinstallieren entfernen.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "SlicerSync"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\SlicerSync.exe"; Description: "SlicerSync jetzt starten"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM SlicerSync.exe /F"; Flags: runhidden; RunOnceId: "StopSlicerSync"

[Code]
var
  DownloadPage: TDownloadWizardPage;
  GitNeeded: Boolean;
  GitDownloaded: Boolean;

function GitInstalled: Boolean;
var
  InstallPath: String;
begin
  Result := False;
  if RegQueryStringValue(HKLM64, 'SOFTWARE\GitForWindows', 'InstallPath', InstallPath) then
    Result := FileExists(AddBackslash(InstallPath) + 'cmd\git.exe');
  if not Result and RegQueryStringValue(HKCU, 'SOFTWARE\GitForWindows', 'InstallPath', InstallPath) then
    Result := FileExists(AddBackslash(InstallPath) + 'cmd\git.exe');
  if not Result then
    Result := FileExists(ExpandConstant('{commonpf64}\Git\cmd\git.exe'));
  if not Result then
    Result := FileExists(ExpandConstant('{localappdata}\Programs\Git\cmd\git.exe'));
end;

procedure InitializeWizard;
begin
  GitNeeded := not GitInstalled;
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), 'Git for Windows wird heruntergeladen …', nil);
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo + NewLine + NewLine;
  if MemoTasksInfo <> '' then
    Result := Result + MemoTasksInfo + NewLine + NewLine;
  if GitNeeded then
    Result := Result + 'Git for Windows {#GitVersion} (inkl. Git Credential Manager) wird heruntergeladen und installiert,' + NewLine +
              Space + 'weil es auf diesem PC noch nicht vorhanden ist.'
  else
    Result := Result + 'Git for Windows ist bereits installiert.';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and GitNeeded then
  begin
    DownloadPage.Clear;
    DownloadPage.Add('{#GitUrl}', 'Git-Setup.exe', '{#GitSha256}');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
        GitDownloaded := True;
      except
        if DownloadPage.AbortedByUser then
          Result := False
        else
          Result := MsgBox('Git for Windows konnte nicht heruntergeladen werden:' + #13#10 + GetExceptionMessage + #13#10#13#10 +
                           'SlicerSync trotzdem installieren? Git muss dann später manuell installiert werden ' +
                           '(https://git-scm.com/download/win).', mbError, MB_YESNO) = IDYES;
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if (CurStep = ssInstall) and GitDownloaded then
  begin
    WizardForm.StatusLabel.Caption := 'Git for Windows wird installiert … (kann eine Minute dauern)';
    WizardForm.ProgressGauge.Style := npbstMarquee;
    try
      if not Exec(ExpandConstant('{tmp}\Git-Setup.exe'),
                  '/VERYSILENT /NORESTART /NOCANCEL /SP- /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS',
                  '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
        MsgBox('Die Installation von Git for Windows ist fehlgeschlagen (Code ' + IntToStr(ResultCode) + ').' + #13#10 +
               'Bitte Git manuell installieren: https://git-scm.com/download/win', mbError, MB_OK);
    finally
      WizardForm.ProgressGauge.Style := npbstNormal;
    end;
  end;
end;

function InitializeUninstall: Boolean;
begin
  Result := MsgBox('Hinweis: Die Deinstallation entfernt nur das Programm.' + #13#10#13#10 +
                   'Die Profilordner bleiben per Junction mit dem Repository-Ordner verbunden, die Slicer funktionieren weiter. ' +
                   'Wer die Profile wieder an ihren Originalort legen möchte, sollte vorher im Tray-Menü „Rückbau…“ ausführen.' + #13#10#13#10 +
                   'Jetzt deinstallieren?', mbConfirmation, MB_YESNO) = IDYES;
end;
