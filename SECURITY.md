# Security policy

**English** | [Deutsch](#sicherheitsrichtlinie)

## Reporting a vulnerability

Please **do not** report security problems in a public issue. Use GitHub's private reporting instead:
**Security → Report a vulnerability** on this repository. You will get an answer as soon as possible.

Relevant are for example: ways to make SlicerSync delete or overwrite user files, leak profiles or credentials, or run
foreign code (e.g. through a manipulated repository or `slicersync.json`).

## What SlicerSync stores

SlicerSync stores no passwords or tokens. Sign-in to GitHub/GitLab is handled by the Git Credential Manager, which
keeps it in the Windows Credential Manager. Profile files can contain secrets themselves (e.g. PrusaLink/OctoPrint
API keys in `physical_printer`) – keep your profile repository **private**.

---

# Sicherheitsrichtlinie

## Sicherheitslücken melden

Sicherheitsprobleme bitte **nicht** in einem öffentlichen Issue melden, sondern über GitHubs private Meldung:
**Security → Report a vulnerability** in diesem Repository. Du bekommst so schnell wie möglich eine Antwort.

Relevant ist zum Beispiel alles, womit SlicerSync dazu gebracht werden kann, Dateien zu löschen oder zu überschreiben,
Profile oder Zugangsdaten preiszugeben oder fremden Code auszuführen (z. B. über ein manipuliertes Repository oder
eine manipulierte `slicersync.json`).

## Was SlicerSync speichert

SlicerSync speichert keine Passwörter oder Tokens. Die Anmeldung bei GitHub/GitLab übernimmt der Git Credential
Manager, der sie in der Windows-Anmeldeinformationsverwaltung ablegt. Profildateien können selbst Geheimnisse enthalten
(z. B. PrusaLink/OctoPrint-API-Schlüssel in `physical_printer`) – das Profil-Repository daher **privat** halten.
