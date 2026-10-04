# SlicerSync

**English** | [Deutsch](README.de.md)

SlicerSync is a small Windows tray app that automatically backs up your own slicer profiles to a private Git
repository (GitHub, GitLab, …), with full version history. Set it up once, and every profile you save in your
slicer is backed up and synced to your other PCs. No command line needed.

**Supported out of the box:** Orca Slicer, Bambu Studio, PrusaSlicer (including alpha/beta builds), SuperSlicer.
**Any other slicer** (Cura, Creality Print, ElegooSlicer, …) can be added as a custom folder.

## Download

Get **`SlicerSync-Setup-<version>.exe`** from the [latest release](../../releases/latest) and run it.

- If **Git for Windows** is not installed yet, the setup downloads and installs it for you (the SHA-256 checksum is
  verified). It includes the **Git Credential Manager**, which handles sign-in to GitHub/GitLab.
- The .NET runtime is built into the app, so nothing else needs to be installed.
- The release also contains a portable `SlicerSync-<version>-portable.exe`. It needs Git for Windows to be
  installed already.

Requirements: Windows 10 or 11 (64-bit).

## How it works

1. Your profile folders are moved into a repository folder (default: `%UserProfile%\SlicerProfiles`).
2. At the original location a directory junction (`mklink /J`) points to the new location. The slicers keep working
   as before; Git sees the real files.
3. A sync runs `git add -A`, commits only if something changed, then `git pull --rebase` and `git push`.

| Slicer | Original location | In the repository |
|---|---|---|
| Orca Slicer | `%AppData%\OrcaSlicer\user\<default or user ID>\{filament, machine, process}` | `Orca\…` |
| Bambu Studio | `%AppData%\BambuStudio\user\<user ID>\{filament, machine, process}` | `Bambu\…` |
| PrusaSlicer | `%AppData%\PrusaSlicer\{print, filament, printer, physical_printer}` | `Prusa\…` |
| PrusaSlicer alpha/beta | `%AppData%\PrusaSlicer-alpha\…` / `%AppData%\PrusaSlicer-beta\…` (same subfolders) | `PrusaAlpha\…` / `PrusaBeta\…` |
| SuperSlicer | `%AppData%\SuperSlicer\{print, filament, printer, physical_printer}` | `SuperSlicer\…` |
| Custom folder | any folder you choose | `Custom\<name>` |

Only these subfolders are backed up, never the whole application folder. Caches and machine-specific settings
(e.g. `PrusaSlicer.ini`) are left alone.

## First setup

On first start the setup wizard opens (English by default, German can be selected on the first page):

1. **Welcome:** checks that Git is available.
2. **Setup type:** *New setup* (this PC has the profiles) or *Second PC* (see below).
3. **Profile folders:** choose the detected folders, or add any other folder with **Add custom folder…**.
4. **Repository:** local folder, remote URL and branch.
   - Click **Create on GitHub…** to create a new private repository directly from the app. If needed, the Git
     Credential Manager opens a browser sign-in first.
   - Click **Create on GitLab…** to use a new GitLab project. GitLab creates it as a private project on the first
     upload.
   - Or create an **empty, private** repository yourself and paste its HTTPS URL.
5. **Summary → Set up:** the slicers must be closed. SlicerSync creates a ZIP backup, moves and links the folders,
   makes the first commit and uploads it.

> ⚠ **`physical_printer`** (PrusaSlicer/SuperSlicer) can contain API keys and passwords for PrusaLink/OctoPrint.
> Your repository **must be private**.

SlicerSync never stores credentials itself. Sign-in is handled entirely by the Git Credential Manager, which keeps it
in the Windows Credential Manager.

Folders that are already set up (existing junctions) are detected and skipped. You can safely run the wizard again,
e.g. after a slicer update recreated a folder.

### Second PC

In the wizard choose **Second PC – clone an existing repository** and enter the same remote URL. SlicerSync then:

1. clones the repository,
2. backs up the existing local profile folders as ZIP,
3. replaces them with junctions to the repository. The repository state wins.

Profiles that only existed on this PC can be found in the ZIP backup afterwards (tray menu → **Open backups**).
Custom folders are mapped to the same place on the second PC (relative to `%AppData%`, `%LocalAppData%` or your
user folder).

Orca Slicer and Bambu Studio store profiles per signed-in user (`default` or your user ID). To share profiles between
PCs, sign in with the same account on both PCs (or with none, which means `default`).

## When does SlicerSync back up?

Set under **Settings → Sync**:

- **Shortly after a file change** (default): SlicerSync watches the repository folder and syncs once 30 seconds
  (configurable) after the last change.
- **At a fixed interval:** every *n* minutes.
- **Daily** at a fixed time.
- **Manually only.**

In every mode you also get a sync on startup (can be turned off) to fetch changes from your other PCs, and
**Sync now** in the tray menu. A commit is only made when files actually changed.

## Tray icon and menu

The dot on the icon shows the state: **green** = OK, **yellow** = syncing, **red** = error or conflict,
**grey** = not set up. Right-click for the menu: status, Sync now, History, Settings, log, repository and backup
folders, setup wizard, Restore, Exit.

Double-click the icon to open the **History**: recent commits with date and changed files. Select a file and click
**Restore file to this state…**. The current version is backed up as ZIP first. Restart the slicer afterwards.

## If a conflict is reported

A conflict happens when two PCs changed the same profile file differently. SlicerSync then aborts the rebase and
overwrites nothing. Your local commit is kept and the icon turns red.

To resolve it once by hand in the repository folder (e.g. with Git Bash):

```bash
cd ~/SlicerProfiles
git pull --rebase origin main     # shows the affected files
# edit the files: keep the version you want, remove the conflict markers
git add -A
git rebase --continue
git push
```

Alternatively run `git rebase --abort`, restore the file on this PC to the remote state via the History, and sync
again.

## Restore (undo setup)

Tray menu → **Restore (undo setup)…** (close the slicers first). All junctions are removed and the folders are
copied back to their original location. The repository with its history is kept and can be deleted by hand
afterwards. Autostart is turned off.

The folders are **copied**, not moved back. If the files disappeared from the repository folder, a later sync could
send that as a deletion to your other PCs.

Single folders can also be unchecked (restored) or added in **Settings → Profiles**.

## Where things are stored

| What | Where |
|---|---|
| Settings | `%AppData%\SlicerSync\settings.json` |
| Log | `%LocalAppData%\SlicerSync\slicersync.log` (rotated at 1 MB) |
| ZIP backups | `%LocalAppData%\SlicerSync\backups\` |
| Profiles + Git repository | default `%UserProfile%\SlicerProfiles` |
| Autostart | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\SlicerSync` |

The repository also contains `slicersync.json`. It lists the backed-up folders, so a second PC knows what to link.
There is also a `.gitattributes` file (`* -text`) so files are stored byte for byte.

## Safety rules

- Before anything is moved, replaced or restored, a ZIP backup is created and checked for completeness.
- Folders are copied and verified first. Then the original is moved aside and the junction is created, and only
  then is the original deleted. If the junction fails, everything is rolled back.
- Nothing is moved while a known slicer is running. For custom folders this works if you picked the program's
  EXE when adding the folder; otherwise close the program yourself.
- On a pull conflict the rebase is aborted and nothing is overwritten.
- Credentials are handled only by the Git Credential Manager.

## Uninstall

Via *Settings → Apps*. This removes the program only: the junctions stay and the slicers keep working. If you want
the folders back at their original location, run **Restore** **first**.

---

## Development

### Requirements

- Windows 10/11 (x64)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Git for Windows](https://git-scm.com/download/win) (also needed by the tests)
- [Inno Setup 6](https://jrsoftware.org/isdl.php) (only for the installer)

With winget: `winget install Microsoft.DotNet.SDK.8 Git.Git JRSoftware.InnoSetup`

### Build

```powershell
.\build.ps1                    # tests, single-file EXE, installer
.\build.ps1 -SkipInstaller     # only publish\SlicerSync.exe
.\build.ps1 -Version 1.1.0
```

Output:

- `publish\SlicerSync.exe`: self-contained single file (win-x64, .NET runtime included)
- `dist\SlicerSync-Setup-<version>.exe`: installer

The build queries the current Git for Windows version including its SHA-256 from GitHub. Without internet access
the values in `installer\SlicerSync.iss` are used.

Single steps by hand:

```powershell
dotnet test
dotnet publish src\SlicerSync.App -c Release -o publish
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\SlicerSync.iss
```

The app icon (`assets\SlicerSync.ico`) is generated by `tools\make-icon.ps1`.

### Releases

GitHub Actions runs the tests on every push (`.github/workflows/ci.yml`). Pushing a tag like `v1.1.0` builds the
installer and publishes a GitHub release (`.github/workflows/release.yml`). Update `CHANGELOG.md` before tagging.

### Project structure

```
src/SlicerSync.Core/        Logic without UI (covered by tests)
  Config/                   AppSettings, SettingsStore (JSON)
  Git/                      GitRunner (git.exe), GitRepository, GitLocator
  FileSystem/               Junction, BackupService (ZIP), DirectoryCopy
  Slicers/                  SlicerCatalog, SlicerDetector, ProcessGuard, RepoManifest
  Setup/                    SetupService: new setup, second PC, add/remove folders, restore
  Sync/                     SyncService, SyncScheduler (+ Debouncer), CommitMessage
  History/                  HistoryService (history, restore a file)
  Hosting/                  RemoteCreator, GitHubClient, GitCredential
  Loc.cs                    English/German texts
src/SlicerSync.App/         WinForms tray app
  TrayContext.cs            icon, menu, notifications, scheduling
  Forms/                    SetupWizardForm, SettingsForm, HistoryForm, CreateRemoteForm, CustomFolderForm
tests/SlicerSync.Core.Tests xUnit; simulates several PCs with temporary folders, a local bare
                            repository as remote and an isolated Git configuration
installer/SlicerSync.iss    Inno Setup script (downloads Git if needed)
```

The tests never touch real profile or settings folders: all paths are injected via `AppPaths`. The global Git
configuration is isolated with `GIT_CONFIG_GLOBAL`/`GIT_CONFIG_NOSYSTEM`, and the GitHub API is simulated.

### Adding another slicer

Add a `SlicerDefinition` to `src/SlicerSync.Core/Slicers/SlicerCatalog.cs`: AppData folder, layout (`UserSets`
like Orca or `Flat` like PrusaSlicer), profile subfolders, executable path and process names. Detection, the
running-slicer check and the second-PC mapping then work automatically.

### Translations

Every visible text is written as an English/German pair next to where it is used: `Loc.T("English", "Deutsch")`.

### Commit message placeholders

`{date}` `{time}` `{computer}` `{user}` `{count}` (German names like `{datum}` work too). Default:
`Profile sync {date} {time} from {computer} ({count} files)`

## License

MIT, see [LICENSE](LICENSE).
