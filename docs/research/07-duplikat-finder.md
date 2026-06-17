# Duplikat-Finder

Diese Kategorie vergleicht spezialisierte Duplikat-Finder mit dem WinCleaner-Befehl `find-duplicates`. WinCleaner nutzt heute ein dreistufiges, rein byte-/hash-basiertes Verfahren (Groessen-Gruppierung -> Partial-Hash der ersten 4 KB -> voller SHA-256) und loescht je Gruppe alle Dateien ausser der ersten. Es gibt **keine Vorschau, keine Aehnlich-Erkennung (Bild/Audio), keine Hardlink-Option, keine Auswahl-Strategie und keine Papierkorb-Sicherung beim Loeschen von Duplikaten** (`File.Delete` direkt). Die folgenden Tools zeigen, was in dieser Kategorie marktueblich ist.

> Hinweis zu Preisen: Die Hersteller-Preisseiten von DigitalVolcano rendern Betraege per JavaScript und waren nicht direkt auslesbar. Genannte Preise stammen aus Review-Quellen 2025/2026 und sind als "ca., Stand pruefen" zu verstehen.

### dupeGuru

**Feature-Katalog**
- Drei Scan-Modi: **Standard** (beliebige Dateien), **Music** (Audio nach Tags wie Artist/Album/Titel) und **Picture** (Bilder).
- **Fuzzy-Matching**: vergleicht Dateinamen, Groessen und Inhalts-Hashes; Aehnlichkeits-Schwelle (Match-Prozent) einstellbar.
- **Picture-Modus**: erkennt visuell aehnliche Bilder (leicht editiert, neu gespeichert), nicht nur byte-identische.
- **Music-Modus**: gruppiert nach Tags; findet auch dieselben Songs in unterschiedlicher Bitrate/Qualitaet.
- **Reference Folders**: Ordner als "unantastbar" markieren - Dateien dort werden nie zum Loeschen vorgeschlagen.
- Aktionen: Loeschen (in Papierkorb), Verschieben, Umbenennen, **Hardlink** statt Loeschen (auf unterstuetzten Plattformen).
- Konfigurierbare Match-Engine (welche Kriterien greifen), Ergebnis-Vorschau in GUI.

**Preis & Lizenzmodell**
- Vollstaendig **kostenlos**, Open Source unter **GPL-3.0**.
- Keine Trial-Limits, keine Premium-Stufe, keine Geraetebeschraenkung. Freiwillige Spenden moeglich.

**Tech/UX**
- **GUI** (Python 3 + Qt/Cocoa), **kein CLI**.
- Plattform: **Windows, macOS, Linux** (cross-platform aus gemeinsamer Python-Codebasis).
- Open Source: ja. Keine Telemetrie, kein Bundling/Adware bekannt.
- **Reputation/Wartung kritisch**: letztes stabiles Release ca. Juli 2022; gilt als nur noch sehr eingeschraenkt gepflegt (viele offene Issues, Stand Anfang 2026). Funktional weiterhin verbreitet empfohlen.

### czkawka (inkl. Krokiet)

**Feature-Katalog**
- **Duplikate** ueber tatsaechlichen Inhalt (Hash, nicht nur Name); mehrstufiges Hashing fuer Tempo.
- **Similar Images**: erkennt visuell aehnliche Bilder trotz unterschiedlicher Aufloesung, Kompression, Rotation/Crop (perceptual Hashes).
- **Similar Videos** und **Similar/Music** (nach Audio-Inhalt bzw. Tags wie Artist/Album).
- Zusatz-Scanner: leere Ordner, leere Dateien, temporaere Dateien, ungueltige/kaputte Symlinks, grosse Dateien.
- **Sichere Loeschstrategie**: in Papierkorb **oder** permanent; **Duplikate durch Hardlinks oder Symlinks ersetzen** statt loeschen (`--hard-link`).
- **Reference Folder**: markierte Referenzordner werden nie veraendert/geloescht.
- Massiv **multithreaded**, sehr hohe Performance (Beispiel-Review: 160 GB Fotobibliothek in unter 2 Minuten auf SATA-SSD).

**Preis & Lizenzmodell**
- Vollstaendig **kostenlos**, Open Source. CLI und GTK4-GUI unter **MIT**, neue Krokiet-GUI unter **GPL-3.0**. Spenden moeglich.

**Tech/UX**
- **Beides: CLI (`czkawka_cli`) UND GUI** (GTK4 sowie das neuere Slint-basierte "Krokiet"). CLI eignet sich fuer Automatisierung/Scripting.
- Plattform: **Windows, macOS, Linux** (inkl. ARM-Linux ab v10, in Debian 13 enthalten).
- Open Source: ja. Keine Telemetrie/Adware/Bundling. In Rust geschrieben.
- Aktiv gepflegt (v9.0 Maerz 2025, v10.0 August 2025). Gilt als einer der schnellsten Duplikat-Finder. Sehr gute Reputation in der Open-Source-Community.

### Auslogics Duplicate File Finder

**Feature-Katalog**
- Sucht Duplikate nach Dateiname, Groesse, Erstellungsdatum und - per **MD5-Engine - nach Inhalt** (inhaltsgleiche Dateien unabhaengig vom Namen).
- Filter nach Dateityp (Bilder/Video/Audio/Dokumente), Groesse und Datum.
- **Vorschau** der gefundenen Duplikate vor dem Loeschen.
- Loeschen standardmaessig in den **Papierkorb** (Wiederherstellung moeglich).
- **Geplante Scans** und Ausschlusslisten (Schutz wichtiger Ordner).
- Einschraenkung: automatische Vorauswahl der zu loeschenden Duplikate ist im Funktionsumfang begrenzt (manuelle Auswahl ueblich).

**Preis & Lizenzmodell**
- **Kostenlos** (Free-Tool). Kein klassisches Freemium-Limit fuer die Duplikat-Kernfunktion.

**Tech/UX**
- **GUI**, **Windows only**.
- Open Source: **nein** (Closed Source).
- **Bloatware-/Bundling-Risiko**: Installer enthaelt **potenziell unerwuenschte Begleitprogramme desselben Herstellers** (per Decline-Button abwaehlbar); Nutzer muessen im Setup aktiv opt-out. Kein Malware/Adware im engeren Sinn; einzelne AV-Engines melden gelegentlich (vermutlich) False Positives. Telemetrie nicht klar dokumentiert.
- Reputation: solide fuer einfache Exact-Match-Anwendungsfaelle; Installations-Bundling truebt das Bild.

### AllDup

**Feature-Katalog**
- Sehr viele Suchkriterien kombinierbar: Dateiname, Groesse, **Inhalt**, Attribute, Erstell-/Aenderungsdatum, Audio-/Video-Dauer.
- Inhalts-Vergleich wahlweise **byte-by-byte (100 %)** oder per Hash; Hash-Algorithmus waehlbar: **CRC32 (schnellster), MD5 oder SHA-1**.
- **Aehnliche Bilder** ueber mehrere Perceptual-Hash-Verfahren (aHash, bHash, dHash, mHash, pHash); fuer exakt-identische Bilder MD5/SHA.
- **Aehnliche Audiodateien**; Suche nach Dateinamen-Aehnlichkeit.
- **Archiv-Scan** (ZIP, RAR, 7Z).
- **Vorschau** fuer Bild, Text, Audio, Video.
- **Auswahl-Assistent**: regelbasiertes Vorauswaehlen (z. B. "kuerzesten Pfad behalten", "neueste Kopie behalten", "aus Ordner X behalten / aus Ordner Y loeschen").
- **Sichere Loeschstrategien**: Papierkorb, Verschieben in Backup-Ordner, sicheres (Shredder-)Loeschen, Ersetzen durch Verknuepfung **oder Hardlink**.

**Preis & Lizenzmodell**
- **Freeware** (Donationware) fuer Privat- und Firmennutzung, kostenlos. Installierbare Version und **portable Edition** (ebenfalls frei). Spenden erbeten.

**Tech/UX**
- **GUI + Kommandozeilen-Funktion** (CLI laut Handbuch verfuegbar) - eignet sich somit auch fuer Automatisierung.
- Plattform: **Windows only** (Windows 7-11, Server 2008 R2 bis 2025, 32/64-bit).
- Open Source: **nein** (Freeware, Closed Source).
- Kein Bundling/Adware berichtet; portable Variante ohne Installation. Gute Reputation als funktionsreichstes kostenloses Windows-Tool.

### Duplicate Cleaner (DigitalVolcano)

**Feature-Katalog (Pro)**
- Duplikate nach Inhalt/Hash sowie nach erweiterten Kriterien; auch **doppelte Ordner** und Unique-File-Suche.
- **Aehnliche Bilder** auch bei Bearbeitung, Rotation, Spiegelung, Groessenaenderung.
- **Audio**: Duplikate per aehnlichem Audio, exaktem Match oder Tags (Artist/Titel); viele Formate (MP3, FLAC, WAV, AAC, OGG, WMA, M4A ...).
- **Video**-Duplikat-Erkennung.
- Suche **innerhalb von ZIP-Archiven**, erweiterte Filter/Suchmethoden.
- **Vorschau** von Bildern, Audio und Text vor dem Loeschen.
- **Selection Assistant** (regelbasierte Vorauswahl).
- **Hard-Linking** als Alternative zum Loeschen (fuer Fortgeschrittene).

**Preis & Lizenzmodell**
- **Free Edition**: Basis-Duplikatsuche, nur private/Heimnutzung.
- **Pro Edition**: ca. **39,95 USD** einmalig (Single User), saisonal/Coupon teils ~29,95 USD; einzelne Quellen nennen abweichend bis ~54,95 USD - **Stand pruefen**. **Perpetual-Lizenz** (kein Abo), Updates der 5er-Serie inklusive. Zusaetzlich **Site License** (unbegrenzte Installationen an einem Standort). **7-Tage-Trial** der Pro-Version.

**Tech/UX**
- **GUI**, **Windows only** (auch ueber Microsoft Store).
- Open Source: **nein** (kommerziell/Closed Source).
- Kein Adware-Bundling bekannt; etablierter kommerzieller Anbieter. Aktuelle Version 5.26/5.27 (2025). Gute Reputation, gilt als Funktionsreferenz im Bezahlsegment.

### Anti-Twin

**Feature-Katalog**
- Duplikatsuche per **byte-by-byte-Vergleich** (garantiert identischer Inhalt) - sehr genau fuer Exact-Matches.
- Optionaler **Pixel-Vergleich** fuer aehnliche Bilder.
- Vergleich auch nach **Dateiname**; Filter (Groesse, Typ).
- Schlank, portable, ohne Installation lauffaehig.

**Preis & Lizenzmodell**
- **Kostenlos** (Freeware).

**Tech/UX**
- **GUI**, primaer **Windows** (laut Quellen auch Mac/Linux genannt, faktisch Windows-Fokus).
- Open Source: **nein**.
- **Abandonware**: letztes Release um 2010 (Version ~1.8d), seit ueber einem Jahrzehnt kein Update. Laeuft technisch noch auf aktuellem Windows, aber Pixel-Aehnlichkeit gilt als veraltet/ungenau gegenueber Perceptual-Hash-Tools. Kein Bundling/Adware. Nur fuer einfache Exact-Match-Faelle empfehlenswert.

### Vergleichs-Feature-Matrix

| Tool | Inhalts-/Hash-Vergleich | Byte-genau exakt | Bild-aehnlich (perceptual) | Audio-aehnlich/Tags | Vorschau | Hardlink statt Loeschen | Papierkorb / sicheres Loeschen | Auswahl-Assistent | Performance | CLI | GUI | Plattform | Open Source | Preis |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **WinCleaner** (Ist) | ja (SHA-256, 3-stufig) | ja | nein | nein | nein | nein | nein (direkt-Delete) | nein (1. behalten) | gut (Partial-Hash-Filter) | ja (nur CLI) | nein | Windows | ja (eigenes Projekt) | kostenlos |
| **dupeGuru** | ja (Hash + Fuzzy) | ja | ja (Picture-Modus) | ja (Music-Tags) | ja | ja | Papierkorb | Reference Folders | mittel | nein | ja | Win/Mac/Linux | ja (GPL-3.0) | kostenlos |
| **czkawka** | ja (Hash, multistage) | ja | ja | ja (Audio+Tags) | ja (GUI) | ja (Hard-/Symlink) | Papierkorb + permanent | Reference Folder | sehr hoch (Rust, MT) | ja | ja (GTK4/Krokiet) | Win/Mac/Linux | ja (MIT/GPL-3.0) | kostenlos |
| **Auslogics DFF** | ja (MD5) | ja | begrenzt | nein (Filter) | ja | nein | Papierkorb | begrenzt | mittel | nein | ja | Windows | nein | kostenlos (Bundling im Installer) |
| **AllDup** | ja (CRC32/MD5/SHA-1) | ja (byte-by-byte) | ja (aHash/dHash/pHash ...) | ja | ja | ja | Papierkorb/Backup/Shredder | ja (regelbasiert) | gut | ja | ja | Windows | nein | kostenlos (Donationware) |
| **Duplicate Cleaner Pro** | ja | ja | ja (rotiert/skaliert) | ja (Audio+Tags) | ja | ja | Papierkorb | ja (Selection Assistant) | gut | nein | ja | Windows | nein | ca. 39,95 USD einmalig (Free-Edition vorhanden) |
| **Anti-Twin** | ja (byte-by-byte) | ja | begrenzt (Pixel, veraltet) | nein | begrenzt | nein | Loeschen | nein | gering | nein | ja | Windows | nein | kostenlos (abandonware) |

> **Faktencheck-Korrekturen (Stand 2026):**
- **WebSearch + GitHub crates.io / qarmin/czkawka**: ~~czkawka: 'Aktiv gepflegt (v9.0 Maerz 2025, v10.0 August 2025)' und implizit v10.0 als neueste Version (auch in der Plattform-Notiz 'inkl. ARM-Linux ab v10').~~ -> Veraltet (Stand 2026): Die neueste Version ist czkawka/Krokiet 11.0.1, veroeffentlicht am 21.02.2026 (11.0.0 kurz davor). v10.0 (August 2025) ist nicht mehr die aktuelle Version. Die Aussage 'aktiv gepflegt' bleibt korrekt, aber die genannte neueste Version sollte auf 11.0.x aktualisiert werden. (ARM-Linux-Builds wurden tatsaechlich mit v10.0 eingefuehrt - das stimmt.)
- **WebSearch + auslogics.com / trustradius.com / cisdem.com**: ~~Auslogics Duplicate File Finder: 'Kostenlos (Free-Tool). Kein klassisches Freemium-Limit fuer die Duplikat-Kernfunktion.' und in der Matrix 'Preis: kostenlos (Bundling im Installer)'.~~ -> Falsch/irrefuehrend (Stand 2026): Es existiert ein klares Freemium-Modell mit kostenpflichtiger Pro-Version (Abo, regulaer ca. 29,95 USD/Jahr, oft rabattiert). Hinter der Paywall liegen u.a. die automatische Vorauswahl der zu loeschenden Duplikate, Dateityp-Scans, EXIF/ID3-Scans und das 'Rescue Center'. Die im Entwurf als 'begrenzt' beschriebene Auto-Vorauswahl ist also nicht nur funktional begrenzt, sondern ein Pro-/Bezahlfeature. Die Behauptung 'kein klassisches Freemium-Limit' ist damit unzutreffend; korrekt waere 'Freemium: kostenlose Basisversion, Pro-Abo fuer Auto-Auswahl/erweiterte Scans/Rescue Center'.

