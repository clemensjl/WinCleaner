# WinCleaner — Master-Gap-Analyse & Roadmap

> Stand: 2026-06-17 · Methodik: Multi-Agent-Wettbewerbsanalyse über 8 Tool-Kategorien
> mit anschließendem Faktencheck. Konsolidiert die Einzelbefunde aus den
> Kategorie-Dateien `01`–`08` zu einer entduplizierten, priorisierten Roadmap.

## 1. Einleitung & Positionierung

WinCleaner ist ein **deutschsprachiges, reines Konsolen-CLI** für Windows
(.NET, nur Windows). Es unterscheidet sich bewusst von den großen GUI-Suiten
(CCleaner, Glary Utilities, Wise Care 365, IObit, Ashampoo WinOptimizer, AVG/Avast):

- **Skriptbarkeit zuerst:** `--json`-Ausgabe, Exit-Codes, Logging und
  Task-Scheduler-Integration (`schedule-clean`) machen WinCleaner zum Baustein
  für Admin-Automatisierung — dort, wo GUI-Suiten manuelle Klick-Workflows sind.
- **Safety-First-Philosophie:** Dry-Run als Default, Verschieben in den
  Papierkorb statt Hard-Delete, konservative Safety-Klassifizierung
  (nur `Safe`-Kategorien werden bereinigt), Bestätigungsabfragen, reversible
  Eingriffe (z. B. `startup-disable`), Wiederherstellungspunkte.
- **Kein Bloatware-Charakter:** keine Telemetrie, kein Bundling, kein resident
  laufender Dienst, kein "Health-Score"-Marketing.

Die Marktanalyse zeigt: WinCleaners **Kern** (Junk/Disk/Duplikate/Startup/Restore/
Scheduling) ist solide, aber **schmal**. Die größten Lücken liegen dort, wo die
gesamte Kategorie ein Feature als selbstverständlich voraussetzt
(Browser-Cleaning, sicheres Löschen, Software-Updates, Privacy-Tweaks, Appx-Debloat,
Uninstall mit Leftover-Bereinigung). Gleichzeitig gibt es **Anti-Features**
(siehe Abschnitt 5), die WinCleaner bewusst *nicht* bauen sollte, um Vertrauen
und Schlankheit zu erhalten.

Zwei interne **Sicherheitslücken** (Inkonsistenzen mit der eigenen Philosophie)
verdienen besondere Erwähnung, weil sie die Glaubwürdigkeit untergraben:
1. `find-duplicates --delete` ruft direkt `File.Delete` auf (kein Papierkorb),
   obwohl `clean-junk` korrekt in den Papierkorb verschiebt.
2. `find-duplicates --delete` behält "die erste" Datei je Gruppe (Enumerations-
   reihenfolge) — also faktisch zufällig, ohne Schutz wichtiger Ordner.

Diese gehören in **Welle 1**, weil sie kein neues Feature sind, sondern das
Reparieren eines Bruchs des eigenen Sicherheitsversprechens.

---

## 2. Konsolidierte Feature-Lücken nach Priorität

Die rund 60 Einzelbefunde aus 8 Kategorien wurden entdupliziert (z. B. tauchte
"Uninstall mit Leftover-Bereinigung", "Appx-Debloat", "Browser-Cleaning",
"winget-Update-Wrapper" und "sicheres Löschen" jeweils in mehreren Kategorien
auf). Komplexität: **klein** (Flags/Wrapper, < ~1 Tag), **mittel** (neues
Subsystem mit Tests, einige Tage), **groß** (Raw-Systemzugriff/Backup-Engine/
neue Datenmodelle, Wochen).

### 2.1 Priorität HOCH

| # | Feature (konsolidiert) | Konkurrenten | Passt zum CLI-Charakter? | Komplexität |
|---|------------------------|--------------|--------------------------|-------------|
| H1 | **Duplikate in den Papierkorb statt Hard-Delete** + Bestätigungs-/Dry-Run-Flow wie bei `clean-junk` | dupeGuru, czkawka, Auslogics, AllDup, Duplicate Cleaner | Ja — repariert Bruch der eigenen Safety-Philosophie; reine Wiederverwendung der `clean-junk`-Papierkorb-Logik | **klein** |
| H2 | **Konfigurierbare Behalte-Strategie** für Duplikate (`--keep oldest\|newest\|shortest-path`, `--keep-in <Pfad>`) + **geschützte Referenzordner** (`--protect <Pfad>`) | AllDup, Duplicate Cleaner, dupeGuru (Reference Folders), czkawka | Ja — ideal als CLI-Flags; behebt das "blind erste behalten"-Risiko | **klein** |
| H3 | **Software-Update-Wrapper um winget** (`update`, `list-updates --json`; `winget upgrade --all --silent`) | winget, Chocolatey, Patch My PC, UniGetUI, UCheck | Ja — perfekt für Konsole/Skript, nutzt vorinstalliertes winget, hoher Nutzen bei geringem Aufwand | **klein** |
| H4 | **Detailliertes Browser-Cleaning** (Cache/Cookies/Verlauf pro Browser & Profil: Chrome/Edge/Firefox, optional SQLite-Vacuum) | BleachBit, CCleaner, Wise Care, AVG/Avast | Ja — BleachBit beweist CLI-Machbarkeit (winapp2.ini-Ansatz); baut auf vorhandenem Junk-Scanner auf | **mittel** |
| H5 | **File-Shredder / sicheres Löschen** einzelner Dateien & Ordner (`shred <Pfad>`, konfigurierbare Überschreib-Pässe; SSD-Limitierung dokumentieren) | BleachBit (`--shred`), Eraser, Glary File Shredder, Ashampoo File Wiper, CCleaner Drive Wiper | Ja — skriptbar, natürliches Pendant zur bestehenden Löschpipeline, fügt sich in Safety-/Dry-Run-Logik | **mittel** |
| H6 | **Programm-Deinstallation inkl. Leftover-Bereinigung** (Listing + Uninstall + Datei-/Registry-Reste; nach Deinstall mit Junk-/Dup-Scanner verzahnt) | Revo, BCU, HiBit, Wise, Geek, IObit, Ashampoo, winget/UniGetUI | Ja — logische Erweiterung von Startup-Verwaltung; Listing/Uninstall als CLI ideal | **mittel→groß** |
| H7 | **Batch-/Multi-Uninstall + Silent-Uninstall mit Installer-Erkennung** (NSIS/InnoSetup/MSI, korrekte Silent-Flags) | BCU (Open-Source-Vorbild), Revo Pro, IObit, HiBit, Ashampoo | Ja — BCU ist faktisch eine CLI; passt direkt zu `--json`/`--yes`/Scheduling | **mittel** |
| H8 | **Appx-/Bloatware-Entfernung** (vorinstallierte Windows-/Store-Apps; Whitelist + Dry-Run + Restore-Point) | Win11Debloat, CTT WinUtil, Sophia, AVG/Avast, Ashampoo | Ja — `Remove-AppxPackage`-Logik passt exzellent zu Windows-CLI; geringe Risikofläche mit Whitelist | **mittel** |
| H9 | **Privacy-/Telemetrie-Tweaks** (Diagnose-Level, Werbe-ID, Activity History, Tracking-Dienste; reversibel, Restore-Point davor) — **inkl. KI-Telemetrie** (Copilot, Recall, Office/KI) | O&O ShutUp10++, W10Privacy, WPD, Sophia, Win11Debloat, CTT WinUtil | Ja — **echtes Alleinstellungsmerkmal**: alle etablierten Privacy-Tools sind GUI-only; Mechanik (Registry/Policy/UAC) beherrscht WinCleaner schon | **mittel** |
| H10 | **Reversible Tweak-Engine mit Apply/Undo** (kuratierte Profile Standard/Advanced; jeder Tweak mit Rückgängig-Funktion) | CTT WinUtil (Standard/Advanced + Undo), Sophia (Restore je Tweak) | Ja — baut auf Restore-Point + reversiblem `startup-disable`; der differenzierende, Safety-getriebene Rahmen für H8/H9 | **mittel→groß** |
| H11 | **Dateityp-/Erweiterungs-Gruppierung in `analyze-disk`** (Größenanteil pro Endung) | WizTree, TreeSize, WinDirStat | Ja — reine Aggregation, keine GUI nötig; beantwortet "welche Dateiarten fressen Platz" | **klein** |
| H12 | **Flexibler Export der Disk-Analyse** (CSV/HTML zusätzlich zu JSON) | RidNacs, WizTree, TreeSize | Ja — CLI-Output wird weiterverarbeitet; CSV/HTML ist Reporting-Standard | **klein** |

### 2.2 Priorität MITTEL

| # | Feature (konsolidiert) | Konkurrenten | Passt zum CLI-Charakter? | Komplexität |
|---|------------------------|--------------|--------------------------|-------------|
| M1 | **Backup/Rollback vor Deinstallation** (Registry-Restore je Session + automatischer Restore-Point) | HiBit, Revo Pro | Ja — `create-restore-point` + Papierkorb-Logik existieren; nur vorschalten/verketten | **mittel** |
| M2 | **Forced/Erzwungene Deinstallation** beschädigter/uninstaller-loser Programme (mit Safety-Klassifizierung) | Geek, HiBit, Wise, Revo Pro, IObit, Ashampoo | Bedingt — riskant, daher nur mit Dry-Run/Klassifizierung wie `clean-junk` | **groß** |
| M3 | **Leftover-Scan inkl. Dienste & geplanter Tasks** | BCU | Ja — Infrastruktur für Tasks/Autostart teils vorhanden | **mittel** |
| M4 | **Dienste-Verwaltung** (Dienste reversibel auf Manual/Disabled; kuratierte Profile) | CTT WinUtil, Sophia, Talon | Ja — logischer dritter Baustein neben Autostart + Tasks | **mittel** |
| M5 | **Paketmanagement / Software-Install per CLI** (Wrapper um `winget install/uninstall`) | winget, Chocolatey, Scoop, UniGetUI, UCheck | Ja — kein eigenes Repo (unwirtschaftlich); reiner Wrapper Richtung "Wartungs-Suite" | **klein** |
| M6 | **Auto-Update-Scheduling** (geplante `winget upgrade`-Läufe) | Patch My PC, Ninite Pro | Ja — `schedule-clean`-Infrastruktur direkt erweiterbar | **klein** |
| M7 | **Hardlink-Ersetzung** statt Löschen für Duplikate (`--hard-link`, NTFS `CreateHardLink`) | czkawka, AllDup, Duplicate Cleaner | Ja — sichere Variante, spart Platz ohne Datenverlust | **mittel** |
| M8 | **Bild-Ähnlichkeitserkennung** (perceptual hashing / pHash/dHash) | czkawka, dupeGuru (Picture), AllDup | Bedingt — häufigster realer Duplikatfall, aber Bibliotheks-/Rechenaufwand | **groß** |
| M9 | **Konfigurierbare Tiefe/Top-N & Filter** in `analyze-disk` (Mindestgröße, Dateityp, Alter, mehrstufige Tiefe) | RidNacs, SpaceSniffer, TreeSize | Ja — trivial über Flags nachrüstbar | **klein** |
| M10 | **Snapshot-/Vergleichsfunktion** (zwei Disk-Scans diffen über gespeicherte JSON-Snapshots) | TreeSize Pro, RidNacs | Ja — passt zu wiederholt geplant laufendem Tool | **mittel** |
| M11 | **MFT-basierter NTFS-Schnellscan** (statt `Directory.EnumerateFiles`) | WizTree (≈46× schneller als WinDirStat) | Ja, aber Raw-MFT-Lesen + Adminrechte | **groß** |
| M12 | **Free-Space-Wiping** (freien Speicher auf HDD überschreiben; SSD-Erkennung + Warnung) | BleachBit, Eraser | Ja — ergänzt H5 (Shredder) | **mittel** |
| M13 | **Telemetrie-Server-Blocking** via Firewall-Regeln und/oder HOSTS (fertige Regelsätze, WindowsSpyBlocker) | WPD, W10Privacy, Privatezilla | Ja — `netsh advfirewall`/HOSTS-Edit + Self-Elevation | **mittel** |
| M14 | **Privacy-Audit-Modus** (`scan-privacy --json`, read-only Ist-Zustand) | Privatezilla, O&O ShutUp10++ | Ja — passt perfekt zu Dry-Run/JSON; Reporting ohne Systemänderung | **klein** |
| M15 | **Treiber-Scan** (veraltete/fehlende/fehlerhafte Treiber erkennen) **+ Treiber-Backup/Export & Rollback** **+ verifizierte Quelle** (WHQL/Signaturprüfung) | IObit, Driver Easy, Avast, Auslogics, **SDIO** (Open-Source-Vorbild) | Bedingt — außerhalb des Cleaner-Kerns, hohe Sicherheits-/Quellenverantwortung; **nur** mit Signaturprüfung & seriöser Quelle | **groß** |
| M16 | **Vorschau/Trockenlauf-Report der Duplikatgruppen** vor dem Löschen (Default-Listing + explizite Bestätigung) | Auslogics, AllDup, dupeGuru, Duplicate Cleaner | Ja — Konsistenz mit `clean-junk` (teilweise von H1 abgedeckt) | **klein** |

### 2.3 Priorität NIEDRIG

| # | Feature (konsolidiert) | Konkurrenten | Passt zum CLI-Charakter? | Komplexität |
|---|------------------------|--------------|--------------------------|-------------|
| N1 | **Reporting / Verlauf** (pro Lauf bereinigte MB, Vorher-Nachher-Summary, optional Markdown/JSON-Export) | CCleaner, IObit, iolo, CTT WinUtil | Ja — Logging/`--json` vorhanden, kleiner Mehrwert | **klein** |
| N2 | **Optionaler GUI-/TUI-Wrapper** (schlanke Menü-TUI über bestehende Befehle) | CTT WinUtil, Win11Debloat, SophiApp, Talon | Bedingt — nicht Kern des CLI-Profils, aber günstiger Reichweiten-Hebel | **mittel** |
| N3 | **Erweiterter Duplikat-Finder** (persistenter Hash-Cache zwischen Läufen, schnellere Hashes Blake3/XXH3) | czkawka | Ja — Optimierung; Kernfunktion existiert bereits | **mittel** |
| N4 | **Byte-genaue Verifikation als Opt-in** (`--verify-bytes`, Paranoia-Modus) | AllDup, Anti-Twin | Ja — technisch bei SHA-256 unnötig, reines Vertrauens-Feature | **klein** |
| N5 | **Zusatzscanner** (leere Ordner, 0-Byte-Dateien, kaputte Symlinks) | czkawka | Ja — eigene Subkommandos, rundet Cleaner-Kern ab | **klein** |
| N6 | **Treemap-/Heatmap-Visualisierung** (ASCII-TUI oder generierter HTML-Report) | WinDirStat, SpaceSniffer, WizTree | Bedingt — als reine CLI nur Annäherung | **mittel** |
| N7 | **Audio-Duplikate** nach Tags/ähnlichem Inhalt | dupeGuru (Music), czkawka, Duplicate Cleaner | Nein/Nische — für System-Cleanup randständig | **groß** |
| N8 | **Ähnliche-Medien-/kaputte-Dateien-Erkennung** | czkawka | Nein/Nische | **groß** |
| N9 | **Suche in Archiven** (ZIP/RAR/7Z) | AllDup, Duplicate Cleaner | Nein/Nische — rechenintensiv | **mittel** |
| N10 | **Defragmentierung / SSD-TRIM** (dünner Wrapper um `defrag.exe`/`Optimize-Volume`) | Glary, Wise Care, Ashampoo, iolo | Bedingt — unter SSD/modernem Windows geringer Mehrwert | **klein** |
| N11 | **Offline-Treiber-Installation** aus lokalem Pack/USB; **skriptbare Treiber-Ops** + Scheduling | SDIO, DriverPack, IObit | Nein/Nische — hoher Pflegeaufwand, abhängig von M15 | **groß** |
| N12 | **Installations-Monitoring** (Snapshot vor/nach oder Echtzeit-Logging) | Revo Pro, Ashampoo, IObit | Nein — Treiber/Hooking, Over-Engineering für CLI | **groß** |
| N13 | **Browser-Erweiterungen verwalten/entfernen** | IObit, Revo Pro, HiBit, Wise, Ashampoo | Bedingt — stark browserspezifisch, nachrangig | **mittel** |
| N14 | **Drittanbieter-App-Telemetrie** (Office, Firefox, Dropbox) deaktivieren | Privatezilla | Bedingt — pflegeintensiv pro App | **mittel** |
| N15 | **Persistenter Privacy-Reapply** nach Windows-Updates (geplanter Reapply-Lauf) | O&O ShutUp10++ Premium | Ja — über Scheduler nachbildbar | **klein** |
| N16 | **Restore-Point vor Privacy-Änderungen** automatisch | O&O ShutUp10++ | Ja — `create-restore-point` nur vorschalten (im Rahmen von H9/H10) | **klein** |
| N17 | **RAM-/Memory-Optimierung** | Glary, Wise Care, IObit, AVG | **Nein — Snake-Oil** (siehe Abschnitt 5) | — |
| N18 | **Real-time-Monitoring / aktiver Schutz** | Wise Care, IObit, iolo | **Nein** — residenter Dienst widerspricht CLI-/Vertrauens-Charakter | — |

---

## 3. Empfohlene Roadmap (3 Wellen)

Die Wellen sind so geschnitten, dass früh **viel Nutzen bei kleinem Aufwand**
entsteht, bestehende Bausteine (Papierkorb-Logik, Restore-Point, Scheduler,
`--json`) wiederverwendet werden und WinCleaners CLI-/Safety-/Open-Source-Geist
gewahrt bleibt.

### Welle 1 — Quick Wins & Safety-Reparaturen (klein, hoher Nutzen)
Ziel: Eigene Sicherheitsphilosophie konsistent machen + sofort sichtbarer Mehrwert.

- **H1 / H2 / H16** — Duplikat-Finder reparieren: Papierkorb statt Hard-Delete,
  Behalte-Strategie-Flags, geschützte Referenzordner, Dry-Run/Bestätigung wie
  `clean-junk`. *(Behebt den gefährlichsten Bruch der eigenen Philosophie.)*
- **H3** — `update` / `list-updates --json` als winget-Wrapper.
- **H11 / H12** — `analyze-disk`: Gruppierung pro Dateiendung + CSV/HTML-Export.
- **M9** — `analyze-disk`-Filter (Mindestgröße/Typ/Alter/Tiefe) als Flags.
- **M14** — `scan-privacy --json` (read-only Audit, ungefährlich, baut Vertrauen).
- **N1** — einfacher Run-Report (bereinigte MB pro Lauf).

### Welle 2 — Mittelfristig (neue Subsysteme, Kern-Paritäten)
Ziel: Die Features schließen, die die ganze Kategorie als selbstverständlich erwartet.

- **H4** — Detailliertes Browser-Cleaning (pro Browser/Profil), als sicherer
  Ausbau des Junk-Scanners.
- **H5 (+ M12)** — `shred`-Befehl (sicheres Löschen) und optionales Free-Space-Wiping
  mit SSD-Erkennung/Warnung.
- **H8** — Appx-/Bloatware-Entfernung mit Whitelist + Dry-Run + Restore-Point.
- **H9 (+ N16)** — Privacy-/Telemetrie-Tweaks inkl. KI-Telemetrie; Restore-Point
  automatisch vorgeschaltet. *(Größter Differenzierer: CLI in einem GUI-only-Feld.)*
- **H6 / H7 (+ M1)** — Uninstall-Subsystem: Listing, Silent-/Batch-Uninstall mit
  Installer-Erkennung, Leftover-Bereinigung, Backup/Restore davor.
- **M5 / M6** — Paket-Install-Wrapper + Auto-Update-Scheduling.
- **M13** — Telemetrie-Server-Blocking (Firewall/HOSTS) mit kuratierten Regelsätzen.

### Welle 3 — Strategisch (groß, differenzierend, Safety-kritisch)
Ziel: WinCleaner als skriptbare Wartungs-Suite mit einzigartigem Reversibilitäts-Rahmen.

- **H10** — Reversible **Tweak-Engine** mit Apply/Undo & Standard/Advanced-Profilen
  als gemeinsames, getestetes Fundament für H8/H9/M4. *(Das eigentliche
  Differenzierungs-Asset: kuratiert + reversibel + skriptbar.)*
- **M4** — Dienste-Verwaltung (reversibel) als dritter Hygiene-Baustein.
- **M3** — Leftover-Scan inkl. Dienste & geplanter Tasks.
- **M7 / M8** — Hardlink-Ersetzung und Bild-Ähnlichkeitserkennung im Duplikat-Finder.
- **M10 / M11** — Disk-Snapshot-Diff und (optional) MFT-Schnellscan.
- **M15** — *Falls überhaupt* Treiber: **nur** mit Backup/Rollback und strikter
  WHQL-/Signaturprüfung aus seriöser Quelle (sonst nicht bauen, siehe Abschnitt 5).
- Optional/Nice-to-have: **N2** TUI-Wrapper, **N3/N5** Cache & Zusatzscanner,
  **N6** HTML-Treemap, **N15** Privacy-Reapply-Scheduling.

---

## 4. Master-Feature-Matrix (WinCleaner vs. typische Konkurrenz)

`hat` = vorhanden · `teilweise` = grob/eingeschränkt vorhanden · `fehlt` = nicht vorhanden

| Funktionsbereich | WinCleaner (IST) | Typische GUI-Suite | Ziel-Welle |
|------------------|------------------|--------------------|------------|
| Junk-/Temp-Bereinigung (Dry-Run, Papierkorb, Safety) | **hat** | hat | — |
| Browser-Cleaning (granular pro Browser/Profil) | **teilweise** (generischer Cache im Junk-Scan) | hat | W2 (H4) |
| Disk-Analyse (größte Ordner/Dateien) | **hat** | hat | — |
| Disk-Analyse: Gruppierung pro Dateityp | **fehlt** | hat | W1 (H11) |
| Disk-Analyse: Export CSV/HTML | **teilweise** (nur JSON) | hat | W1 (H12) |
| Disk-Analyse: Filter/Tiefe, Snapshot-Diff, MFT-Speed | **fehlt** | hat (teilw.) | W1/W3 (M9/M10/M11) |
| Duplikat-Finder (Hash-basiert) | **hat** | hat | — |
| Duplikate: Papierkorb statt Hard-Delete | **fehlt** (Hard-Delete!) | hat | W1 (H1) |
| Duplikate: Behalte-Strategie & Schutzordner | **fehlt** | hat | W1 (H2) |
| Duplikate: Bild-Ähnlichkeit / Hardlink | **fehlt** | hat (teilw.) | W3 (M7/M8) |
| Startup-/Autostart-Verwaltung (reversibel) | **hat** | hat | — |
| Dienste-Verwaltung (reversibel) | **fehlt** | hat | W3 (M4) |
| Restore-Point-Erstellung | **hat** | hat (teilw.) | — |
| Geplante Bereinigung (Task Scheduler) | **hat** | hat (teilw.) | — |
| Sicheres Löschen / File-Shredder | **fehlt** | hat | W2 (H5) |
| Free-Space-Wiping | **fehlt** | hat | W2 (M12) |
| Programm-Deinstallation + Leftover-Bereinigung | **fehlt** | hat | W2 (H6/H7) |
| Backup/Rollback vor Deinstallation | **teilweise** (Restore-Point existiert) | hat | W2 (M1) |
| Appx-/Bloatware-Entfernung | **fehlt** | hat | W2 (H8) |
| Privacy-/Telemetrie-Tweaks (inkl. KI/Copilot/Recall) | **fehlt** | **GUI-only** bei Konkurrenz | W2 (H9) |
| Privacy-Audit (read-only) | **fehlt** | hat (teilw.) | W1 (M14) |
| Telemetrie-Server-Blocking (Firewall/HOSTS) | **fehlt** | hat (teilw.) | W2 (M13) |
| Reversible Tweak-Engine (Apply/Undo, Profile) | **teilweise** (Restore + reversibler Startup-Disable) | hat (teilw.) | W3 (H10) |
| Software-Update (winget-Wrapper) | **fehlt** | hat | W1 (H3) |
| Paket-Install per CLI / Auto-Update-Scheduling | **fehlt** | hat | W2 (M5/M6) |
| Treiber-Scan/-Update (verifizierte Quelle) | **fehlt** | hat | W3 (M15, bedingt) |
| Reporting/Verlauf (bereinigte MB, Summary) | **teilweise** (Logging + `--json`) | hat | W1 (N1) |
| Maschinenausgabe `--json` | **hat** | selten/fehlt | — *(WinCleaner-Vorteil)* |
| GUI/TUI | **fehlt (bewusst)** | hat | optional W3 (N2) |
| RAM-Optimierung | **fehlt (bewusst, Anti-Feature)** | hat | **nie** |
| Real-time-Monitoring (resident) | **fehlt (bewusst, Anti-Feature)** | hat | **nie** |

---

## 5. Bewusst NICHT bauen (Anti-Features)

Diese Features würden WinCleaners Vertrauenswürdigkeit, Schlankheit oder
Ehrlichkeit beschädigen. Sie sind **bewusste Nicht-Ziele** — Weglassen ist hier
ein Qualitätsmerkmal (vgl. BleachBits bewussten Verzicht auf Registry-Cleaning).

- **Aggressiver Registry-Cleaner.** Realnutzen ist nachweislich umstritten,
  das Risiko (kaputte Profile/Bootprobleme) hoch. Falls überhaupt, **nur** extrem
  konservativ mit Pflicht-Backup und enger `Safe`-Klassifizierung — und selbst
  dann ist der bewusste Verzicht (BleachBit-Linie) gut vertretbar. Kein
  "X.000 Fehler gefunden!"-Marketing.
- **RAM-/Memory-"Optimierung".** Unter modernem Windows weitgehend Placebo
  (Working-Set-Trimming schadet eher). Aufnahme würde WinCleaner zum "Snake-Oil"-
  Tool degradieren. **Nicht bauen.**
- **Resident laufendes Real-time-Monitoring / "aktiver Schutz".** Widerspricht
  dem schlanken On-Demand-CLI-Charakter, erzeugt einen Hintergrunddienst und
  rückt gefährlich nah an Telemetrie/Autostart-Bloat. Allenfalls ein expliziter,
  vom Nutzer gestarteter `watch`-Modus — kein Autostart-Daemon.
- **Treiber-Updates aus dubiosen/unverifizierten Quellen.** DriverPack (PUP-/
  Adware-Bundling, gefälschte Versionen) und IObit zeigen, wie man es **nicht**
  macht. Treiber nur, wenn WHQL-/Signaturprüfung und eine seriöse Quelle
  garantiert sind und Backup/Rollback existiert — sonst gar nicht.
- **Bundling, Telemetrie, "Health-Score"-Gamification, Pro-Upsell-Nags.** Jede
  Form von mitinstallierter Drittsoftware, Phone-Home oder künstlichem
  Dringlichkeits-Marketing ist ausgeschlossen.
- **Installations-Monitoring per Treiber/Hooking.** Mächtig, aber Over-Engineering
  für eine schlanke CLI; Snapshot-Diffing nur, falls je leichtgewichtig machbar.
- **Eigenes Paket-Repository.** Unwirtschaftlich — winget ist gratis und
  vorinstalliert; WinCleaner kapselt es, statt ein konkurrierendes Repo zu pflegen.

**Leitprinzip:** Jeder schreibende Eingriff ist *reversibel* (Papierkorb,
Restore-Point, Undo) oder *read-only* (Audit/Scan). Nicht-reversible Aktionen
(z. B. `shred`) sind opt-in, klar gekennzeichnet und niemals Default.