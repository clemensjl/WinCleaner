# WinCleaner — Wettbewerbs- & Feature-Research

Dieser Ordner (`docs/research/`) sammelt die **Wettbewerbsanalyse** und die
daraus abgeleitete **Feature-Gap-Analyse & Roadmap** für WinCleaner — das
deutschsprachige Windows-.NET-Konsolen-CLI zur Systembereinigung.

- **Stand:** 2026-06-17
- **Gegenstand:** Vergleich von WinCleaner (reines CLI, Safety-First, `--json`,
  kein Bloatware) gegen die typischen GUI-Cleaner-/Optimierer-Suiten und
  Open-Source-Tools des Marktes.

## Methodik

- **Multi-Agent-Recherche:** je ein Analyse-Durchgang pro Tool-Kategorie
  (8 Kategorien), mit Erhebung der jeweils marktüblichen Features und ihrem
  Status in WinCleaner (vorhanden / teilweise / fehlt).
- **Faktencheck:** Konsolidierung und Plausibilisierung der Einzelbefunde,
  Entduplizierung kategorieübergreifender Mehrfachnennungen, Einordnung von
  Realnutzen vs. Marketing (z. B. RAM-Optimierung, Registry-Cleaning).
- **Priorisierung:** Bewertung jeder Lücke nach Priorität (hoch/mittel/niedrig),
  Passung zum CLI-/Skript-/Safety-Charakter von WinCleaner und grober
  Umsetzungs-Komplexität (klein/mittel/groß).

## Inhalt

### Gesamtauswertung

- [99-gap-analyse.md](./99-gap-analyse.md) — **Master-Gap-Analyse & Roadmap**:
  konsolidierte, entduplizierte Feature-Lücken nach Priorität, 3-Wellen-Roadmap
  (Quick Wins / Mittelfristig / Strategisch), Abschnitt "Bewusst NICHT bauen"
  (Anti-Features) und Master-Feature-Matrix WinCleaner vs. Konkurrenz.

### Kategorie-Analysen

| # | Datei | Kategorie | Lücken |
|---|-------|-----------|--------|
| 01 | [01-allround-optimizer.md](./01-allround-optimizer.md) | All-in-One Optimizer & Cleaner | 9 |
| 02 | [02-uninstaller.md](./02-uninstaller.md) | Deinstallationsprogramme (Uninstaller) | 9 |
| 03 | [03-treiber-updater.md](./03-treiber-updater.md) | Treiber-Updater | 5 |
| 04 | [04-software-updater.md](./04-software-updater.md) | Software-Updater & Paketmanager | 6 |
| 05 | [05-terminal-debloat.md](./05-terminal-debloat.md) | Terminal- & Debloat-Tools (Power-User) | 9 |
| 06 | [06-disk-analyzer.md](./06-disk-analyzer.md) | Speicherplatz-Analyse (Disk Space Analyzer) | 8 |
| 07 | [07-duplikat-finder.md](./07-duplikat-finder.md) | Duplikat-Finder | 10 |
| 08 | [08-privacy-debloat.md](./08-privacy-debloat.md) | Privacy-, Telemetrie- & sicheres Löschen | 10 |

## Lesereihenfolge

1. Start mit der [Master-Gap-Analyse](./99-gap-analyse.md) für den priorisierten
   Gesamtüberblick und die Roadmap.
2. Für Detail-Begründungen je Tool-Markt in die einzelnen Kategorie-Dateien (01–08).