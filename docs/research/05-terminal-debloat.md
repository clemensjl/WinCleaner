# Terminal- & Debloat-Tools (Power-User)

Diese Kategorie umfasst Open-Source-Werkzeuge fuer Power-User, die Windows entschlacken (Debloat), Telemetrie/Privacy steuern, Registry-/System-Tweaks anwenden und Software per Paketmanager verwalten. Der gemeinsame Nenner ist hohe Skriptbarkeit (PowerShell), Appx-Bloatware-Removal und Reversibilitaet. WinCleaner steht dieser Kategorie am naechsten von allen analysierten Segmenten, deckt aber den Debloat-/Tweak-/Privacy-Kern bisher gar nicht ab. Hinweis Datenstand: Preise/Versionen Stand Juni 2026; bei einzelnen Werten "Stand pruefen" beachten.

---

### Chris Titus Tech WinUtil (CTT WinUtil)

Der De-facto-Standard unter den Community-Debloat-Tools (ueber 30 Mio. Ausfuehrungen, 200+ Contributors). Eine WPF-GUI, die zur Laufzeit per PowerShell-One-Liner geladen wird.

**Feature-Katalog:**
- **Installs**: Bulk-Software-Installation per `winget` (und Choco-Fallback) ueber kuratierte App-Liste (Hunderte Programme, Mehrfachauswahl).
- **Tweaks**: Zwei Stufen — "Standard/Essential Tweaks" (sicher, reversibel) und "Advanced Tweaks" (fuer Power-User). Deaktiviert Telemetrie, Activity History, Consumer-Features, GameDVR, Hibernation etc.
- **Privacy**: Telemetrie aus, Werbe-ID/Targeted Ads aus, Tracking-Dienste reduziert; reduziert laufende Prozesse typ. auf 70-80.
- **Reversibilitaet**: Erstellt automatisch einen Systemwiederherstellungspunkt vor Aenderungen; "Undo Selected Tweaks" macht gezielt einzelne Tweaks rueckgaengig.
- **Config**: Troubleshooting-/Fix-Funktionen, Windows-Update-Policy-Steuerung (z. B. Updates pausieren/Security-only).
- **MicroWin / "Win11 Creator"**: Erzeugt aus einer offiziellen Windows-11-ISO ein entschlacktes, angepasstes Custom-Image (nutzt DISM fuer Image-Servicing) — entfernt AI-Integration, Bloatware, vorinstallierte Apps.
- **Dienste-Verwaltung**: Setzt Windows-Dienste auf Manual/Disabled gemaess Tweak-Profilen.
- **Presets**: Standard / Minimal / Advanced als vordefinierte Tweak-Sets.

**Preis & Lizenz:** Kern kostenlos, **MIT-Lizenz**, voll Open Source. Optionaler kompilierter EXE-Wrapper als Bezahl-Variante (ca. 10 USD, Stand pruefen) — die PowerShell-Variante bleibt gratis. Kein Abo, keine Geraetelimits.

**Tech/UX:** GUI (WPF), wird aber als PowerShell geladen (`irm christitus.com/win | iex`); benoetigt Admin (UAC). Nur Windows (10/11). Open Source (GitHub: ChrisTitusTech/winutil). Keine Telemetrie/Adware/Bundling — Code oeffentlich pruefbar. Sehr hohe Reputation; Standardempfehlung in der Community. Nutzt vertrauenswuerdige Systemtools (DISM, winget) statt Eigen-Hacks wo moeglich.

---

### Win11Debloat (Raphire)

Schlankes, skriptzentriertes PowerShell-Tool fuer Windows 10 **und** 11, beliebt fuer Automatisierung/Deployment. Sehr aktive Releases (mehrere 2025/2026).

**Feature-Katalog:**
- **Appx-/App-Removal**: Entfernt vorinstallierte Apps (Microsoft, OEM wie HP, Drittanbieter wie TikTok) ueber Appx **und** WinGet. Gezielte Einzelauswahl per `-RemoveApps`/`-Apps` (Komma-Liste); Ziel per `-AppRemovalTarget` (alle User / bestimmter User / `CurrentUser`).
- **Privacy/Telemetrie**: Telemetrie, Diagnosedaten, Activity History, App-Launch-Tracking, Targeted Ads, Standortdienste, Find My Device, MSN-Feed, M365-Werbung abschalten.
- **AI-Removal**: Copilot, Windows Recall, Click to Do entfernen/deaktivieren; AI in Edge/Paint/Notepad aus.
- **System-Tweaks**: Kontextmenue (Win10-Stil) wiederherstellen, Mausbeschleunigung, BitLocker-Auto-Encryption aus, Fast Startup, Storage Sense, Delivery Optimization.
- **UI-Tweaks**: Taskbar-Ausrichtung/Widgets, Start-Pins, Suche, Explorer-Defaults, versteckte Dateien, Dark Mode, Animationen.
- **Deployment**: Silent/CLI-Parameter, Sysprep-Modus fuer neue Userprofile, laeuft unter SYSTEM-Konto (fuer Automation/Intune/Imaging).

**Preis & Lizenz:** Voll kostenlos, **MIT-Lizenz**, Open Source. Keine Limits.

**Tech/UX:** Hybrid — interaktives Menue (TUI/menugefuehrt) **und** vollwertige CLI-Parameter; keine Installation noetig (portables Skript). Windows 10 + 11. Open Source (GitHub: Raphire/Win11Debloat). Keine Telemetrie/Bundling. **Reversibilitaet**: dokumentierte Anleitungen zum Zuruecksetzen der Aenderungen. Hohe Reputation, oft als "der bessere Skript-Ansatz fuer Techniker" empfohlen.

---

### ThisIsWin11 / BloatyNosy (builtbybel)

ThisIsWin11 ist der aeltere GUI-Debloater desselben Entwicklers; der aktive Nachfolger ist **BloatyNosy** (auch "Bloatynosy"). ThisIsWin11 gilt als veraltet und ohne moderne Plugin-Architektur.

**Feature-Katalog (BloatyNosy):**
- **"Experience"**: Sammel-Interventionen — Telemetrie aus, Werbeinhalte ausblenden, optionale Features wie Recall abschalten, Edge-Komponenten reduzieren.
- **"Dumputer"**: Entfernt vorinstallierte Apps inkl. Eintraegen, die Windows normalerweise schuetzt.
- **Plugin-Engine + Plugin-Store**: Kuratierte Verhaltensweisen nachladbar (z. B. "New Outlook"-Preinstall blocken, Bulk-winget-Installs) — erlaubt schnelle Anpassung an Microsofts wechselnde Defaults.
- **Settings-Tweaks**: mehrere Systemeinstellungen gleichzeitig anpassen.

**Preis & Lizenz:** Kostenlos, Open Source. (Lizenz typ. MIT — Stand pruefen.)

**Tech/UX:** GUI, portabel (kein Install noetig). Nur Windows 11 (BloatyNosy). Open Source (GitHub: builtbybel). Staerke = klare Oberflaeche + Plugin-Flexibilitaet; Schwaeche = ThisIsWin11 selbst wird nicht mehr gepflegt. Reputation solide, aber kleiner als WinUtil/Win11Debloat.

---

### Sophia Script for Windows (farag2)

"Der maechtigste Open-Source-Tweaker auf GitHub" — ein sehr granulares PowerShell-Modul fuer Feintuning, eher fuer erfahrene Power-User als fuer Ein-Klick-Nutzer.

**Feature-Katalog:**
- **150+ einzelne Funktionen**, jede mit Gegenfunktion zum Wiederherstellen der Standardwerte.
- **Privacy & Security**, Telemetrie/Diagnosedaten-Management, Windows-AI-Konfiguration.
- **UWP/Appx-Deinstallation** (mit lokalisierten App-Namen, dynamische Liste installierter Apps).
- **OneDrive-Entfernung**, **DNS-over-HTTPS** Setup (Cloudflare, Google, Quad9, AdGuard, Comss.one).
- **Scheduled Tasks** erstellen (z. B. Windows-Cleanup, Temp-Datei-Loeschung als geplanter Task).
- **Personalisierung/UI**: Explorer- und Kontextmenue-Tweaks, User-Ordner-Verschiebung, Win11-Cursor.
- **Runtimes installieren**: Visual C++ Redistributables, .NET-Runtimes.

**Preis & Lizenz:** Kostenlos, **MIT-Lizenz**, Open Source. Distribution auch ueber Chocolatey/Scoop/WinGet.

**Tech/UX:** Reines **PowerShell/CLI** (kein eingebautes GUI). **Reversibilitaet**-Modell: Nutzer editieren das `Sophia.ps1`-Skript direkt und kommentieren ungewollte Funktionen aus; pro Tweak existiert eine Restore-Funktion (aber kein automatischer Massen-Rollback/Export-Preset). Breiter OS-Support: Windows 10 (22H2, 21H2/1809 LTSC), Windows 11 (25H2+, 24H2 LTSC), ARM64. **SophiApp** (C#/WinUI 3) ist die separate GUI-Variante. Open Source (GitHub: farag2/Sophia-Script-for-Windows). Keine Telemetrie/Bundling. Sehr hohe Reputation bei Profis; nutzt explizit nur von Microsoft dokumentierte Wege.

---

### UniGetUI (frueher WingetUI)

Kein Debloater, sondern der fuehrende GUI-Wrapper um Windows-Paketmanager — relevant fuer die Felder Software-Updates, Paketmanagement und Deinstallation.

**Feature-Katalog:**
- **Mehrere Paketmanager unter einer Oberflaeche**: WinGet, Scoop, Chocolatey, Pip, Npm, Bun, .NET Tool, PowerShell Gallery (und mehr).
- **1-Klick Install / Update / Uninstall**; Suche + Filter mit detaillierten Metadaten ueber alle Manager.
- **Bulk-Operationen**: mehrere Pakete gleichzeitig installieren/aktualisieren/entfernen.
- **Auto-Updater** mit Versions-Skipping und Per-Paket-Ignore.
- **Backup/Restore von Paketlisten** (Export/Import) fuer Migration auf neue Maschinen.
- **System-Tray-Integration** + Benachrichtigungen ueber verfuegbare Updates.
- **Custom-Install-Parameter**, Architekturauswahl, Paket-Sharing per Link.

**Preis & Lizenz:** Kostenlos, **MIT-Lizenz**, Open Source. Keine Limits.

**Tech/UX:** GUI (Wrapper um CLI-Paketmanager). Windows 10 + 11. Open Source (GitHub: Devolutions/UniGetUI; urspruenglich Marti Climent, seit 2026 von **Devolutions** gepflegt, bleibt community-driven). Keine Adware/Bundling. Sehr hohe Reputation, oft als "Windows braucht das"-Empfehlung.

---

### Talon (Raven Dev Team) — "2-Klick-Debloater"

Aggregat-Tool, das etablierte Skripte (CTT WinUtil, Raphire Win11Debloat) zu einem Ein-/Zwei-Klick-Prozess buendelt; Fokus auf Einsteiger-Tauglichkeit.

**Feature-Katalog:**
- **Bulk-Debloat in 2 Klicks**: entfernt eingebettete AI, Telemetrie/Spying, deaktiviert unnoetige Hintergrundprozesse/Dienste.
- **AI-Removal** ueber Copilot/Recall hinaus (modernere AI-Integrationen), entfernt Store-Suche aus der Windows-Suchleiste.
- **Browser-Install** ueber Chocolatey (statt winget, wegen besserer Zuverlaessigkeit).
- Kombiniert kuratierte Tweaks aus WinUtil + Win11Debloat.

**Preis & Lizenz:** Kostenlos, **BSD-3-Clause**, Open Source (GitHub-Mirror read-only; Issues ueber Feedback-Formular).

**Tech/UX:** GUI (QML + Python); Start per `irm debloat.win | iex`, EXE-Download oder Selbst-Kompilieren (Python 3.12.4). Primaer Windows 11. **Wichtige Einschraenkungen**: "run and done"-Tool — vor allem fuer **Frischinstallationen** gedacht; erneutes Ausfuehren kann schaden; manche Tweaks werden zwar als reversibel gefuehrt, koennen aber nach grossen Windows-Updates zurueckfallen. Gelegentliche AV-False-Positives. Reputation gut, aber mit deutlichen "Vorsicht"-Hinweisen der Community.

---

### AME / Ameliorated (AME Wizard + Playbooks)

Der radikalste Ansatz der Kategorie: ein "Playbook"-Runner, der Windows tiefgreifend de-amerikanisiert/entschlackt (entfernt Komponenten, die andere Tools nur deaktivieren).

**Feature-Katalog:**
- **AME Wizard** fuehrt **Playbooks** (modulare Drag-&-Drop-Instruktionssets) aus; das offizielle "AME Play"/Performance-Playbook entfernt Bloatware, deaktiviert Tracking und tunt Defaults aggressiv.
- Tiefe Entfernung von Telemetrie/Komponenten, nicht nur Deaktivierung.

**Preis & Lizenz:** AME Wizard teils Open Source, **teils Closed Source** und durch das ameliorated.io-Team gegated (Verifizierung der Playbooks + Auto-Updates). Sofort verifizierte Playbooks/Updates per **Patreon ca. 10 USD/Monat** (Stand pruefen). Das ist das einzige Tool der Kategorie mit Bezahl-/Abo-Komponente.

**Tech/UX:** GUI-Wizard + Playbook-Dateien. Nur Windows. **Kontroverse**: Closed-Source-Anteile + Monetarisierung der Verifizierung; Windows Defender kann AME Wizard Beta als verdaechtig melden (False Positive). Tiefe, oft **schwer/nicht reversible** Eingriffe — nur fuer Frischinstallationen empfohlen. Reputation gemischt: technisch anerkannt, aber Vertrauensdebatten (Transparenz). Eher Referenz fuer "was nicht zu WinCleaners sicherem, reversiblem Ansatz passt".

---

### "debloat-Skripte allgemein" (Windows10Debloater u. a.)

Sammelbegriff fuer aeltere/generische PowerShell-Debloat-Skripte (z. B. Sycnex Windows10Debloater, diverse GitHub-Gists).

**Feature-Katalog (typisch):**
- Appx-Bulk-Removal per `Get-AppxPackage | Remove-AppxPackage`.
- Telemetrie-/Dienste-Deaktivierung per Registry-Edits.
- OneDrive/Cortana-Entfernung; teils interaktiver oder "silent"-Modus.

**Preis & Lizenz:** Kostenlos, meist MIT, Open Source.

**Tech/UX:** Reine CLI/PowerShell, oft ohne GUI. **Risiko**: viele sind unmaintained, ohne Restore-Mechanik, ohne Safety-Klassifizierung — koennen System destabilisieren. Reputation stark abnehmend zugunsten der gepflegten Tools (WinUtil/Win11Debloat/Sophia). Lehre fuer WinCleaner: Reversibilitaet + Safety-Rating sind genau die Differenzierung, die diese Skripte vermissen lassen.

---

### Vergleichs-Feature-Matrix

| Tool | Appx/App-Removal | Privacy/Telemetrie | Registry-/System-Tweaks | Paket-/App-Install | Reversibilitaet | CLI / GUI | Open Source / Lizenz | Preis |
|---|---|---|---|---|---|---|---|---|
| **CTT WinUtil** | Ja (winget/appx) | Ja | Ja (Standard+Advanced) | Ja (winget bulk) | Restore-Point + Undo Selected | GUI (per PS geladen) | Ja / MIT | Frei; EXE-Wrapper ca. 10 USD |
| **Win11Debloat** | Ja (appx + winget, gezielt) | Ja (umfangreich) | Ja | Teil (winget-Removal) | Dokumentierte Reverts | CLI + Menue-TUI | Ja / MIT | Frei |
| **ThisIsWin11 / BloatyNosy** | Ja (auch geschuetzte) | Ja | Ja (Settings) | Plugin (Bulk winget) | begrenzt | GUI + Plugins | Ja (MIT, Stand pruefen) | Frei |
| **Sophia Script** | Ja (UWP, lokalisiert) | Ja (sehr granular) | Ja (150+ Funktionen) | Runtimes (VC++/.NET) | Restore-Fn je Tweak (Skript-Edit) | CLI (PowerShell); SophiApp=GUI | Ja / MIT | Frei |
| **UniGetUI** | Uninstall via Manager | Nein | Nein | Ja (8+ Manager, bulk, auto-update, backup) | n/a | GUI (CLI-Wrapper) | Ja / MIT | Frei |
| **Talon** | Ja (Bulk) | Ja | Ja (kuratiert) | Browser via Choco | teilw., bricht nach Updates | GUI (QML/Python) | Ja / BSD-3 | Frei |
| **AME / Ameliorated** | Ja (tiefe Entfernung) | Ja (aggressiv) | Ja (Playbooks) | tlw. | schlecht/nicht | GUI-Wizard + Playbooks | Teilweise (gated) | Frei + Patreon ca. 10 USD/Mon |
| **Generische Debloat-Skripte** | Ja (appx bulk) | Ja | Ja (Registry) | selten | meist keine | CLI/PowerShell | Ja (meist MIT) | Frei |
| **WinCleaner (IST)** | **Nein** | **Nein** | **Nein** | **Nein** (nur Junk/Disk) | Papierkorb + reversibler Startup-Disable + Restore-Point | **CLI** (.NET 9, DE) | Ja (eigenes Repo) | Frei |

**Kerneinsicht fuer WinCleaner:** Die gesamte Kategorie lebt von Debloat + Privacy + Tweaks + Paketmanagement — WinCleaner hat davon aktuell **null**. Gleichzeitig besitzt WinCleaner Eigenschaften, die fast alle Tools hier vermissen lassen: konsequenter **Dry-Run-Default**, **Safety-Klassifizierung** (nur "Safe" wird geloescht), Papierkorb statt Hard-Delete und sauberes **`--json`** fuer Automation. Diese Disziplin ist die Differenzierung — neue Debloat-/Tweak-Features sollten dasselbe Reversibilitaets-/Safety-Modell erben (Restore-Point vor Eingriff, Undo, Dry-Run), statt die Risiken der "run and done"-Skripte zu kopieren.
