# WinCleaner — Anleitung (Deutsch)

WinCleaner ist ein Kommandozeilen-Werkzeug für Windows, das deinen PC aufräumt
und wartet: Datenmüll löschen, Speicherfresser finden, doppelte Dateien
aufspüren, Autostart entschlacken, Bloatware entfernen, Privatsphäre-
Einstellungen setzen und Software aktuell halten — alles ohne Werbung,
Telemetrie oder Hintergrunddienst.

## Starten

WinCleaner ist unter `%LOCALAPPDATA%\Programs\WinCleaner` installiert und im
PATH eingetragen. Öffne ein **Terminal** (Windows-Taste → „Terminal" oder
„PowerShell") und tippe:

```
WinCleaner help
```

Das zeigt alle Befehle. Hilfe zu einem einzelnen Befehl:

```
WinCleaner clean-junk --help
```

## Am einfachsten: die grafische Oberfläche

Seit **v2.0.0** gibt es ein echtes Fenster-Programm — kein Terminal. Starte es
über die Verknüpfung **„WinCleaner (GUI)"** auf dem Desktop bzw. im Startmenü
(oder `WinCleanerGui.exe` im Installationsordner). Links die Bereiche
(Übersicht, Aufräumen, Speicher, Programme, Autostart & Dienste, Privatsphäre,
Sicher löschen, System), rechts der Inhalt.

Bedienung wie erwartet: **erst scannen** (zeigt nur an, was passieren würde),
Häkchen setzen, **bereinigen** — und vor jeder löschenden Aktion kommt eine
Rückfrage. Gelöschtes geht in den Papierkorb, Systemänderungen sind umkehrbar.
Nur der rot markierte Bereich „Sicher löschen" ist endgültig.

**An die Taskleiste anheften:** GUI starten, dann Rechtsklick auf das
WinCleaner-Symbol in der Taskleiste → „An Taskleiste anheften".

## Auch möglich: das Menü im Terminal

Wer sich keine Befehle merken will, tippt einfach:

```
WinCleaner menu
```

Dann erscheint ein Menü — Aufgabe per Zahl auswählen, fertig. Löschende
Aktionen zeigen erst einen Probelauf und fragen dann nach. Noch bequemer:
im Installationsordner (`%LOCALAPPDATA%\Programs\WinCleaner`) liegt
**`WinCleaner-Menue.cmd`** — per Doppelklick öffnet sich das Menü direkt.

## Das Wichtigste zuerst: Nichts passiert ohne dein Okay

WinCleaner ist absichtlich vorsichtig gebaut:

1. **Probelauf ist Standard.** Jeder Befehl, der etwas löscht oder ändert,
   zeigt zuerst nur an, *was* er tun würde. Erst mit `--no-dry-run` passiert
   es wirklich — und auch dann fragt WinCleaner noch einmal nach
   (überspringen mit `--yes`).
2. **Gelöscht wird in den Papierkorb**, nicht endgültig. Du kannst alles
   zurückholen.
3. **Systemänderungen sind umkehrbar.** Privacy-Tweaks, Dienste und
   Autostart-Einträge lassen sich mit `--undo` bzw. erneutem Aufruf
   rückgängig machen; vor großen Eingriffen wird ein
   Wiederherstellungspunkt erstellt.
4. **Einzige Ausnahmen:** `shred` und `wipe-free-space` löschen absichtlich
   endgültig — das steht groß dabei und passiert nie aus Versehen.

## Typische Aufgaben

### PC aufräumen (Datenmüll löschen)

```
WinCleaner scan-junk              # Was liegt an Müll herum? (nur anzeigen)
WinCleaner clean-junk             # Probelauf: was würde gelöscht?
WinCleaner clean-junk --no-dry-run   # Wirklich löschen (in den Papierkorb)
```

### Was frisst meinen Speicherplatz?

```
WinCleaner analyze-disk C:\Users\jelec           # größte Ordner/Dateien
WinCleaner analyze-disk D:\ --by-type            # nach Dateityp gruppiert
WinCleaner analyze-disk D:\ --min-size 500MB     # nur große Brocken
```

### Speicherwachstum über die Zeit verfolgen

```
WinCleaner analyze-disk D:\ --snapshot vorher.json    # Zustand festhalten
# ... Wochen später ...
WinCleaner analyze-disk D:\ --snapshot nachher.json
WinCleaner disk-diff vorher.json nachher.json         # Was ist gewachsen?
```

### Doppelte Dateien finden und aufräumen

```
WinCleaner find-duplicates D:\Fotos                          # nur anzeigen
WinCleaner find-duplicates D:\Fotos --delete --keep oldest   # Probelauf
WinCleaner find-duplicates D:\Fotos --delete --keep oldest --no-dry-run
```

Mit `--protect <Ordner>` wird ein Referenzordner nie angerührt; `--hard-link`
spart Platz ohne etwas zu löschen. `--cache` merkt sich berechnete Hashes und
macht Wiederholungsläufe über große Ordner deutlich schneller.

### Windows schneller starten lassen

```
WinCleaner startup-list                # Was startet alles mit?
WinCleaner startup-disable Spotify     # Eintrag abschalten (umkehrbar)
```

### Software aktuell halten

```
WinCleaner list-updates                # Welche Updates gibt es?
WinCleaner update --no-dry-run         # Alle Pakete aktualisieren
WinCleaner schedule-update weekly      # ... automatisch jede Woche
```

### Privatsphäre verbessern

```
WinCleaner scan-privacy                # Ist-Zustand ansehen (ändert nichts)
WinCleaner privacy --apply standard    # empfohlene Tweaks setzen (umkehrbar)
WinCleaner privacy --undo              # alles wieder zurück
```

`--apply advanced` schaltet zusätzlich Cortana, Standortverlauf u. a. ab.
`block-telemetry --apply` blockt Microsoft-Telemetrie-Server über die
hosts-Datei (umkehrbar mit `--undo`).

Windows-Updates setzen einzelne Schalter gern wieder zurück — dagegen hilft:

```
WinCleaner schedule-privacy weekly     # Tweaks jede Woche automatisch neu anwenden
```

### Vorinstallierten Ballast entfernen

```
WinCleaner debloat --list              # Welche Apps würden entfernt?
WinCleaner debloat --no-dry-run        # Entfernen (aus dem Store reinstallierbar)
```

### Programme deinstallieren

```
WinCleaner list-programs               # Alles Installierte auflisten
WinCleaner uninstall "AlteApp" --no-dry-run
```

WinCleaner erstellt vorher einen Wiederherstellungspunkt und bietet danach an,
Überbleibsel (Ordner, Registry-Reste) mit zu entfernen — nur nach separater
Rückfrage.

### Automatische Wartung einrichten

```
WinCleaner create-restore-point        # Sicherungspunkt (braucht Admin)
WinCleaner schedule-clean weekly       # Junk-Bereinigung jede Woche um 3:00
WinCleaner schedule-update weekly      # Software-Updates jede Woche
```

## Für Skripte und Profis

- `--json` liefert bei den meisten Lese-Befehlen maschinenlesbare Ausgabe;
  Log-Meldungen gehen nach **stderr**, die Daten bleiben sauber:
  `WinCleaner scan-junk --json | jq .TotalBytes`
- Exit-Code `0` = Erfolg, ungleich `0` = Fehler — ideal für Automatisierung.
- Befehle, die Adminrechte brauchen (Wiederherstellungspunkt, systemweite
  Tweaks, hosts-Datei), fordern die UAC-Erhöhung selbst an.

## Häufige Fragen

**Kann ich etwas kaputt machen?**
Kaum. Ohne `--no-dry-run` wird nie etwas verändert, Gelöschtes landet im
Papierkorb, Tweaks haben `--undo`, und vor großen Eingriffen entsteht ein
Wiederherstellungspunkt. Nur `shred`/`wipe-free-space` sind endgültig.

**Warum meckert der Befehl „unbekannte Option"?**
Absichtlich: Vertippte Flags werden abgelehnt statt ignoriert, damit ein
falsch geschriebenes `--no-dry-run` nicht unbemerkt untergeht.

**Wo ist die grafische Oberfläche?**
Es gibt bewusst keine. WinCleaner ist ein schlankes Terminal-Werkzeug —
skriptbar, planbar, ohne Hintergrundprozesse.

**Neu bauen nach Code-Änderungen:**

```
dotnet publish WinCleaner\WinCleaner.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ..\dist
```

Die vollständige Befehlsreferenz steht in der [README](README.md) und in
`WinCleaner help`.
