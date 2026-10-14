# Contributing to SlicerSync

Thanks for helping! Issues and pull requests are welcome in **English or German**.

- **Bug?** Open an issue with the *Bug report* form. Please attach the relevant part of the log
  (tray menu → *Open log*, file `%LocalAppData%\SlicerSync\slicersync.log`) and remove anything private.
- **Idea or another slicer?** Use the *Feature request* form.
- **Security problem?** Please do not open a public issue – see [SECURITY.md](SECURITY.md).

## Requirements

- Windows 10/11 (x64)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Git for Windows](https://git-scm.com/download/win) (also needed by the tests)
- [Inno Setup 6](https://jrsoftware.org/isdl.php) (only for the installer)

With winget: `winget install Microsoft.DotNet.SDK.8 Git.Git JRSoftware.InnoSetup`

## Build and test

```powershell
dotnet test SlicerSync.sln     # all tests (core logic + UI)
.\build.ps1                    # tests, single-file EXE, installer
.\build.ps1 -SkipInstaller     # only publish\SlicerSync.exe
.\build.ps1 -Version 1.2.0
```

Output: `publish\SlicerSync.exe` (self-contained single file, .NET runtime included) and
`dist\SlicerSync-Setup-<version>.exe` (installer). The build fetches the current Git for Windows version and its
SHA-256 from GitHub; offline, the defaults in `installer\SlicerSync.iss` are used.

The tests never touch real profile or settings folders: all paths are injected via `AppPaths`, the global Git
configuration is isolated (`GIT_CONFIG_GLOBAL`, `GIT_CONFIG_NOSYSTEM`), running processes and installed apps are
simulated, and the GitHub API is faked. They create junctions and Git repositories in the temp folder only.

## Project structure

```
src/SlicerSync.Core/          Logic without UI (covered by tests)
  Config/                     AppSettings, SettingsStore (JSON)
  Git/                        GitRunner (git.exe), GitRepository, GitLocator
  FileSystem/                 Junction, BackupService (ZIP), DirectoryCopy
  Slicers/                    SlicerCatalog, SlicerDetector, ProcessGuard, RepoManifest, InstalledApps
  Setup/                      SetupService (setup, second PC, add/remove folders, restore), ProfileSetCopier
  Sync/                       SyncService, SyncScheduler (+ Debouncer), BackupLocations, CommitMessage
  History/                    HistoryService (history, restore a file)
  Hosting/                    RemoteCreator, GitHubClient, GitCredential
  Loc.cs                      English/German texts
src/SlicerSync.App/           WinForms tray app (TrayContext, Forms/)
tests/SlicerSync.Core.Tests/  xUnit; simulates several PCs with temporary folders and a local bare repository
tests/SlicerSync.App.Tests/   Opens every window with real (temporary) profile folders
installer/SlicerSync.iss      Inno Setup script (downloads Git if needed)
tools/Screenshots/            Renders the README screenshots with demo data
tools/make-icon.ps1           Generates assets\SlicerSync.ico
```

## Adding another slicer

Add a `SlicerDefinition` to `src/SlicerSync.Core/Slicers/SlicerCatalog.cs`:

- `AppDataFolder` and `Layout`: `UserSets` (`%AppData%\<app>\user\<account>\<folder>`, like Orca Slicer) or `Flat`
  (`%AppData%\<app>\<folder>`, like PrusaSlicer),
- `Folders`: only the folders with the user's own profiles – never caches, vendor downloads or logs,
- `ExeCandidates`, `ProcessNames`, `UninstallNames` (name in Windows "Installed apps") and `SecretFolders`.

Detection, the running-slicer check and the second-PC mapping then work automatically. Please add a test in
`tests/SlicerSync.Core.Tests/SlicerTests.cs` with the real folder layout.

## Translations

Every visible text is written as an English/German pair right where it is used:

```csharp
Loc.T("Settings", "Einstellungen")
```

New texts need both languages. English is the default.

## Code style

- Keep the logic in `SlicerSync.Core` and cover it with tests; the app project only contains the UI.
- Never delete or overwrite user data without a verified ZIP backup first (`BackupService`).
- Comments and identifiers in English.

## Screenshots

After UI changes, regenerate the README screenshots (uses made-up demo data, nothing from your PC):

```powershell
dotnet run --project tools/Screenshots
```

## Releases

GitHub Actions runs all tests on every push (`.github/workflows/ci.yml`). To release:

1. Add a section `## <version>` at the top of `CHANGELOG.md`.
2. Push a tag `v<version>` (e.g. `v1.2.0`).

`.github/workflows/release.yml` then builds the installer and the portable EXE and publishes a GitHub release with
the matching changelog section as release notes.
