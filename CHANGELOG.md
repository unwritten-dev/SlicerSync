# Changelog

## 1.0.3

- New: **Use account profiles without sign-in** (Settings → Profiles). Orca Slicer and Bambu Studio keep separate
  profiles per signed-in account. This copies the profiles of an account into the folder the slicer uses without
  sign-in ("default"), with account and cloud IDs removed. Replaced versions are backed up as ZIP first; the
  account folder stays untouched. The copy reaches your other PCs with the next sync.
- PrusaSlicer 3 (alpha) is supported. Only your own presets (`%AppData%\PrusaSlicer3-dev\presets\user`) are
  backed up, not the downloaded vendor data, app settings or log file.
- Folders that lie inside each other can no longer be set up together (this would create a link inside a link).
- A folder you stop backing up is removed from `slicersync.json`, so a newly set up PC does not link it again.
- Autostart: the entry is renewed on start if it is missing or points to an old location (e.g. after moving the
  portable version). The settings show a warning if Windows has switched the autostart off. The log notes
  whether SlicerSync was started by Windows.

## 1.0.2

- Fixed: the settings window crashed with "Invoke or BeginInvoke cannot be called on a control until the
  window handle has been created" as soon as profile folders were set up. The setup wizard had the same
  problem in rare cases.
- New UI tests open every window with real profile folders, so errors like this are caught before a release.

## 1.0.1

- Slicers installed outside the default folder (e.g. on another drive) are now detected via the Windows
  list of installed apps.
- The setup wizard lists all supported slicers with their status, including those that were not found,
  and shows where profiles were expected.
- PrusaSlicer alpha and beta builds are supported. Their profiles live in separate folders
  (`PrusaSlicer-alpha`, `PrusaSlicer-beta`) and are backed up separately from the stable version.
- Custom folders: optionally pick the program's EXE, so SlicerSync checks whether it is running before
  moving or restoring files.

## 1.0.0

First release.

- Backs up profiles of Orca Slicer, Bambu Studio, PrusaSlicer and SuperSlicer, plus any custom folder, to a
  private Git repository using directory junctions.
- Setup wizard for a new setup or a second PC; detects existing junctions.
- Creates the remote repository on GitHub (private, via API) or GitLab (on first push) directly from the app.
- Sync after file changes, at an interval, daily, or manually; "Sync now" in the tray menu.
- History with restore of single files.
- ZIP backup before every move, replace or restore; restore (undo setup) function.
- Installer downloads and installs Git for Windows (with Git Credential Manager) if it is missing.
- User interface in English and German.
