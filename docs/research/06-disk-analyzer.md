# Speicherplatz-Analyse (Disk Space Analyzer)

Diese Kategorie umfasst Werkzeuge, die belegten Speicherplatz sichtbar machen: groesste Ordner/Dateien finden, Treemap-/Heatmap-Visualisierung, Dateityp-Gruppierung, oft auch Duplikatsuche. WinCleaner deckt diese Kategorie heute nur rudimentaer ab. Der Befehl `analyze-disk <Pfad>` liefert eine reine Top-N-Liste der groessten direkten Unterordner und Dateien (rekursive Groessensumme via `Directory.EnumerateFiles`, Skip von Reparse-Points/Junctions, `--json`). Es gibt KEINE MFT-basierte Schnellscan-Technik, KEINE Visualisierung, KEINE Dateityp-Gruppierung, KEINEN Export ausser JSON, KEINE Netzlaufwerk-Optimierung und KEINE GUI/TUI. `find-duplicates` ist ein separater, hash-basierter (Groesse -> 4-KB-Partial-SHA256 -> Voll-SHA256) Duplikat-Finder. Damit konkurriert WinCleaner funktional am ehesten mit dem CLI-Kern von WizTree oder czkawka_cli, ist aber bei Geschwindigkeit (kein MFT) und Komfort (keine Treemap, kein Export) klar unterlegen.

### WizTree

Der Geschwindigkeits-Massstab der Kategorie. Liest auf lokalen NTFS-Laufwerken die Master File Table (MFT) direkt aus und ist dadurch laut Hersteller bis zu 46x schneller als WinDirStat; ein Komplett-Scan dauert oft nur Sekunden.

**Feature-Katalog:**
- MFT-Direktlesen auf lokalen NTFS-Volumes (extrem schnell); benoetigt Adminrechte fuer den schnellen Modus, sonst Fallback auf langsameren Standard-Scan.
- Fallback-Scan (Datei-Enumeration) fuer FAT/exFAT, Netzlaufwerke und substituierte Laufwerke - dort KEIN MFT-Vorteil, deutlich langsamer.
- Treemap-Visualisierung (farbige Kachelansicht) plus sortierbare Datei-/Ordnerliste.
- Dateityp-Ansicht: Gruppierung/Statistik nach Dateiendung mit Groessenanteil.
- Integrierter Duplikat-Finder (nach Name, Groesse, optional Datum).
- Export als CSV und JSON; Kopieren von Datei-/Ordnerdaten in die Zwischenablage.
- Kommandozeilen-Modus: automatisierte CSV-Exporte und MFT-Dumps via CLI-Parameter.
- Suche/Filter ueber Dateinamen und Pfade.

**Preis & Lizenzmodell:**
- Kostenlos fuer den privaten (nicht-kommerziellen) Gebrauch.
- Kommerzielle Nutzung erfordert einen "Supporter Code"; Einstiegspreis ca. 25 USD fuer Einzelperson/Kleinstorganisation (Preis nach Mitarbeiterzahl am Standort gestaffelt, Lizenz pro Standort). **Stand pruefen.**
- Enterprise-Lizenz (>100 Mitarbeiter/Standort) und Enterprise-Multi-Site verfuegbar.
- Distribution License (Bundling in eigene Software) ca. 1.200 USD/Jahr. **Stand pruefen.**
- Lizenzen i. d. R. perpetual fuer das Versionsjahr; Renewal mit 50% Rabatt.

**Tech/UX:**
- GUI-Anwendung plus CLI-Teilmodus; Plattform: nur Windows.
- Closed Source (proprietaer).
- Reputation sehr hoch; gilt als schnellster Disk-Analyzer fuer Windows. Keine bekannten Bundling-/Adware-Probleme; saubere Installation, auch portable Nutzung gaengig.
- Datenschutz: arbeitet lokal; keine prominenten Telemetrie-Vorwuerfe.

### TreeSize (Free / Personal / Professional)

Professioneller deutscher Anbieter (JAM Software). Editionsgestaffelt vom kostenlosen Basis-Tool bis zur Profi-Version mit CLI, Cloud-Speichern und Automatisierung.

**Feature-Katalog (Free):**
- Ordner-/Dateigroessen-Analyse mit Treemap (2D/3D laut Editionsseite).
- Dateityp-Statistik, Datei-Alter- und Eigentuemer-Statistik auf Ordnerebene.
- Liste der groessten Dateien ("Top Files" / frueher Top 100).
- Lokale Laufwerke und externe/USB-Datentraeger; KEINE Domaenen-Netzlaufwerk-Analyse.
- Export nur als PDF.
- Basis-Filter nach Pfad/Name.
- KEIN Kommandozeilen-Modus, KEINE Snapshot-Vergleiche, KEINE geplanten Scans.

**Feature-Katalog (Personal, zusaetzlich):**
- Erweiterte Suche mit mehreren Filterattributen; Suche nach grossen Dateien.
- Duplikatsuche inkl. Pruefsummen-Vergleich.
- Export als Excel, CSV, HTML, SQLite.
- Zugriff auf Windows-Server-Freigaben, SharePoint, Google Drive, S3, Azure, Outlook, Linux via SSH (nur Privatgebrauch).

**Feature-Katalog (Professional, zusaetzlich):**
- Kommandozeilen-Unterstuetzung (Scans/Reports automatisierbar) und portable Installation (USB).
- Erweiterte Datei-Explorer-Operationen, Anzeige von NTFS-Berechtigungen.
- Snapshot-Vergleiche und geplante Scans.
- Alle Speicherarten unterstuetzt (inkl. kommerzieller Netz-/Cloud-Nutzung).

**Preis & Lizenzmodell:**
- Free: 0 EUR.
- Personal: ca. 1,70 EUR/Nutzer/Monat bei Jahresabrechnung (Abo). **Stand pruefen.**
- Professional: ab ca. 3,40 EUR/Nutzer/Monat bei Jahresabrechnung. **Stand pruefen.**
- Abo-Modell (jaehrliche Abrechnung); Unlimited-/Volumenlizenzen fuer Organisationen verfuegbar.

**Tech/UX:**
- GUI (Free/Personal); GUI + CLI (Professional). Plattform: nur Windows.
- Closed Source.
- Reputation sehr hoch, etabliert im Enterprise-/Admin-Umfeld. Keine Bloatware/Adware bekannt, serioeser Hersteller.
- Datenschutz: lokale Analyse; Hersteller mit DSGVO-/EU-Fokus.

### WinDirStat

Der Open-Source-Klassiker, seit Ende 2024 mit grossem 2.x-Rewrite wieder aktiv. Bekannt fuer die ikonische bunte Treemap und die Dateiendungs-Liste.

**Feature-Katalog:**
- Treemap-Visualisierung (farbcodiert nach Dateityp) mit synchronisierter Verzeichnisbaum-Ansicht.
- Erweiterungs-/Dateityp-Liste mit Groessenanteil und Prozentanzeige.
- Scan von internen, externen und Netzlaufwerken (klassische Datei-Enumeration, KEIN MFT-Schnellscan -> deutlich langsamer als WizTree).
- Benutzerdefinierte Cleanup-Jobs (eigene Aktionen/Loeschen aus der Oberflaeche).
- Dark Mode (seit 2.x), portable Version verfuegbar.

**Preis & Lizenzmodell:**
- Vollstaendig kostenlos, GPLv2 Open Source. Keine Editions-/Abo-Grenzen.

**Tech/UX:**
- GUI-Anwendung; KEIN dedizierter CLI-Modus. Plattform: nur Windows.
- Open Source (GitHub, aktiv gepflegt; stabile 2.x-Linie).
- Reputation: legendaerer Ruf, aber langsamer als MFT-Tools. Keine Adware/Bundling; auch ueber Microsoft Store verfuegbar.
- Datenschutz: lokal, keine Telemetrie.

### SpaceSniffer

Portables Freeware-Treemap-Tool mit besonders interaktiver, animierter Live-Visualisierung. Stark im explorativen "Wo ist mein Platz hin?"-Workflow.

**Feature-Katalog:**
- Treemap-Visualisierung mit Echtzeit-/Live-Aufbau waehrend des Scans, Zoom in Unterordner per Doppelklick.
- Anpassbare Filter (Groesse, Datum, Dateityp) direkt auf der Treemap.
- Tags/Farbcodierung von Dateien und Ordnern.
- Erkennung und Anzeige von NTFS Alternate Data Streams (seltenes Feature).
- Drag-and-Drop, Datei-Operationen ueber Windows-Explorer-Kontext.
- Vollstaendig portabel (kein Setup, nur XML-Konfig, kein Registry-Eingriff).

**Preis & Lizenzmodell:**
- Freeware (kostenlos). Spenden moeglich. Kein Editions-/Abo-Modell.

**Tech/UX:**
- GUI-Anwendung; KEIN CLI-Modus. Plattform: nur Windows.
- Closed Source (Freeware).
- Klassischer Datei-Enumeration-Scan (kein MFT) -> langsamer als WizTree, fuer den interaktiven Einsatz aber beliebt.
- Reputation hoch; aktiv (Version 2.2.x, Updates 2026). Sauber, kein Bundling.
- Datenschutz: lokal, keine Telemetrie.

### czkawka (inkl. Krokiet-GUI)

In Rust geschriebenes, plattformuebergreifendes Open-Source-Aufraeumwerkzeug. Schwerpunkt Duplikate, aber auch "groesste Dateien", leere Ordner, aehnliche Bilder/Musik/Videos. Funktional am naechsten an WinCleaners `find-duplicates` + `analyze-disk`-Kombination, aber deutlich breiter.

**Feature-Katalog:**
- Duplikatsuche per Hash (Blake3 empfohlen, alternativ CRC32/XXH3) mit persistentem Cache zwischen Laeufen (schnelle Wiederholungsscans).
- "Big Files"-Modus: groesste N Dateien (Default 50, konfigurierbar) mit Groesse/Pfad - direktes Gegenstueck zu `analyze-disk`.
- Leere Ordner, leere Dateien, temporaere/unnoetige Dateien, ungueltige Symlinks.
- Aehnliche Bilder (visuelle Aehnlichkeit), aehnliche Videos, Musik-Duplikate (per Audio-Inhalt oder Tags), kaputte Dateien.
- Referenz-/geschuetzte Ordner, JSON-Cache-Export, Loeschen/Verschieben/Hardlink-Aktionen.
- Vollwertige CLI (`czkawka_cli`) mit allen Scan-Modi der GUI.

**Preis & Lizenzmodell:**
- 100% kostenlos, MIT-Lizenz (Open Source). Keine Grenzen, kein Abo.

**Tech/UX:**
- GUI (GTK) und alternative GUI "Krokiet" sowie vollstaendige CLI. Plattform: Windows, Linux, macOS (inkl. Apple Silicon).
- Open Source (GitHub, sehr aktiv; v11.x, Februar 2026).
- Datenschutz: komplett offline, keine Telemetrie/Datensammlung.
- Reputation hoch in der Open-Source-/Linux-Community; KEINE Treemap-Visualisierung (Listenfokus); kein MFT-Schnellscan.

### Folder Size (MindGems)

Freemium-Disk-Analyzer mit Diagramm-Ansichten (Balken/Torte) und Explorer-Integration. Ohne Treemap, dafuer mit detaillierten Datei-/Ordner-Metadaten und Reports.

**Feature-Katalog:**
- Datei-/Ordnergroessen als Balken- und Tortendiagramme (keine klassische Treemap).
- Detail-Metadaten: Erstellungs-/Aenderungszeit, Datei- und Unterordner-Zahl.
- Vorgefertigte Reports: groesste/aelteste/neueste Dateien und Ordner.
- Anzeige von Ordnergroessen direkt im Windows-Explorer.
- Lokale, externe USB- und Netzlaufwerke.
- Datei-Operationen: Loeschen (auch in Free); Kopieren/Verschieben nur Paid.

**Preis & Lizenzmodell:**
- Freemium. Free-Version: Filter- und Export-Funktionen deaktiviert, Loeschen erlaubt.
- Paid-Version schaltet Filter, Export und erweiterte Operationen (Kopieren/Transfer) frei. Konkreter Preis **Stand pruefen**.

**Tech/UX:**
- GUI-Anwendung; KEIN CLI-Modus. Plattform: nur Windows.
- Closed Source.
- Ressourcenschonend (laut Hersteller nur bei Nutzung aktiv). Kein MFT-Schnellscan.
- Reputation solide; MindGems serioes, kein bekanntes Adware-Bundling. Datenschutz: lokal.

### RidNacs

Schlankes, schnelles Freeware-Tool mit Datei-Manager-artiger Baum-Ansicht und Prozent-Balken statt Treemap. Stark beim einfachen Export/Vergleich.

**Feature-Katalog:**
- Mehrspaltige Baum-Ansicht mit Prozent-Balkendiagramm-Spalte (konfigurierbar).
- Scan von Festplatten, Netzlaufwerken und einzelnen Ordnern.
- Export als XML, HTML, CSV, TXT (gut fuer Druck/Vergleich mit spaeteren Scans).
- Gruppierung von Dateien unterhalb einer konfigurierbaren Mindestgroesse.
- Datei-Operationen (oeffnen/loeschen) ueber Explorer-Kontext.
- Vertraute Dateimanager-Oberflaeche.

**Preis & Lizenzmodell:**
- Freeware (kostenlos). Kein Editions-/Abo-Modell.

**Tech/UX:**
- GUI-Anwendung; KEINE echte Treemap (nur Balken). Plattform: nur Windows.
- Closed Source (Freeware).
- Kein MFT-Schnellscan. Aelteres, aber stabiles Tool (3.0). Sauber, kein Bundling.
- Datenschutz: lokal, keine Telemetrie.

### Vergleichs-Feature-Matrix

| Tool | MFT-Schnellscan | Treemap/Heatmap | Dateityp-Gruppierung | Duplikate | Export | CLI-Modus | Netzlaufwerke | Plattform | Open Source | Preis |
|---|---|---|---|---|---|---|---|---|---|---|
| **WizTree** | Ja (lokal NTFS) | Ja | Ja | Ja | CSV, JSON | Ja | Ja (langsam, kein MFT) | Windows | Nein | Free privat; komm. Supporter Code ab ca. 25 USD |
| **TreeSize Free** | Nein | Ja | Ja | Nein | Nur PDF | Nein | Nein (Domaene) | Windows | Nein | 0 EUR |
| **TreeSize Personal** | Nein | Ja | Ja | Ja (Pruefsumme) | Excel/CSV/HTML/SQLite | Nein | Ja (auch Cloud) | Windows | Nein | ca. 1,70 EUR/Mon. (Abo) |
| **TreeSize Professional** | Nein | Ja | Ja | Ja | Excel/CSV/HTML/SQLite | Ja | Ja (alle) | Windows | Nein | ab ca. 3,40 EUR/Mon. (Abo) |
| **WinDirStat** | Nein | Ja | Ja | Nein | Begrenzt | Nein | Ja | Windows | Ja (GPLv2) | 0 EUR |
| **SpaceSniffer** | Nein | Ja (Live) | Filter | Nein | Begrenzt | Nein | Ja | Windows | Nein | Free |
| **czkawka** | Nein | Nein (Listen) | Teilw. | Ja (Blake3) | JSON/Text | Ja | Ja | Win/Linux/macOS | Ja (MIT) | 0 EUR |
| **Folder Size** | Nein | Nein (Balken/Torte) | Teilw. | Nein | Nur Paid | Nein | Ja | Windows | Nein | Freemium |
| **RidNacs** | Nein | Nein (Balken) | Min.-Groesse-Gruppe | Nein | XML/HTML/CSV/TXT | Nein | Ja | Windows | Nein | Free |
| **WinCleaner (Ist)** | Nein | Nein | Nein | Ja (SHA256) | Nur JSON | Ja (Kern-CLI) | Indirekt (Pfad) | Windows | (eigenes Repo) | n/a |

**Einordnung WinCleaner:** Funktional liegt `analyze-disk` auf dem Niveau eines minimalen CLI-Big-Files-Modus (vergleichbar czkawka "Big Files" oder WizTree-CLI-Export), jedoch ohne MFT-Beschleunigung, ohne Treemap, ohne Dateityp-Gruppierung und ohne flexiblen Export. Der integrierte Duplikat-Finder ist ein echtes Plus gegenueber TreeSize Free/WinDirStat/SpaceSniffer/RidNacs, bleibt aber hinter czkawka (Cache, mehrere Hash-Algorithmen, aehnliche Medien) zurueck.

> **Faktencheck-Korrekturen (Stand 2026):**
- **WinDirStat**: ~~Scan ... klassische Datei-Enumeration, KEIN MFT-Schnellscan -> deutlich langsamer als WizTree; und Matrix: WinDirStat MFT-Schnellscan = Nein.~~ -> Veraltet. Das aktuelle WinDirStat 2.x (README, v2.6.x, Stand Juni 2026) listet ausdruecklich 'fast NTFS scanning' (direktes NTFS-/MFT-Scannen) sowie Multithreading. WinDirStat besitzt also seit dem 2.x-Rewrite einen schnellen NTFS-Scan und ist nicht mehr nur auf langsame Datei-Enumeration beschraenkt. Matrix-Eintrag 'MFT-Schnellscan = Nein' ist falsch.
- **WinDirStat**: ~~Feature-Katalog WinDirStat: keine Duplikatsuche; Matrix: Duplikate = Nein; sowie Einordnungssatz 'Der integrierte Duplikat-Finder ist ein echtes Plus gegenueber TreeSize Free/WinDirStat/SpaceSniffer/RidNacs'.~~ -> Falsch/veraltet. WinDirStat 2.x bietet eine eigene 'Duplicate Files'-Ansicht mit konfigurierbarer Duplikaterkennung (Hash). Im README explizit als View und unter 'Search, duplicate detection, and filtering' genannt. Matrix-Eintrag 'Duplikate = Nein' ist falsch, und WinDirStat gehoert nicht mehr in die Aufzaehlung der Tools ohne Duplikat-Finder.
- **WinDirStat**: ~~GUI-Anwendung; KEIN dedizierter CLI-Modus; Matrix: CLI-Modus = Nein.~~ -> Veraltet. WinDirStat 2.x unterstuetzt laut README 'command-line targets' fuer Scans und 'command-line CSV workflows'. Es gibt also CLI-Parameter (zumindest fuer Scan-Ziele/CSV-Export). Die Aussage 'KEIN dedizierter CLI-Modus' ist zu absolut; Matrix 'CLI-Modus = Nein' ist mindestens irrefuehrend.
- **WinDirStat**: ~~stabile 2.x-Linie / impliziert Versionsstand um 2.0-2.2.~~ -> Veraltet. Aktuelle stabile Version ist 2.6.x (z. B. 2.6.1 vom 22.05.2026, 2.6.2 Anfang Juni 2026), nicht 2.0-2.2. 2.2.0 stammt vom 06.01.2025.
- **WizTree**: ~~Export als CSV und JSON; Matrix: Export = CSV, JSON.~~ -> Der JSON-Export laesst sich nicht belegen. WizTree dokumentiert (Website/Guides, Stand 2026) nur CSV-Export (inkl. 'Command Line CSV Export'); JSON wird nirgends als Exportformat genannt. Wahrscheinlich falsch -- Export auf CSV korrigieren.
- **WizTree**: ~~Kommerzielle Nutzung erfordert einen 'Supporter Code'; Einstiegspreis ca. 25 USD fuer Einzelperson/Kleinstorganisation.~~ -> Praezisierung noetig: 25 USD ist die Einzelperson-Lizenz (Individual). Die guenstigste Mehr-Personen-/Organisationslizenz (Micro Business, bis 5 Mitarbeiter) kostet 100 USD. Weitere Staffeln (Stand 2026): Small 200, Medium 350, Large 500, Enterprise Single-Site 750, Enterprise Multi-Site 1.800 USD. 'Einzelperson/Kleinstorganisation ab 25 USD' vermischt die getrennten Tiers.

