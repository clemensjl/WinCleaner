# Software-Updater & Paketmanager

Diese Kategorie umfasst Werkzeuge, die Drittanbieter-Software automatisiert installieren, aktualisieren und teils deinstallieren. Sie zerfaellt in zwei Lager: echte **Paketmanager** (winget, Chocolatey, Scoop) mit eigenem Repository, Versionsaufloesung und Skript-API sowie reine **Software-Updater** (Patch My PC, Ninite, UCheck, SUMo), die primaer vorhandene Installationen patchen. Fuer WinCleaner sind winget, Chocolatey und Scoop die direkten CLI-Konkurrenten im Sinne von "Konsolen-Tool fuer Windows-Wartung".

Wichtiger Hinweis zur Aufgabenliste: **UCheck stammt von Adlice Software, nicht von Glary/Glarysoft.** Glary bietet ein separates Produkt ("Software Update"). Die Analyse behandelt deshalb das Adlice-UCheck (inkl. CLI-Variante UCheckCMD), das fuer den Vergleich relevanter ist. **SUMo (KC Softwares) ist End-of-Life** (Abschaltung 31.10.2023, Update-Server offline) und wird nur noch als historischer/abkuendigter Vertreter dokumentiert.

---

### winget (Windows Package Manager)

**Feature-Katalog**
- CLI-first: `winget install`, `upgrade`, `uninstall`, `list`, `search`, `show`, `pin`, `export`/`import`, `configure`.
- `winget upgrade --all` aktualisiert alle erkannten Apps; `--silent` unterdrueckt Installer-UI; `--include-unknown` erfasst Apps ohne erkennbare Version.
- Zwei Quellen out of the box: `winget` (Community-Repo) und `msstore` (Microsoft Store) — installiert damit auch Store-Apps per CLI.
- Pinning (Versionen festhalten), private Feeds/Enterprise-Quellen, Locale-Filter.
- YAML-basierte `winget configure` (Desired-State-Konfiguration ganzer Maschinen-Setups).
- Export/Import einer App-Liste als JSON fuer reproduzierbare Neuaufsetzungen.
- Silent-Automatisierung gaengig via `--accept-source-agreements --accept-package-agreements`.
- PowerShell-Modul und COM-API zusaetzlich zur CLI (gut skriptbar/programmatisch einbindbar).

**Preis & Lizenzmodell**
- Vollstaendig kostenlos, Open Source (MIT). Keine Editionen, keine Limits.

**Tech/UX**
- CLI-first; offizielle GUI fehlt (Drittanbieter-GUIs wie "winstall"/"UniGetUI" existieren).
- Plattform: Windows 10, 11, Windows Server 2025. In modernen Windows-Builds vorinstalliert (App Installer).
- Open Source: ja (Client `winget-cli` und Manifest-Repo `winget-pkgs`, beide MIT).
- Repo-Groesse: `winget-pkgs` enthaelt ~30.000+ YAML-Manifeste; mehrere tausend bis ~4.000+ eindeutige Pakete in der Default-Quelle (je nach Zaehlweise; Stand pruefen, Mitte 2025).
- Telemetrie: Microsoft-Diagnosedaten standardmaessig aktiv, abschaltbar (`winget settings`, `--disable-interactivity`/Policy). Datenschutz unkritisch, da MS-Standardrichtlinien.
- Bloatware/Bundling: gering — Pakete kommen aus offiziellen Hersteller-Installern; Manifeste werden moderiert. Restrisiko durch Installer-eigenes Bundling der Hersteller.
- Reputation: sehr hoch, De-facto-Standard und von Microsoft getragen.

---

### Chocolatey

**Feature-Katalog**
- CLI-first: `choco install`, `upgrade`, `uninstall`, `list`, `search`, `pin`, `outdated`.
- `choco upgrade all -y` aktualisiert alle Pakete; Silent-Installation ist Default-Verhalten der Pakete.
- NuGet-basierte Pakete (`.nupkg`) mit PowerShell-Install-Skripten — erlaubt komplexe Installationen, Abhaengigkeiten, Pre/Post-Hooks.
- Eigene interne Repos/Feeds (NuGet-Server, Nexus, ProGet, Artifactory) fuer Enterprise-Verteilung.
- Integration in Konfigurationsmanagement (Ansible, Puppet, Chef, DSC, Intune/SCCM-Skripte).
- C4B-only: Package Builder/Internalizer (automatisches Paketieren), Self-Service-GUI (Chocolatey GUI mit Lizenz), Installations-Reporting, automatische Genehmigung/Quarantaene, Background-Service.

**Preis & Lizenzmodell**
- Open Source: kostenlos (Kernfunktionen, Community-Repo).
- Pro (Individual): ca. 96 USD/Jahr (Stand pruefen).
- Chocolatey for Business (C4B): ca. 17–18 USD pro Node/Jahr, **Mindestabnahme 100 Nodes** (≈1.800 USD/Jahr Einstieg), Abo (Stand pruefen).
- Erweiterte Automatisierungs-/Enterprise-Funktionen liegen hinter der Bezahlschranke (Freemium).

**Tech/UX**
- CLI-first; Chocolatey GUI existiert (Basis kostenlos, viele Komfort-Features lizenzpflichtig).
- Plattform: Windows (PowerShell-basiert); benoetigt fuer viele Pakete Adminrechte.
- Open Source: teilweise — Kern (`choco`) ist Open Source (Apache 2.0), Pro/C4B sind proprietaer.
- Repo-Groesse: Community Repository mit ~11.000 eindeutigen Paketen (264.000 Paketversionen, Milestone Aug. 2025) — groesste kuratierte Windows-Paketsammlung.
- Telemetrie: gering im OSS-Kern; FOSS-Nutzung des Community-CDN ist seit Jahren rate-limitiert (kommerzielle Massennutzung soll C4B verwenden).
- Bloatware/Bundling: gering — Pakete moderiert/virengeprueft; einige Pakete laden Hersteller-Installer mit eigenem optionalem Bundling.
- Reputation: sehr hoch, Enterprise-etabliert, lange Historie.

---

### Scoop

**Feature-Katalog**
- CLI-only: `scoop install`, `update`, `update *` (alle), `uninstall`, `status`, `search`, `bucket add`.
- Installation ins Benutzerverzeichnis (`~/scoop`) — **keine Adminrechte noetig**, kein Eintrag in "Programme & Features", sauberer PATH.
- Dezentrale "Buckets" (Git-Repos mit JSON-Manifesten); Standard ist `main`, weitere wie `extras` manuell hinzufuegbar.
- Manifeste mit Download-URL, Checksummen, Install/Uninstall-Logik, Versions-Aliasse; Maintainer-Tool "Autoupdate" haelt Manifeste aktuell.
- Versionsverwaltung mehrerer parallel installierter Versionen, `scoop hold`/`unhold` (Pin).
- Fokus auf portable/CLI-Tools und Entwicklerwerkzeuge; "Linux-aehnliches" Verhalten.

**Preis & Lizenzmodell**
- Vollstaendig kostenlos, Open Source (MIT / Unlicense). Keine Editionen, keine kommerzielle Variante.

**Tech/UX**
- CLI-only (reines PowerShell-Tool); keine GUI (Drittanbieter wie UniGetUI moeglich).
- Plattform: Windows; PowerShell-basiert, user-level.
- Open Source: ja.
- Repo-Groesse: `main` ~1.268, `extras` ~1.892 Pakete; insgesamt deutlich kleiner als winget/Choco, dafuer dev-fokussiert (Stand pruefen, waechst).
- Telemetrie: keine nennenswerte; rein lokal/Git.
- Bloatware/Bundling: sehr gering — bevorzugt portable Archive, kein Installer-Bundling; Community-Manifeste sind aber weniger streng moderiert als winget.
- Reputation: hoch bei Entwicklern/Power-Usern; weniger fuer Endanwender-Apps.

---

### Patch My PC (Home Updater / Enterprise)

**Feature-Katalog (Home Updater)**
- GUI-Tool (portable, keine Installation noetig), listet veraltete Apps und aktualisiert sie per Klick.
- 500+ unterstuetzte Drittanbieter-Apps (Chrome, Firefox, Adobe Reader, Java, 7-Zip, Zoom u.v.m.).
- Silent-/unbeaufsichtigter Update-Modus.
- Auto-Update-Scheduling: taeglich, werktags, woechentlich, monatlich, jaehrlich; Windows-Toast-Benachrichtigung bei verfuegbaren Updates.
- Unterstuetzt auch portable Apps; kann fehlende Apps neu installieren.
- Sicherheit: bezieht Updates aus offiziellen Quellen, prueft via VirusTotal.

**Feature-Katalog (Enterprise/Cloud — kostenpflichtig)**
- Integration in Microsoft Intune und ConfigMgr/SCCM (Publishing-Service); Cloud-Variante als SaaS ohne On-Prem-Infrastruktur.
- Automatisches Paketieren/Veroeffentlichen, Reporting, custom Apps; auf Enterprise-Patchmanagement ausgelegt.

**Preis & Lizenzmodell**
- Home Updater: kostenlos, **nur private/nicht-kommerzielle Nutzung**.
- Enterprise/Cloud: pro Geraet/Jahr, gestaffelt; haeufig ca. 2–7 USD/Geraet/Jahr je nach Volumen/Laufzeit; Editionen Enterprise Patch / Plus / Premium (ca. 2,00 / 3,50 / 5,00 USD/Jahr genannt). Abo. Genaue Preise nur per Angebot — **Stand pruefen**.

**Tech/UX**
- Home: GUI-first (kein offizielles CLI fuer Heim-Tool); Enterprise: ueber Intune/SCCM und Publishing-Service automatisiert.
- Plattform: Windows.
- Open Source: nein (proprietaer).
- Telemetrie: Enterprise erhebt Geraete-/Patchdaten (notwendig fuer Reporting); Home-Tool unkritisch.
- Bloatware/Bundling: gering — explizit darauf ausgelegt, Hersteller-Bundling/Toolbars zu vermeiden.
- Reputation: sehr hoch, besonders im Enterprise-/MSP-Umfeld (Intune-Drittanbieter-Patching).

---

### Ninite

**Feature-Katalog (Ninite Home, kostenlos)**
- Web-basiert: Nutzer waehlt Apps auf ninite.com, laedt einen massgeschneiderten Installer; ein Klick installiert/aktualisiert alle ausgewaehlten Apps.
- 100+ populaere Apps (Browser, Player, Reader, Kompression, Messenger usw.).
- Installiert immer die aktuellste Version; bereits aktuelle Apps werden uebersprungen.
- Vollautomatisch/silent — klickt "Weiter" weg und **lehnt Toolbars/Bundleware/Junkware automatisch ab**.
- Wieder-Ausfuehren des Installers = Update-Lauf (gleicher Installer fungiert als Updater).

**Feature-Katalog (Ninite Pro, kostenpflichtig)**
- Web-Dashboard zum Verwalten vieler Maschinen, leichtgewichtiger Ninite-Agent.
- Install-/Update-/Uninstall-Befehle fuer online und offline Maschinen; Auto-Update-Policies.
- Online/Offline-Status, Tagging, Echtzeit-Reporting, optionaler Cache-Server.

**Preis & Lizenzmodell**
- Home: kostenlos, **nur private Nutzung**.
- Pro: Abo nach Maschinenzahl, ab ca. 35 USD/Monat (50 Maschinen), 135 USD/Monat (250), 365 USD/Monat (1.000), bis ~5.115 USD/Monat (20.000) — Stand pruefen.

**Tech/UX**
- GUI/Web-first; kein klassisches lokales CLI (Pro per Agent/Dashboard).
- Plattform: Windows.
- Open Source: nein (proprietaer).
- Telemetrie: Installer fragt Konfiguration vom Ninite-Server ab (funktionsbedingt); Pro erhebt Inventardaten.
- Bloatware/Bundling: **vorbildlich** — Kernzweck ist gerade die Vermeidung von Bundleware. Sehr saubere Installer.
- Reputation: sehr hoch und langjaehrig (Standardempfehlung fuer schnelle Neuaufsetzung).

---

### UCheck (Adlice Software)

**Feature-Katalog**
- GUI-Tool plus separate CLI-Variante **UCheckCMD** (reines Shell-Tool, cmd/PowerShell, keine UI).
- Erkennt installierte Software, prueft auf Updates und aktualisiert automatisch; auch Windows-Update-Pruefung.
- CLI kann installierte Programme listen, Updates pruefen, Windows-Updates pruefen, kompatible Programme anzeigen, `-update ALL` ausfuehren.
- Software-Deinstallation und Neu-Installation von Apps moeglich; Adware-Warnungen.
- Bezieht Updates ausschliesslich aus offiziellen Quellen.
- Keine Treiber-Updates (bewusst ausgeklammert; von Nutzern gewuenscht, nicht implementiert).

**Preis & Lizenzmodell**
- Free-Version: funktionsfaehig, aber im Batch eingeschraenkt (Updates effektiv ein Programm nach dem anderen); 30-Tage-Pro-Test.
- Premium (Personal): ab ca. 10 EUR (bzw. USD-Aequivalent); Laufzeiten 1 Jahr / 2 Jahre / Lifetime; 1, 3, 5 oder 10 Geraete. Auch Technician-Variante. Stand pruefen.
- **UCheckCMD ist nicht separat zu kaufen** — die CLI nutzt dieselbe UCheck-Premium-Lizenz (`-register`).

**Tech/UX**
- GUI mit zusaetzlicher CLI (UCheckCMD) — fuer Skriptbarkeit relevant, aber CLI-Vollfunktion an Premium gebunden.
- Plattform: Windows.
- Open Source: nein (proprietaer).
- Repo/Katalog: vergleichsweise klein (Reviews nennen ~120 Apps, Datenbank "sehr begrenzt" im Vergleich zu winget/Choco). Stand pruefen.
- Telemetrie: herstellerueblich, unkritisch.
- Bloatware/Bundling: gering; Updates aus offiziellen Quellen, warnt sogar vor Adware.
- Reputation: solide Nische (Adlice ist v.a. fuer RogueKiller bekannt); kleinere Reichweite.

---

### SUMo (KC Softwares) — abgekuendigt / End-of-Life

**Status**
- **Eingestellt:** KC Softwares hat den Betrieb zum 31.10.2023 beendet. Die Update-Erkennungs-Server sind offline; SUMo (und DUMo/KCleaner) **funktionieren nicht mehr** zuverlaessig. Kein Verkauf, kein Support mehr.

**Feature-Katalog (historisch)**
- Software Update Monitor: erkannte installierte Apps und meldete neuere Versionen (server-/crowd-basierter Versionsabgleich).
- Listenbasiertes Monitoring, Export, Ignore-Listen; Updates wurden i.d.R. extern heruntergeladen (kein echtes One-Click-Silent-Patching wie Patch My PC/Ninite).
- Schwesterprodukte: DUMo (Treiber-Update-Monitor), KCleaner.

**Preis & Lizenzmodell (historisch)**
- War Free + Pro (Einmalkauf-Lizenz, 1 Jahr). Heute irrelevant, da EOL — nicht mehr beziehbar.

**Tech/UX**
- GUI-first (auch portable Version, ebenfalls discontinued).
- Plattform: Windows. Open Source: nein.
- Empfehlung der Community: Migration zu winget, Patch My PC, Ninite oder UCheck.
- **Fazit:** Als aktiver Wettbewerber irrelevant; nur als Beleg, dass der "Versions-Monitor"-Ansatz ohne gepflegtes Backend nicht ueberlebt.

---

### Vergleichs-Feature-Matrix

| Tool | CLI / GUI | Repo-/Katalog-Groesse | Silent-Update | Auto-Update-Scheduling | Enterprise-Tauglich | Skriptbar / API | Open Source | Preis (Stand pruefen) |
|---|---|---|---|---|---|---|---|---|
| **winget** | CLI-first (keine offiz. GUI) | ~30.000+ Manifeste / mehrere Tsd. Pakete + msstore | Ja (`--silent`) | Ja (per Task Scheduler/Skript) | Ja (private Feeds, `configure`, Intune) | Sehr gut (CLI, PowerShell, COM, JSON-Export) | Ja (MIT) | Kostenlos |
| **Chocolatey** | CLI-first (GUI optional) | ~11.000 Pakete (Community) | Ja (Default) | Ja (Skript/Task; C4B-Service) | Ja (C4B, interne Repos, Reporting) | Sehr gut (CLI, PowerShell, NuGet-Feeds) | Teilweise (Kern Apache 2.0; Pro/C4B proprietaer) | OSS frei; Pro ~96 USD/J; C4B ~17–18 USD/Node/J (min. 100) |
| **Scoop** | CLI-only | ~1.268 (main) + ~1.892 (extras) | Ja (`update *`) | Ja (per Task Scheduler/Skript) | Bedingt (eigene Buckets; kein Enterprise-Mgmt) | Sehr gut (CLI, Git-Manifeste) | Ja (MIT/Unlicense) | Kostenlos |
| **Patch My PC** | GUI (Home); Enterprise via Intune/SCCM | 500+ Apps | Ja | Ja (taeglich/woechentlich/…) | Ja (Cloud/Intune/SCCM, Reporting) | Eingeschr. (Heim-Tool kein CLI; Enterprise per Publishing) | Nein | Home kostenlos (privat); Enterprise ~2–7 USD/Geraet/J |
| **Ninite** | Web/GUI (Pro: Agent+Dashboard) | 100+ Apps | Ja | Ja (Pro-Policies) | Ja (Ninite Pro) | Eingeschr. (Pro-API/Agent; Home kaum) | Nein | Home kostenlos (privat); Pro ab ~35 USD/Monat (50 Geraete) |
| **UCheck (Adlice)** | GUI + CLI (UCheckCMD) | ~120 Apps | Ja (Premium) | Ja (Premium-Scheduling) | Bedingt (Technician-Lizenz) | Mittel (CLI an Premium gebunden) | Nein | Free eingeschr.; Premium ab ~10 EUR (1–10 Geraete) |
| **SUMo (EOL)** | GUI | — (Server offline) | Nein (Monitor, kein Patcher) | — | Nein | Nein | Nein | Eingestellt (31.10.2023) |

**Einordnung fuer WinCleaner:** winget, Chocolatey und Scoop sind die direkten CLI-/Skript-Konkurrenten und decken Paketmanagement + Software-Updates ab — ein Bereich, der WinCleaner komplett fehlt. winget ist zudem auf jedem modernen Windows vorinstalliert und kostenlos, was die Eintrittshuerde fuer einen eigenen Paketmanager hoch macht. Realistischer Mehrwert fuer WinCleaner liegt eher in der **Orchestrierung/Wrapper** (z.B. `update`-Befehl, der winget/choco aufruft) als im Aufbau eines eigenen Repos.
