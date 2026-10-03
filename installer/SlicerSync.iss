; Inno Setup script for SlicerSync.
; Installs the app and, if needed, downloads Git for Windows (including the Git Credential Manager)
; and installs it silently. Called by build.ps1, which passes the current Git version via /D;
; the values below are only the fallback.

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
AppPublisherURL=https://github.com/unwritten-dev/SlicerSync
AppSupportURL=https://github.com/unwritten-dev/SlicerSync/issues
DefaultDirName={autopf}\SlicerSync
DefaultGroupName=SlicerSync
DisableProgramGroupPage=yes
PrivilegesRequired=admin
; The HKCU entry below only cleans up the autostart value on uninstall.
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
; English is the default; the user can pick German in the language dialog.
ShowLanguageDialog=yes
LanguageDetectionMethod=none

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[CustomMessages]
english.DesktopIcon=Create a desktop shortcut
english.AdditionalIcons=Additional shortcuts:
english.LaunchApp=Start SlicerSync now
english.DownloadingGit=Downloading Git for Windows …
english.GitWillBeInstalled=Git for Windows {#GitVersion} (including the Git Credential Manager) will be downloaded and installed,
english.GitWillBeInstalled2=because it is not installed on this PC yet.
english.GitAlreadyInstalled=Git for Windows is already installed.
english.GitDownloadFailed=Git for Windows could not be downloaded:
english.GitDownloadContinue=Install SlicerSync anyway? Git then has to be installed manually later (https://git-scm.com/download/win).
english.InstallingGit=Installing Git for Windows … (this can take a minute)
english.GitInstallFailed=Installing Git for Windows failed (code %1). Please install Git manually: https://git-scm.com/download/win
english.UninstallNote=Note: uninstalling removes the program only.%n%nThe profile folders stay linked to the repository folder via junctions, so the slicers keep working. If you want the profiles back at their original location, run "Restore (undo setup)…" from the tray menu first.%n%nUninstall now?
german.DesktopIcon=Desktop-Verknüpfung erstellen
german.AdditionalIcons=Zusätzliche Verknüpfungen:
german.LaunchApp=SlicerSync jetzt starten
german.DownloadingGit=Git for Windows wird heruntergeladen …
german.GitWillBeInstalled=Git for Windows {#GitVersion} (inkl. Git Credential Manager) wird heruntergeladen und installiert,
german.GitWillBeInstalled2=weil es auf diesem PC noch nicht vorhanden ist.
german.GitAlreadyInstalled=Git for Windows ist bereits installiert.
german.GitDownloadFailed=Git for Windows konnte nicht heruntergeladen werden:
german.GitDownloadContinue=SlicerSync trotzdem installieren? Git muss dann später manuell installiert werden (https://git-scm.com/download/win).
german.InstallingGit=Git for Windows wird installiert … (kann eine Minute dauern)
german.GitInstallFailed=Die Installation von Git for Windows ist fehlgeschlagen (Code %1). Bitte Git manuell installieren: https://git-scm.com/download/win
german.UninstallNote=Hinweis: Die Deinstallation entfernt nur das Programm.%n%nDie Profilordner bleiben per Junction mit dem Repository-Ordner verbunden, die Slicer funktionieren weiter. Wer die Profile wieder an ihren Originalort legen möchte, sollte vorher im Tray-Menü „Rückbau…“ ausführen.%n%nJetzt deinstallieren?

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\SlicerSync.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.de.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\SlicerSync"; Filename: "{app}\SlicerSync.exe"
Name: "{autodesktop}\SlicerSync"; Filename: "{app}\SlicerSync.exe"; Tasks: desktopicon

[Registry]
; The app itself creates the autostart value ("Start with Windows"); remove it on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "SlicerSync"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\SlicerSync.exe"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent runasoriginaluser

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
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), CustomMessage('DownloadingGit'), nil);
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo + NewLine + NewLine;
  if MemoTasksInfo <> '' then
    Result := Result + MemoTasksInfo + NewLine + NewLine;
  if GitNeeded then
    Result := Result + CustomMessage('GitWillBeInstalled') + NewLine + Space + CustomMessage('GitWillBeInstalled2')
  else
    Result := Result + CustomMessage('GitAlreadyInstalled');
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
          Result := MsgBox(CustomMessage('GitDownloadFailed') + #13#10 + GetExceptionMessage + #13#10#13#10 +
                           CustomMessage('GitDownloadContinue'), mbError, MB_YESNO) = IDYES;
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
    WizardForm.StatusLabel.Caption := CustomMessage('InstallingGit');
    WizardForm.ProgressGauge.Style := npbstMarquee;
    try
      if not Exec(ExpandConstant('{tmp}\Git-Setup.exe'),
                  '/VERYSILENT /NORESTART /NOCANCEL /SP- /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS',
                  '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
        MsgBox(FmtMessage(CustomMessage('GitInstallFailed'), [IntToStr(ResultCode)]), mbError, MB_OK);
    finally
      WizardForm.ProgressGauge.Style := npbstNormal;
    end;
  end;
end;

function InitializeUninstall: Boolean;
begin
  Result := MsgBox(CustomMessage('UninstallNote'), mbConfirmation, MB_YESNO) = IDYES;
end;
