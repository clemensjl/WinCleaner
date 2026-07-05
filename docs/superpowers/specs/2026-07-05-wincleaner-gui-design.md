# WinCleaner GUI — Design-Spec

> Stand: 2026-07-05 · Ziel-Version: 2.0.0 · Status: freigegeben

## Ziel

Ein echtes grafisches Windows-Programm (Fenster, kein Terminal) als **zusätzliches**
Frontend für WinCleaner. Die bestehende CLI und das TUI-Menü bleiben unverändert.
Alle 28 Befehle sind über die GUI erreichbar; die häufigen Aufgaben poliert, die
selteneren als schlanke Formularseiten. Sicherheitsmodell wie im CLI:
**Vorschau zuerst**, Löschen in den Papierkorb, umkehrbare Tweaks,
Wiederherstellungspunkt vor systemweiten Eingriffen.

## Architektur

- Neues Projekt **`WinCleaner.Gui`** (WPF, `net8.0-windows`, `OutputType=WinExe`
  → kein Konsolenfenster), MVVM.
- Referenziert das bestehende **`WinCleaner`**-Projekt (wie `WinCleaner.Tests`)
  und ruft die Core-/SystemTools-Klassen **direkt** auf — strukturierte Daten,
  kein Text-Parsing, kein zweiter Prozess für Lese-/Nutzer-Aktionen.
- **Admin-Aktionen** (Wiederherstellungspunkt, systemweite Privacy-Tweaks
  HKLM, Dienste, hosts-Datei) rufen gezielt die installierte
  `WinCleaner.exe <befehl>` per UAC (`ShellExecute runas`) auf und nutzen so
  die bereits gehärtete, getestete Elevation-/Restore-Logik. Die GUI selbst
  läuft nicht dauerhaft als Admin.
- Lange Scans laufen in `Task.Run` (Hintergrund) → UI friert nie ein;
  Fortschritt + Abbrechen via `CancellationToken` wo sinnvoll.

### Core-Anpassung
`Logger` bekommt einen optionalen Sink (`Action<string,string>`), damit die GUI
Meldungen in ihre Statusleiste leitet statt auf `Console.Error`. Default-Verhalten
(stderr) bleibt unverändert → abwärtskompatibel, bestehende Tests unberührt.

## Fensteraufbau

Linke Navigationsleiste (Kategorien) · Inhaltsbereich (die jeweilige Seite) ·
Statusleiste unten (letzte Meldung + Fortschrittsbalken).

Seiten (decken alle 28 Befehle ab):

| Seite | Befehle |
|-------|---------|
| Übersicht (Dashboard) | Kurzstatus aus scan-junk / analyze-disk / scan-privacy / startup-list |
| Aufräumen | scan-junk, clean-junk, browser-clean, scan-extras |
| Speicher | analyze-disk (+ --by-type, Filter, --snapshot), find-duplicates, disk-diff |
| Programme | list-programs, uninstall, debloat, list-updates, update, install, schedule-update |
| Autostart & Dienste | startup-list, startup-disable, services |
| Privatsphäre | scan-privacy, privacy (--apply/--undo), block-telemetry, schedule-privacy |
| Sicher löschen | shred, wipe-free-space (rot markiert, Extra-Bestätigung) |
| System | create-restore-point, schedule-clean, unschedule-clean |

Reiche Ansichten (Liste + Häkchen + Fortschritt): Aufräumen, Speicher, Duplikate,
Autostart, Privatsphäre, Programme. Schlanke Formularseiten: shred, wipe-free-space,
block-telemetry, schedule-*, disk-diff.

## Vorschau-zuerst-Ablauf

Jede verändernde Aktion:
1. **[Scannen/Vorschau]** → Liste mit Häkchen zeigt, was betroffen wäre.
2. **[Ausführen]** → Bestätigungsdialog mit Zusammenfassung (Anzahl, Größe).
3. Aktion: Löschen → Papierkorb; Tweak → umkehrbar (Undo-Knopf); systemweit →
   vorher Wiederherstellungspunkt.
4. `shred` / `wipe-free-space`: rote Kennzeichnung, „unwiderruflich"-Extra-Bestätigung.

## Technik

- WPF, MVVM (leichtgewichtig, ohne schweres Framework): `ViewModelBase` mit
  `INotifyPropertyChanged`, `RelayCommand`.
- Design-System als `ResourceDictionary`: Dunkel-/Hell-Variante (folgt
  Windows-App-Theme), Akzentfarbe, Abstände, Typografie (Segoe UI Variable),
  Karten-/Listen-/Button-Styles. Optik-Feinschliff mit frontend-design-Skill.
- `GuiLogger` (Logger mit Sink) → Statusleiste.
- `BackgroundRunner`-Hilfe: führt synchrone Core-Aufrufe in `Task.Run` aus,
  marshalt Ergebnis zurück, schaltet Busy-Indikator.
- `ElevatedCli`-Hilfe: startet `WinCleaner.exe <args>` per `runas`, wartet,
  liefert Exitcode.
- Eigenes App-Icon; installiert neben die CLI in
  `%LOCALAPPDATA%\Programs\WinCleaner`; als echtes Fenster normal an die
  Taskleiste anheftbar.

## Tests

ViewModel-/Hilfslogik (z. B. Auswahl-/Formatierungslogik, `ElevatedCli`-Argumentbau,
`GuiLogger`-Sink) per xUnit im bestehenden `WinCleaner.Tests`-Projekt. Die
Core-Aufrufe sind bereits abgedeckt.

## Nicht-Ziele (v2.0.0)

- Kein Ersatz von CLI/TUI — GUI ist additiv.
- Keine neuen Cleaner-Funktionen; nur grafische Hülle über Vorhandenes.
- Bild-Ähnlichkeit (M8), MFT-Scan (M11) bleiben weiterhin offen.
