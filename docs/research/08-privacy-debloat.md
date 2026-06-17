# Privacy-, Telemetrie- & sicheres Loeschen

Diese Kategorie deckt zwei verwandte, aber technisch unterschiedliche Aufgabenbereiche ab: (1) das Abschalten von Windows-Telemetrie, Tracking, Werbe-IDs und neuerdings KI-Funktionen (Copilot, Recall) sowie das Blockieren von Microsoft-Telemetrieservern, und (2) das datenschutzkonforme, nicht wiederherstellbare Loeschen von Dateien bzw. Ueberschreiben von freiem Speicherplatz. Als Vergleichsbasis dienen die Windows-Bordmittel (Storage Sense / Disk Cleanup). WinCleaner deckt aktuell KEINE dieser Funktionen ab - weder Privacy/Telemetrie-Tweaks noch sicheres Loeschen. Das ist die zentrale Erkenntnis fuer die Gap-Analyse.

Wichtiger technischer Hinweis vorab (gilt fuer ALLE Shredder/Wipe-Tools): Mehrfaches Ueberschreiben bietet auf modernen SSDs/NVMe keine Garantie mehr. Durch Wear-Leveling, Overprovisioning, remappte Sektoren und TRIM kann die Software die physischen NAND-Zellen nicht zuverlaessig erreichen. Verlaessliche Methoden sind hier vorherige Voll-Verschluesselung (BitLocker) und der herstellerseitige ATA Secure Erase / NVMe Format-Befehl. Tools wie Eraser und BleachBit selbst weisen darauf hin. Fuer klassische HDDs bleibt Ueberschreiben weiterhin wirksam.

### O&O ShutUp10++

Der De-facto-Standard fuer Windows-Telemetrie-Abschaltung im deutschsprachigen Raum (deutscher Hersteller, deutschsprachige UI). Portables Einzel-EXE ohne Installation und ohne Hintergrunddienst.

**Feature-Katalog:**
- Buendelt saemtliche Datenschutz-relevanten Schalter in einer GUI, gruppiert in Kategorien (Privacy, Activity History, App-Berechtigungen, Telemetrie, Windows AI/Copilot, Office-Telemetrie).
- Aktuelle Versionen decken neuere Windows-Funktionen ab: Windows Copilot und Recall lassen sich abschalten.
- Schaltet Windows-Telemetrie/Diagnosedaten-Uebertragung ein/aus, deaktiviert Werbe-ID und personalisierte Werbung.
- Setzt Aenderungen ueber Registry, Gruppenrichtlinien-/Policy-Werte, Dienste und geplante Aufgaben um.
- Ampel-Farbcodierung der Empfehlungen: gruen (unbedenklich/empfohlen), gelb (mit Vorsicht), rot (kann Funktionen beeintraechtigen).
- Erstellt vor Aenderungen automatisch einen Systemwiederherstellungspunkt (gut dokumentiertes Verhalten der Free-Version).
- Hinterlaesst keine Autostart-Eintraege, sofern man sie nicht selbst anlegt.
- KEIN sicheres Loeschen / Free-Space-Wiping - reines Privacy/Telemetrie-Tool.

**Preis & Lizenzmodell:**
- Free-Version: vollstaendig kostenlos, alle Einstellungen manuell setzbar.
- Premium-Version: Einmalkauf, KEIN Abo. Listenpreis 19,90 EUR, zeitweise Aktionspreis ab 14,90 EUR (Stand 2026 pruefen). Genaue Anzahl abgedeckter PCs auf der Produktseite nicht eindeutig ausgewiesen - Lizenzbedingungen pruefen.
- Premium-Mehrwert gegenueber Free: gewaehlte Einstellungen werden dauerhaft ueberwacht und nach Aenderungen (z. B. durch Windows-Updates) automatisch wiederhergestellt; Benachrichtigungen; kontinuierliche Pflege fuer neue Windows-Versionen.

**Tech/UX:**
- GUI-only, kein offizielles CLI. Portabel, keine Installation.
- Plattform: Windows 10 und 11.
- NICHT Open Source (proprietaer).
- Reputation: hoch; etablierter deutscher Anbieter (O&O Software GmbH), keine Bundling-/Adware-Vorwuerfe, kein Telemetrie-/Bloatware-Risiko.

### W10Privacy

Umfangreiches deutschsprachiges Freeware-Tweaking-Tool (deutscher Entwickler), das deutlich tiefer geht als ShutUp10, dafuer weniger anfaengerfreundlich ist.

**Feature-Katalog:**
- Ueber hundert Telemetrie-/Tracking-/Tweak-Optionen, organisiert in Tabs: Privacy, Telemetrie, Suche, Netzwerk, Explorer, Dienste (Services), OneDrive, geplante Aufgaben (Tasks), Tweaks u. a.
- Standortverfolgung, Telemetrie-Stufe, Werbe-ID, Cortana-Funktionen, Hintergrund-Apps und Netzwerk-/Freigabe-Einstellungen konfigurierbar.
- Blockieren bekannter Microsoft-Telemetrieserver per Firewall-Regel ODER ueber die HOSTS-Datei.
- Farbcodierung (welche Optionen sicher deaktivierbar sind) plus Tooltip-Beschreibung je Option.
- Aenderungen vor dem Anwenden review-/bestaetigbar.
- Verwaltung von Diensten und geplanten Aufgaben integriert (relevant fuer WinCleaner-Luecken Dienste-/Tasks-Verwaltung).
- KEIN sicheres Loeschen / Free-Space-Wiping.

**Preis & Lizenzmodell:**
- Komplett kostenlos (Freeware), keine Bezahl-Stufe, keine Geraetebeschraenkung. Keine Installation noetig.

**Tech/UX:**
- GUI-only, kein CLI. Portabel.
- Plattform: Windows 10 und 11 (aktuelle Version 5.4.x, Stand pruefen).
- NICHT Open Source (Freeware, Quellcode nicht oeffentlich).
- Reputation: gut etabliert, von Fachpresse (gHacks, BleepingComputer) wiederholt empfohlen; kein Adware-/Bundling-Risiko. Zielgruppe eher Power-User wegen Optionsfuelle.

### Privatezilla

Open-Source-Tool (Nachfolger von "Spydish") mit Fokus auf einen automatisierten Privacy-Check plus PowerShell-Skripting.

**Feature-Katalog:**
- Aktuell ca. 60 integrierte Datenschutzeinstellungen, jeweils aktivierbar/deaktivierbar.
- "Privacy Check": prueft auf Knopfdruck den Ist-Zustand; gesetzte Einstellungen werden als "Configured" markiert.
- Telemetrie-Abschaltung auch fuer Drittanbieter-Apps (z. B. CCleaner, Firefox, Dropbox, Microsoft Office).
- Telemetrie-Blocking via Firewall und HOSTS-Datei (Regeln aus crazy-max/WindowsSpyBlocker).
- PowerShell-basiertes Scripting; Community-Packages erlauben z. B. Entfernen vorinstallierter Apps, OneDrive-Deinstallation, Start-Menue-Entpinnen, Einbindung des Windows10Debloater.ps1.
- KEIN sicheres Loeschen / Free-Space-Wiping.

**Preis & Lizenzmodell:**
- Kostenlos und Open Source (MIT-Lizenz). Keine Bezahl-Stufe.

**Tech/UX:**
- GUI plus PowerShell-Scripting-Anbindung.
- Plattform: ausgewiesen fuer Windows 10 (Builds 1809-2009); KEINE offizielle Windows-11-Pflege.
- Open Source (GitHub: builtbybel/privatezilla).
- WARTUNGSSTATUS: letzte Release 0.60.0 vom Juni 2022, seitdem keine aktive Entwicklung. De facto eingestellt/veraltet - fuer Windows 11 nur eingeschraenkt brauchbar. Relevant als Architektur-Vorbild (Check-Konzept, WindowsSpyBlocker-Regeln), weniger als Live-Konkurrent.

### WPD (Windows Privacy Dashboard)

Sehr kleines (~335 KB) portables Tool, das ueber die Windows-API arbeitet; bekannt fuer seine Firewall-Regelsaetze.

**Feature-Katalog:**
- Drei Kernfunktionen: (1) Datenschutzeinstellungen verwalten (Telemetrie, Customer Experience Improvement, Werbe-ID, Eingabe-Personalisierung, Cortana, OneDrive u. a.), (2) Windows-Telemetrie-IP-Adressen per Firewall blockieren, (3) Windows-Store-Apps deinstallieren.
- Vorgefertigte Firewall-Regelsaetze: "Spy", "Update" und "Extra" - per Klick aktivierbar (blockiert Telemetrie-/Update-/sonstige MS-Endpunkte).
- App-Deinstaller fuer Modern-UI/Store-Apps (auch vorinstallierte) - relevant fuer WinCleaner-Luecke Appx-Debloat.
- Unterstuetzt Kommandozeilen-Argumente/Switches (skriptbar) und mehrere Sprachen; Dark Theme.
- Deckt auch Server-/Enterprise-/Education-Editionen ab.
- KEIN sicheres Loeschen / Free-Space-Wiping.

**Preis & Lizenzmodell:**
- Kostenlos, portabel, werbefrei.

**Tech/UX:**
- GUI mit CLI-Unterstuetzung (Switches). Portabel, keine Installation.
- Plattform: Windows 10 und 11 (alle unterstuetzten Client- und Server-Versionen).
- Als Open Source bezeichnet (Quellen widerspruechlich; einige Verzeichnisse fuehren es als "discontinued"). Aktualitaetsstatus pruefen - letzte regulaere Version 1.5.x, Copyright bis 2022 ausgewiesen, danach geringe Aktivitaet.
- Reputation: in Privacy-Communities geschaetzt (v. a. wegen der Firewall-Regeln), keine Bundling-Vorwuerfe.

### BleachBit (Shredder/Wipe)

Open-Source-Cleaner mit echtem sicheren Loeschen - der direkteste Funktionsueberschneider mit WinCleaners Bereinigungskern, plus Shredding.

**Feature-Katalog:**
- Datei-Shredding: ueberschreibt Dateiinhalte nach dem Loeschen, um Wiederherstellung zu verhindern.
- Wipe Free Space: ueberschreibt freien Speicherplatz, um Spuren bereits geloeschter Dateien zu beseitigen (loescht keine bestehenden Dateien).
- Kommandozeilen-Schnittstelle (CLI) fuer Scripting/Automation - direkt vergleichbar mit WinCleaners CLI-Ansatz.
- Reinigt Browser-/App-Spuren in tausenden Anwendungen (Chrome, Edge, Firefox, VLC u. v. m.); Cache, Cookies, Verlauf, Temp, Logs.
- Cookie-Manager (ab 5.1.0); selektives Entfernen privater Daten aus SQLite-DBs/Konfigs; Datenbank-Vacuuming (Firefox/Chrome).
- Windows-Registry-Schluessel loeschen; defekte Verknuepfungen entfernen.
- 72 Sprachen; portabler Modus unter Windows; kein Konto noetig.

**Preis & Lizenzmodell:**
- Vollstaendig kostenlos und Open Source (GPL). Keine Bezahl-Stufe.

**Tech/UX:**
- GUI UND CLI; portabel.
- Plattform: Windows und Linux.
- Open Source; ausdruecklich frei von Adware/Spyware/Telemetrie/Bloatware/Backdoors/Toolbars (Eigenangabe, durch offenen Code pruefbar).
- Aktuell gepflegt: Version 5.0.0 (Mai 2025). Reputation hoch; gilt als seriose CCleaner-Alternative.
- Einschraenkung: Shredding/Wipe auf SSDs nur "best effort" (siehe Hinweis oben).

### Eraser

Spezialist ausschliesslich fuer sicheres Loeschen auf Datei-/Freispeicher-Ebene.

**Feature-Katalog:**
- Sicheres Loeschen durch mehrfaches Ueberschreiben mit ausgewaehlten Mustern.
- Mehrere Loeschstandards: Gutmann (35 Durchgaenge), US DoD 5220.22-M, Pseudozufalls-Ueberschreibung u. a.
- Loescht Reste geloeschter Dateien, freien Speicher, sowie NTFS-MFT/MFT-residente Dateien und FAT-Verzeichnisindizes.
- Flexibler Scheduler (geplantes Wischen) - konzeptionell vergleichbar mit WinCleaners schedule-clean.
- Integration ins Kontextmenue des Explorers; portabler Modus moeglich.
- Reines Loesch-Tool - KEINE Telemetrie-/Privacy-Tweaks, kein Browser-Cleaning.

**Preis & Lizenzmodell:**
- Kostenlos und Open Source (GPLv3).

**Tech/UX:**
- GUI (plus Explorer-Kontextmenue); kein vollwertiges CLI.
- Plattform: nur Windows.
- Open Source.
- Wartung: Kernarchitektur seit 6.2 (2018) weitgehend stabil/stagnierend; Build 6.2.0.2996 aus 2025 dokumentiert, aber kaum Weiterentwicklung. Reputation solide, aber als "veraltet" wahrgenommen.
- Wichtige Limitierungen: kann SSDs nicht zuverlaessig loeschen (kein Firmware-Secure-Erase), kann keine kompletten Laufwerke wischen, keine Compliance-Dokumentation. Die Standards DoD 5220.22-M und Gutmann gelten fuer moderne Laufwerke als ueberholt.

### Windows Storage Sense / Disk Cleanup (cleanmgr) - Baseline

Die kostenlosen Bordmittel, die WinCleaner als Mindestmassstab schlagen oder ergaenzen muss. KEINE Privacy/Telemetrie- oder Shredder-Funktion.

**Feature-Katalog Storage Sense:**
- Automatisierte, zeitgesteuerte Bereinigung (taeglich/woechentlich/monatlich oder bei knappem Speicher).
- Loescht Temp-Dateien (User- und Windows-Temp), leert Papierkorb nach konfigurierbarem Alter, raeumt Downloads-Ordner (optional), bereinigt OneDrive-Cloud-Cache (dehydriert), Thumbnails, Delivery-Optimization-Cache.
- Laeuft nur auf dem Systemlaufwerk. In den Einstellungen unter System > Speicher konfigurierbar.

**Feature-Katalog Disk Cleanup (cleanmgr.exe):**
- Erfasst Temp-Dateien, Papierkorb, Thumbnails, Windows-Update-Bereinigung.
- Mit "Systemdateien bereinigen" (Elevation): vorherige Windows-Installationen (Windows.old), Komponentenspeicher (WinSxS), Update-Caches, Delivery-Optimization-Dateien.
- Skriptbar ueber cleanmgr /sageset:n und /sagerun:n (CLI-Automation moeglich).
- In Windows 11 ergaenzt durch "Bereinigungsempfehlungen".

**Preis & Lizenzmodell:**
- Kostenlos, Bestandteil von Windows.

**Tech/UX:**
- GUI (Settings/cleanmgr) plus eingeschraenkte CLI (cleanmgr-Switches, Storage Sense via Policy/Registry/Task Scheduler automatisierbar).
- Plattform: Windows 10/11. Proprietaer (Microsoft).
- Kein Adware-/Bundling-Risiko; Standard-Vertrauensbasis. Standardeinstellungen reinigen allerdings eher konservativ ("fast nutzlos" laut Kritik), keine Browser-/App-Spuren, kein sicheres Loeschen.

### Vergleichs-Feature-Matrix

| Tool | Telemetrie/Tracking aus | KI/Copilot/Recall aus | Telemetrie-IP-Blocking (FW/HOSTS) | Appx-/Bloatware entfernen | Sicheres Loeschen (Shred) | Free-Space-Wiping | Browser-/App-Spuren | CLI | GUI | Open Source | Preis |
|---|---|---|---|---|---|---|---|---|---|---|---|
| O&O ShutUp10++ | Ja | Ja | Nein | Nein | Nein | Nein | Nein | Nein | Ja | Nein | Free; Premium ab ~14,90-19,90 EUR (Einmalkauf, Stand pruefen) |
| W10Privacy | Ja (100+) | Teilw. | Ja (beide) | Teilw. | Nein | Nein | Nein | Nein | Ja | Nein | Kostenlos |
| Privatezilla | Ja (~60) | Nein (veraltet) | Ja (beide) | Ja (Packages) | Nein | Nein | Teilw. (3rd-Party-Telemetrie) | PowerShell | Ja | Ja (MIT) | Kostenlos |
| WPD | Ja | Teilw. | Ja (Firewall: Spy/Update/Extra) | Ja (Store-Apps) | Nein | Nein | Nein | Ja (Switches) | Ja | Ja (Status pruefen) | Kostenlos |
| BleachBit | Nein | Nein | Nein | Nein | Ja | Ja | Ja (tausende Apps) | Ja | Ja | Ja (GPL) | Kostenlos |
| Eraser | Nein | Nein | Nein | Nein | Ja (Gutmann/DoD) | Ja | Nein | Nein | Ja | Ja (GPLv3) | Kostenlos |
| Storage Sense / Disk Cleanup | Nein | Nein | Nein | Nein | Nein | Nein | Nein | Teilw. (cleanmgr/Task) | Ja | Nein | Kostenlos (Bordmittel) |

Fazit fuer WinCleaner: In dieser Kategorie existieren zwei klar getrennte Saeulen. Privacy/Telemetrie wird von ShutUp10++/W10Privacy/WPD/Privatezilla dominiert (alle GUI, mehrheitlich kostenlos), sicheres Loeschen von BleachBit/Eraser. BleachBit ist als CLI-faehiges, plattformuebergreifendes Open-Source-Tool der direkteste Konkurrent zu WinCleaners Architektur und kombiniert Cleaning + Shredding + Wipe in einem - genau die Bruecke, die WinCleaner schlagen koennte. Eine CLI-first-Loesung fuer Telemetrie-Tweaks (per Registry/Policy/Dienste/Task - alles Mechanismen, die WinCleaner bereits teilweise nutzt) hat im Markt kaum Konkurrenz, da alle Privacy-Tools GUI-only sind. Das ist die staerkste Differenzierungschance.

> **Faktencheck-Korrekturen (Stand 2026):**
- **WebSearch + WebFetch (bleachbit.org/download, 9to5Linux, BetaNews, Linuxiac, PortableApps.com, GitHub releases)**: ~~BleachBit: 'Aktuell gepflegt: Version 5.0.0 (Mai 2025).' Ausserdem im Feature-Katalog: 'Cookie-Manager (ab 5.1.0)'.~~ -> Veraltet. Aktuelle stabile Version ist BleachBit 6.0.0 (Release April/Mai 2026) - laut Projekt 'biggest release in years'. 5.0.0 stammt aus 2023, 5.0.2 war eine spaetere 5.x-Pflege; 'Mai 2025' trifft fuer keine aktuelle Version zu. Der Cookie-Manager wurde zwar in 5.1.0 Beta eingefuehrt, ist aber erst mit dem Stable-Release 6.0.0 final ausgeliefert worden - die Angabe 'ab 5.1.0' ist daher nur fuer die Beta korrekt, fuer Stable-Nutzer gilt 6.0.0. In der Matrix-Spalte 'Aktuell gepflegt' auf 6.0.0 (2026) aktualisieren.
- **WebFetch (wpd.app), WebSearch (Portable Freeware Collection, Softpedia)**: ~~WPD: 'Als Open Source bezeichnet (Quellen widerspruechlich)' bzw. Matrix-Eintrag 'Open Source: Ja (Status pruefen)'.~~ -> WPD ist NICHT Open Source. Mehrere Verzeichnisse (Portable Freeware Collection, Softpedia) fuehren WPD ausdruecklich als proprietaer/Freeware (closed source), die offizielle Seite wpd.app nennt keinerlei Open-Source-Lizenz/Quellcode. Der Matrix-Eintrag sollte 'Nein' lauten statt 'Ja (Status pruefen)'; die Formulierung 'als Open Source bezeichnet' ist irrefuehrend. (Stand 2026.) Korrekt ist hingegen der Discontinued-Hinweis: letzte Version 1.5.2042 RC 1, Copyright 2016-2022, kein Stable-Release danach.

