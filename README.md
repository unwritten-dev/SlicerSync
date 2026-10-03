# SlicerSync

SlicerSync ist eine kleine Windows-App für den Infobereich der Taskleiste. Sie sichert deine selbst erstellten Profile aus
**Orca Slicer** und **PrusaSlicer** automatisch in ein privates Git-Repository (GitHub, GitLab, …), mit
vollständigem Versionsverlauf. Einrichtung und Betrieb kommen ohne Kommandozeile aus.

## So funktioniert es

1. Die Profilordner werden in einen Repository-Ordner verschoben (Standard: `%UserProfile%\SlicerProfiles`).
2. Am ursprünglichen Ort entsteht eine Verzeichnis-Junction (`mklink /J`), die auf den neuen Ort zeigt. Die Slicer
   arbeiten unverändert weiter, Git sieht die echten Dateien.
3. Ein Sync führt `git add -A` aus und committet nur, wenn sich etwas geändert hat. Danach folgen `git pull --rebase` und `git push`.

Gesicherte Ordner (jeweils einzeln verlinkt):

| Slicer | Originalort | Im Repository |
|---|---|---|
| Orca Slicer | `%AppData%\OrcaSlicer\user\<default oder Nutzer-ID>\{filament, machine, process}` | `Orca\<…>\…` |
| PrusaSlicer | `%AppData%\PrusaSlicer\{print, filament, printer, physical_printer}` | `Prusa\…` |

Vom PrusaSlicer-Ordner werden nur diese Unterordner gesichert. Cache und rechnerspezifische Einstellungen
(z. B. `PrusaSlicer.ini`) bleiben außen vor.

## Installation

1. `SlicerSync-Setup-<Version>.exe` ausführen (Administratorrechte nötig).
2. Ist **Git for Windows** noch nicht installiert, lädt das Setup es herunter (die SHA-256-Prüfsumme wird geprüft) und
   installiert es still mit. Der **Git Credential Manager** ist dabei automatisch enthalten.
3. Am Ende startet SlicerSync. Die .NET-Laufzeit ist in der EXE enthalten, es muss nichts weiter installiert werden.

## Erste Einrichtung

Beim ersten Start öffnet sich der Einrichtungsassistent:

1. **Willkommen:** Prüft, ob Git vorhanden ist.
2. **Einrichtungsart:** Neu einrichten oder Zweiter PC (siehe unten).
3. **Profilordner:** Gefundene Ordner auswählen.
4. **Repository:** Ordner, Remote-URL und Branch angeben.
   - Lege vorher bei GitHub/GitLab ein **leeres, privates** Repository an und kopiere die HTTPS-URL,
     z. B. `https://github.com/name/slicer-profile.git`.
   - Beim ersten Zugriff öffnet sich das Anmeldefenster des Git Credential Manager. SlicerSync selbst speichert keine
     Zugangsdaten.
5. **Zusammenfassung → Einrichten:** Die Slicer müssen geschlossen sein. SlicerSync legt zuerst eine ZIP-Sicherung an,
   dann werden die Ordner verschoben und verlinkt. Zum Schluss folgen der erste Commit und der Push.

> ⚠ **`physical_printer`** kann API-Schlüssel und Passwörter für PrusaLink/OctoPrint enthalten.
> Das Repository muss daher **privat** sein.

Bereits eingerichtete Ordner (vorhandene Junctions) werden erkannt und übersprungen. Den Assistenten kannst du daher
gefahrlos erneut ausführen, z. B. nachdem ein Slicer-Update einen Ordner neu angelegt hat.

### Zweiter PC

Im Assistenten **„Zweiter PC – bestehendes Repository klonen“** wählen und dieselbe Remote-URL angeben. SlicerSync
erledigt dann:

1. Das Repository wird geklont.
2. Vorhandene lokale Profilordner werden als ZIP gesichert.
3. Die lokalen Ordner werden durch Junctions auf das Repository ersetzt. Der Stand im Repository gilt.

Lokale Profile, die es nur auf diesem PC gab, findest du danach in der ZIP-Sicherung (Tray-Menü → „Sicherungen öffnen“).

Hinweis zu Orca Slicer: Die Ordner werden über den Namen des Benutzer-Unterordners zugeordnet (`default` oder deine
Orca-Nutzer-ID). Damit beide PCs dieselben Profile sehen, sollte Orca auf beiden PCs gleich angemeldet sein
(beide nicht angemeldet = `default`).

## Wann wird gesichert?

Einstellbar unter **Einstellungen → Synchronisation**:

- **Kurz nach einer Dateiänderung** (Standard): SlicerSync überwacht den Repository-Ordner. Nach der letzten Änderung
  wartet es 30 Sekunden (einstellbar) und synchronisiert dann einmal.
- **Intervall:** alle *n* Minuten.
- **Täglich** zu einer festen Uhrzeit.
- **Nur manuell.**

Außerdem gibt es in jedem Modus:

- einen Sync beim Start (abschaltbar), damit Änderungen vom anderen PC geholt werden,
- den Eintrag **„Jetzt synchronisieren“** im Tray-Menü.

## Tray-Icon und Menü

Der Punkt am Icon zeigt den Zustand: **grün** = OK, **gelb** = Sync läuft, **rot** = Fehler oder Konflikt,
**grau** = nicht eingerichtet. Ein Rechtsklick öffnet das Menü mit Status, „Jetzt synchronisieren“, Verlauf,
Einstellungen, Protokoll, Repository- und Sicherungsordner, Einrichtungsassistent, Rückbau und Beenden.

Ein Doppelklick auf das Icon öffnet den **Verlauf**: die letzten Commits mit Datum und geänderten Dateien. Eine Datei
auswählen und **„Datei auf diesen Stand zurücksetzen…“** klicken. Die aktuelle Version wird vorher als ZIP gesichert.
Den Slicer danach neu starten.

## Wenn ein Konflikt gemeldet wird

Ein Konflikt entsteht, wenn zwei PCs dieselbe Profildatei unterschiedlich geändert haben. SlicerSync bricht den Rebase
dann ab und überschreibt nichts. Der lokale Commit bleibt erhalten, das Icon wird rot.

Lösung (einmalig von Hand, im Repository-Ordner, z. B. mit Git Bash):

```bash
cd ~/SlicerProfiles
git pull --rebase origin main     # zeigt die betroffenen Dateien
# Dateien bearbeiten: gewünschte Version behalten, Konfliktmarker entfernen
git add -A
git rebase --continue
git push
```

Alternativ: `git rebase --abort`, die Datei auf diesem PC über den Verlauf auf den Remote-Stand zurücksetzen und erneut
synchronisieren.

## Rückbau

Tray-Menü → **„Rückbau…“** (Slicer vorher schließen). Alle Junctions werden entfernt und die Ordner an ihren
Originalort zurückkopiert. Das Repository samt Verlauf bleibt erhalten und kann danach von Hand gelöscht werden.
Autostart wird dabei abgeschaltet.

Abweichend vom ursprünglichen Konzept („zurückverschieben“) wird **kopiert**. Würden die Dateien aus dem
Repository-Ordner entfernt, könnte ein späterer Sync das als Löschung zu den anderen PCs übertragen.

Einzelne Ordner lassen sich auch in **Einstellungen → Profile** abwählen (Rückbau nur für diesen Ordner) oder neu
aufnehmen.

## Speicherorte

| Was | Wo |
|---|---|
| Einstellungen | `%AppData%\SlicerSync\settings.json` |
| Protokoll | `%LocalAppData%\SlicerSync\slicersync.log` (rotiert bei 1 MB) |
| ZIP-Sicherungen | `%LocalAppData%\SlicerSync\backups\` |
| Profile + Git-Repository | Standard `%UserProfile%\SlicerProfiles` |
| Autostart | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\SlicerSync` |

Im Repository liegt außerdem `slicersync.json`: Diese Datei listet die gesicherten Ordner, damit ein zweiter PC
weiß, was zu verlinken ist. Dazu kommt `.gitattributes` (`* -text`), damit Dateien byte-genau gespeichert werden.

## Sicherheitsregeln

- Vor jedem Verschieben, Ersetzen oder Zurücksetzen wird eine ZIP-Sicherung angelegt und auf Vollständigkeit geprüft.
- Ordner werden erst kopiert und geprüft. Dann wird das Original zur Seite gelegt und die Junction angelegt.
  Erst danach wird das Original gelöscht. Schlägt die Junction fehl, wird alles zurückgerollt.
- Solange ein Slicer läuft, wird nichts verschoben.
- Bei einem Pull-Konflikt wird der Rebase abgebrochen und nichts überschrieben.
- Zugangsdaten verwaltet ausschließlich der Git Credential Manager (Windows-Anmeldeinformationsverwaltung).

## Deinstallation

Über „Apps & Features“. Das entfernt nur das Programm: Die Junctions bleiben bestehen und die Slicer funktionieren weiter.
Wer die Ordner zurück an den Originalort legen möchte, führt **vorher** den Rückbau aus.

---

## Entwicklung

### Voraussetzungen

- Windows 10/11 (x64)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Git for Windows](https://git-scm.com/download/win) (für die Tests)
- [Inno Setup 6](https://jrsoftware.org/isdl.php) (nur für den Installer)

Per winget: `winget install Microsoft.DotNet.SDK.8 Git.Git JRSoftware.InnoSetup`

### Bauen

```powershell
.\build.ps1                    # Tests, Einzeldatei-EXE, Installer
.\build.ps1 -SkipInstaller     # nur publish\SlicerSync.exe
.\build.ps1 -Version 1.1.0
```

Ergebnis:

- `publish\SlicerSync.exe`: eigenständige Einzeldatei (win-x64, .NET-Laufzeit enthalten)
- `dist\SlicerSync-Setup-<Version>.exe`: Installer

Der Build fragt die aktuelle Git-for-Windows-Version samt SHA-256 bei GitHub ab. Ohne Internet gelten die Werte aus
`installer\SlicerSync.iss`.

Einzelne Schritte von Hand:

```powershell
dotnet test
dotnet publish src\SlicerSync.App -c Release -o publish
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\SlicerSync.iss
```

Das App-Icon (`assets\SlicerSync.ico`) erzeugt `tools\make-icon.ps1`.

### Projektstruktur

```
src/SlicerSync.Core/        Logik ohne Oberfläche (komplett getestet)
  Config/                   AppSettings, SettingsStore (JSON)
  Git/                      GitRunner (git.exe), GitRepository, GitLocator
  FileSystem/               Junction, BackupService (ZIP), DirectoryCopy
  Slicers/                  SlicerDetector, ProcessGuard, RepoManifest
  Setup/                    SetupService: Neu, Zweiter PC, Ordner hinzufügen/entfernen, Rückbau
  Sync/                     SyncService, SyncScheduler (+ Debouncer), CommitMessage
  History/                  HistoryService (Verlauf, Datei zurücksetzen)
src/SlicerSync.App/         WinForms-Tray-App
  TrayContext.cs            Icon, Menü, Benachrichtigungen, Planung
  Forms/                    SetupWizardForm, SettingsForm, HistoryForm
tests/SlicerSync.Core.Tests xUnit; simuliert mehrere PCs mit temporären Ordnern,
                            lokalem Bare-Repository als Remote und isolierter Git-Konfiguration
installer/SlicerSync.iss    Inno-Setup-Skript (lädt Git bei Bedarf)
```

Die Tests greifen nie auf echte Profil- oder Einstellungsordner zu: Alle Pfade werden über `AppPaths` injiziert.
Die globale Git-Konfiguration wird per `GIT_CONFIG_GLOBAL`/`GIT_CONFIG_NOSYSTEM` isoliert.

### Platzhalter für die Commit-Nachricht

`{datum}` `{zeit}` `{rechner}` `{benutzer}` `{anzahl}`, Standard:
`Profil-Sync {datum} {zeit} von {rechner} ({anzahl} Dateien)`

## Lizenz

MIT, siehe [LICENSE](LICENSE).
