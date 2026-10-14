# SlicerSync

[English](README.md) | **Deutsch**

SlicerSync ist eine kleine Windows-App für den Infobereich der Taskleiste. Sie sichert deine selbst erstellten
Slicer-Profile automatisch in ein privates Git-Repository (GitHub, GitLab, …), mit vollständigem Versionsverlauf.
Einmal eingerichtet, wird jedes Profil, das du im Slicer speicherst, gesichert und mit deinen anderen PCs abgeglichen.
Eine Kommandozeile braucht es dafür nicht.

**Direkt unterstützt:** Orca Slicer, Bambu Studio, PrusaSlicer (auch Alpha-/Beta-Versionen), SuperSlicer.
**Jeder andere Slicer** (Cura, Creality Print, ElegooSlicer, …) lässt sich als eigener Ordner hinzufügen.

## Download

Lade **`SlicerSync-Setup-<Version>.exe`** aus dem [neuesten Release](../../releases/latest) herunter und führe es aus.

- Ist **Git for Windows** noch nicht installiert, lädt das Setup es herunter (die SHA-256-Prüfsumme wird geprüft)
  und installiert es mit. Der **Git Credential Manager** ist dabei enthalten und übernimmt die Anmeldung bei
  GitHub/GitLab.
- Die .NET-Laufzeit steckt in der App, es muss nichts weiter installiert werden.
- Im Release liegt außerdem eine portable `SlicerSync-<Version>-portable.exe`. Sie setzt ein bereits installiertes
  Git for Windows voraus.

Voraussetzung: Windows 10 oder 11 (64 Bit).

## So funktioniert es

1. Die Profilordner werden in einen Repository-Ordner verschoben (Standard: `%UserProfile%\SlicerProfiles`).
2. Am ursprünglichen Ort entsteht eine Verzeichnis-Junction (`mklink /J`), die auf den neuen Ort zeigt. Die Slicer
   arbeiten unverändert weiter, Git sieht die echten Dateien.
3. Ein Sync führt `git add -A` aus und committet nur, wenn sich etwas geändert hat. Danach folgen
   `git pull --rebase` und `git push`.

| Slicer | Originalort | Im Repository |
|---|---|---|
| Orca Slicer | `%AppData%\OrcaSlicer\user\<default oder Nutzer-ID>\{filament, machine, process}` | `Orca\…` |
| Bambu Studio | `%AppData%\BambuStudio\user\<Nutzer-ID>\{filament, machine, process}` | `Bambu\…` |
| PrusaSlicer | `%AppData%\PrusaSlicer\{print, filament, printer, physical_printer}` | `Prusa\…` |
| PrusaSlicer 2.x Alpha/Beta | `%AppData%\PrusaSlicer-alpha\…` / `%AppData%\PrusaSlicer-beta\…` (gleiche Unterordner) | `PrusaAlpha\…` / `PrusaBeta\…` |
| PrusaSlicer 3 (Alpha) | `%AppData%\PrusaSlicer3-dev\presets\user` | `Prusa3\presets\user` |
| SuperSlicer | `%AppData%\SuperSlicer\{print, filament, printer, physical_printer}` | `SuperSlicer\…` |
| Eigener Ordner | ein beliebiger Ordner deiner Wahl | `Custom\<Name>` |

Gesichert werden nur diese Unterordner, nie der ganze Programmordner. Cache und rechnerspezifische Einstellungen
(z. B. `PrusaSlicer.ini`) bleiben außen vor.

## Erste Einrichtung

Beim ersten Start öffnet sich der Einrichtungsassistent. Er startet auf Englisch, Deutsch lässt sich auf der ersten
Seite auswählen.

1. **Willkommen:** prüft, ob Git vorhanden ist.
2. **Einrichtungsart:** *Neu einrichten* (dieser PC hat die Profile) oder *Zweiter PC* (siehe unten).
3. **Profilordner:** gefundene Ordner auswählen oder mit **Eigenen Ordner hinzufügen…** einen beliebigen weiteren
   Ordner aufnehmen.
4. **Repository:** lokaler Ordner, Remote-URL und Branch.
   - **Auf GitHub anlegen…** legt direkt aus der App ein neues privates Repository an. Falls nötig, öffnet der Git
     Credential Manager zuerst eine Anmeldung im Browser.
   - **Auf GitLab anlegen…** verwendet ein neues GitLab-Projekt. GitLab legt es beim ersten Hochladen als privates
     Projekt an.
   - Oder du legst selbst ein **leeres, privates** Repository an und fügst dessen HTTPS-URL ein.
5. **Zusammenfassung → Einrichten:** Die Slicer müssen geschlossen sein. SlicerSync legt eine ZIP-Sicherung an,
   verschiebt und verlinkt die Ordner, erstellt den ersten Commit und lädt ihn hoch.

> ⚠ **`physical_printer`** (PrusaSlicer/SuperSlicer) kann API-Schlüssel und Passwörter für PrusaLink/OctoPrint
> enthalten. Das Repository **muss privat** sein.

SlicerSync speichert selbst keine Zugangsdaten. Die Anmeldung übernimmt allein der Git Credential Manager, der sie in
der Windows-Anmeldeinformationsverwaltung ablegt.

Bereits eingerichtete Ordner (vorhandene Junctions) werden erkannt und übersprungen. Den Assistenten kannst du daher
gefahrlos erneut ausführen, z. B. nachdem ein Slicer-Update einen Ordner neu angelegt hat.

### Zweiter PC

Im Assistenten **„Zweiter PC – bestehendes Repository klonen“** wählen und dieselbe Remote-URL angeben. SlicerSync
erledigt dann:

1. Das Repository wird geklont.
2. Vorhandene lokale Profilordner werden als ZIP gesichert.
3. Die lokalen Ordner werden durch Junctions auf das Repository ersetzt. Der Stand im Repository gilt.

Profile, die es nur auf diesem PC gab, findest du danach in der ZIP-Sicherung (Tray-Menü → **Sicherungen öffnen**).
Eigene Ordner landen auf dem zweiten PC am selben Ort (relativ zu `%AppData%`, `%LocalAppData%` bzw. dem
Benutzerordner).

Orca Slicer und Bambu Studio legen Profile pro angemeldetem Benutzer ab (`default` oder deine Nutzer-ID). Damit
beide PCs dieselben Profile sehen, auf beiden mit demselben Konto anmelden (oder auf beiden gar nicht, dann gilt
`default`).

Du willst ohne Anmeldung arbeiten, deine Profile liegen aber in einem Konto-Ordner? Dann **Einstellungen → Profile →
Konto-Profile ohne Anmeldung nutzen…** verwenden. Das kopiert die Profile eines Kontos nach `default` (ohne Konto- und
Cloud-IDs, ersetzte Versionen vorher als ZIP gesichert, der Konto-Ordner bleibt unverändert). Danach im Slicer auf
jedem PC abmelden.

## Wann wird gesichert?

Einstellbar unter **Einstellungen → Synchronisation**:

- **Kurz nach einer Dateiänderung** (Standard): SlicerSync überwacht den Repository-Ordner. Nach der letzten
  Änderung wartet es 30 Sekunden (einstellbar) und synchronisiert dann einmal.
- **Intervall:** alle *n* Minuten.
- **Täglich** zu einer festen Uhrzeit.
- **Nur manuell.**

Außerdem gibt es in jedem Modus einen Sync beim Start (abschaltbar), damit Änderungen der anderen PCs geholt
werden, und den Eintrag **„Jetzt synchronisieren“** im Tray-Menü. Es wird nur committet, wenn sich tatsächlich
Dateien geändert haben.

## Tray-Icon und Menü

Der Punkt am Icon zeigt den Zustand: **grün** = OK, **gelb** = Sync läuft, **rot** = Fehler oder Konflikt,
**grau** = nicht eingerichtet. Ein Rechtsklick öffnet das Menü: Status, Jetzt synchronisieren, Verlauf,
Einstellungen, Protokoll, Repository- und Sicherungsordner, Einrichtungsassistent, Rückbau, Beenden.

Ein Doppelklick auf das Icon öffnet den **Verlauf**: die letzten Commits mit Datum und geänderten Dateien. Eine Datei
auswählen und **„Datei auf diesen Stand zurücksetzen…“** klicken. Die aktuelle Version wird vorher als ZIP gesichert.
Den Slicer danach neu starten.

## Wenn ein Konflikt gemeldet wird

Ein Konflikt entsteht, wenn zwei PCs dieselbe Profildatei unterschiedlich geändert haben. SlicerSync bricht den
Rebase dann ab und überschreibt nichts. Der lokale Commit bleibt erhalten, das Icon wird rot.

Lösung (einmalig von Hand, im Repository-Ordner, z. B. mit Git Bash):

```bash
cd ~/SlicerProfiles
git pull --rebase origin main     # zeigt die betroffenen Dateien
# Dateien bearbeiten: gewünschte Version behalten, Konfliktmarker entfernen
git add -A
git rebase --continue
git push
```

Alternativ: `git rebase --abort`, die Datei auf diesem PC über den Verlauf auf den Remote-Stand zurücksetzen und
erneut synchronisieren.

## Rückbau

Tray-Menü → **„Rückbau…“** (Slicer vorher schließen). Alle Junctions werden entfernt und die Ordner an ihren
Originalort zurückkopiert. Das Repository samt Verlauf bleibt erhalten und kann danach von Hand gelöscht werden.
Autostart wird dabei abgeschaltet.

Die Ordner werden **kopiert**, nicht zurückverschoben. Würden die Dateien aus dem Repository-Ordner verschwinden,
könnte ein späterer Sync das als Löschung zu den anderen PCs übertragen.

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
- Ordner werden erst kopiert und geprüft. Dann wird das Original zur Seite gelegt und die Junction angelegt, erst
  danach wird das Original gelöscht. Schlägt die Junction fehl, wird alles zurückgerollt.
- Solange ein bekannter Slicer läuft, wird nichts verschoben. Bei eigenen Ordnern klappt das, wenn beim Hinzufügen
  die EXE des Programms gewählt wurde; sonst das Programm bitte selbst schließen.
- Bei einem Pull-Konflikt wird der Rebase abgebrochen und nichts überschrieben.
- Zugangsdaten verwaltet ausschließlich der Git Credential Manager.

## Deinstallation

Über *Einstellungen → Apps*. Das entfernt nur das Programm: Die Junctions bleiben bestehen, die Slicer funktionieren
weiter. Wer die Ordner zurück an den Originalort legen möchte, führt **vorher** den Rückbau aus.

---

## Entwicklung

Die Anleitung für Entwickler (Bauen, Releases, Projektstruktur, weitere Slicer hinzufügen, Übersetzungen) steht in
der [englischen README](README.md#development).

Kurzfassung: .NET 8 SDK, Git for Windows und Inno Setup 6 installieren
(`winget install Microsoft.DotNet.SDK.8 Git.Git JRSoftware.InnoSetup`), dann `.\build.ps1` ausführen. Ergebnis sind
`publish\SlicerSync.exe` und `dist\SlicerSync-Setup-<Version>.exe`.

## Lizenz

MIT, siehe [LICENSE](LICENSE).
