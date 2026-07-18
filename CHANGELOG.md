# Changelog

All notable changes to WinCleaner are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.1.0] - 2026-07-19

Closes the remaining feasible items from the competitive gap analysis
(`docs/research/99-gap-analyse.md`): fast NTFS scanning, visual image
duplicates, space-saving hard links and an interactive disk report.

### Added
- **`analyze-disk --fast`** (M11) — NTFS fast scan: directory tree via USN
  enumeration (`FSCTL_ENUM_USN_DATA`), sizes via large-fetch enumeration.
  Requires elevation and NTFS; falls back to the standard scanner with a
  stderr notice otherwise. Output and JSON structure are identical to the
  standard scan. Junctions/reparse points are never followed.
- **`find-similar-images <path>`** (M8) — finds visually similar images
  (rescaled/recompressed copies) via 64-bit dHash and Hamming-distance
  clustering (`--threshold 0-16`, `--recurse`). Same safety flow as
  `find-duplicates`: dry-run by default, recycle bin, `--keep`/`--protect`.
- **`find-duplicates --hard-link`** (M7, hardened) — replaces duplicates with
  NTFS hard links via temp-link + atomic `File.Replace` swap and recycle-bin
  backup. Guards: same volume, NTFS only, reparse points, 1024-link limit,
  already-linked detection via file IDs. Per-action detail in `--json`.
- **`analyze-disk --html <report.html>`** (N6) — self-contained interactive
  HTML report: squarified treemap with click-zoom, breadcrumb and tooltips,
  top-directory table, size-by-extension breakdown; light/dark via
  `prefers-color-scheme`; no external requests.

### Changed
- `find-duplicates` reports skipped files and per-file actions in `--json`;
  `--delete` and `--hard-link` are mutually exclusive.
- Keep/protect option parsing shared between `find-duplicates` and
  `find-similar-images` (`KeepProtectOptions`).
- Disk snapshots record the effective scan mode (`standard`/`ntfs-fast`);
  `disk-diff` warns when comparing snapshots from different modes (junction
  entries may appear as added/removed). Old snapshot files keep loading.
- `NtfsFastScanner.IsSupported` additionally returns a typed block reason
  (`FastScanBlockReason`); the GUI switches on it instead of matching
  message text.

### Fixed
- Hard-link replacement no longer transfers the duplicate's attributes and
  creation time onto the kept file (Win32 `ReplaceFile` metadata semantics
  on a shared file record).
- Duplicate/image actions report real per-file sizes instead of the group
  average — relevant for similar images of different sizes (console,
  `--json` and GUI dialogs).
- Hard links across UNC/SMB paths work again (regression vs. 2.0.0:
  everything was skipped as "not NTFS").
- The 1024-links-per-file limit is enforced for planned links in dry runs
  too, so preview and execution match; unreadable file identity now causes
  a conservative skip (new `--json` action code `skip-identity-unknown`).
- `find-similar-images` drops `.webp` (GDI+ cannot decode WebP) and lists
  skipped files like `find-duplicates` does.
- GUI: the fast-scan preflight checks whether the installed CLI knows
  `--fast` before showing a UAC prompt; failure messages include the exit
  code; path/support checks no longer block the UI thread.

## [2.0.0] - 2026-07-05

Adds a real graphical desktop application (**WinCleaner.Gui**, WPF) alongside
the CLI and the text menu. A window — no terminal — with a dark/light
instrument-panel design that follows the Windows theme. The CLI and TUI are
unchanged; the GUI is an additional frontend over the same Core logic.

### Added
- **WinCleaner.Gui** — WPF desktop app (`net8.0-windows`, `WinExe`, no console
  window). Left navigation over eight pages covering all commands: Übersicht
  (dashboard readout), Aufräumen (junk), Speicher (disk analysis + duplicates),
  Programme (inventory, uninstall, debloat, updates), Autostart & Dienste,
  Privatsphäre (audit + apply/undo + telemetry blocking), Sicher löschen
  (shred / wipe-free-space, clearly marked irreversible), System (restore point,
  scheduled cleanup).
- **Preview-first safety in the UI**: read-only scans list what would change;
  destructive actions need an explicit confirmation, delete to the recycle bin,
  and reverse via undo. `shred` / `wipe-free-space` are red-flagged with an
  extra warning dialog.
- **Direct Core reuse**: non-admin operations call the Core/SystemTools classes
  in a background thread (UI never freezes); admin operations are delegated to
  the hardened CLI via UAC, so the proven elevation/restore-point path is reused.
- App icon and Start-menu/Desktop shortcuts; the window is a normal app, so it
  can be pinned to the taskbar by right-click.

### Changed
- `Core.Logger` gained an optional message sink (`Action<string,string>`) so the
  GUI can route diagnostics to its status bar. Default stderr behavior is
  unchanged — CLI and existing tests are unaffected.
- Test suite expanded to 112 tests (adds CommandLineToArgvW-safe argument
  quoting tests for the GUI).

## [1.3.0] - 2026-07-05

### Added
- **`menu`** — interactive text menu (TUI) over the existing commands: pick a
  task by number instead of memorising commands and flags. Every entry
  delegates to the exact same command dispatch as the CLI (no second code
  path); destructive tasks run a dry run first and are then confirmed
  individually. Refuses to run when input is redirected (non-interactive).
  A double-clickable **`WinCleaner-Menue.cmd`** launcher ships next to the exe.

### Changed
- `Program.Dispatch` extracted so the CLI and the `menu` command share one
  dispatch path (flag validation, `--help`, error handling).
- Test suite expanded from 94 to 101 tests.

## [1.2.0] - 2026-07-05

Completes the remaining feasible roadmap items from `docs/research/`
(wave 3): snapshot diffing, service/task leftover detection, scheduled
privacy re-apply and a persistent duplicate hash cache.

### Added
- **`disk-diff <alt.json> <neu.json>`** — compare two disk snapshots: which
  folders/files grew, shrank, appeared or vanished (sorted by |Δ|, `--top`,
  `--json`).
- **`analyze-disk --snapshot <file>`** — save the (unfiltered-by-top-N)
  top-level analysis as a snapshot file for later `disk-diff`.
- **`uninstall`** now also detects **services and scheduled tasks** that still
  point into the program's install folder (leftover scan). Report-only with
  the matching `sc delete` / `schtasks /Delete` commands — removing services
  and tasks is irreversible and deliberately stays a manual decision.
- **`schedule-privacy daily|weekly [--profile standard|advanced] | unschedule`**
  — scheduled re-apply of the privacy tweaks (Windows feature updates like to
  reset individual telemetry/AI switches); runs
  `privacy --apply <profile> --no-dry-run --yes` at 05:00.
- **`find-duplicates --cache`** — opt-in persistent hash cache
  (`%LOCALAPPDATA%\WinCleaner\hash-cache.json`): full SHA-256 hashes are
  reused across runs as long as a file's size and mtime are unchanged; never
  changes results, only speeds up repeat runs.

### Changed
- Test suite expanded from 65 to 94 tests.

## [1.1.0] - 2026-06-17

Major feature release. The command set grows from 9 to 26 commands, derived from
a competitor / feature-gap analysis (`docs/research/`). Every new
system-modifying command follows the safety model: dry-run by default, reversible
where possible, irreversible only on explicit opt-in.

### Added
- **`browser-clean`** — per-browser/per-profile cache cleaning
  (Chrome/Edge/Brave/Firefox); cookies/history/sessions opt-in; dry-run → recycle bin.
- **`analyze-disk`** options — `--by-type` (group by extension), filters
  (`--min-size`, `--type`, `--age-days`, `--depth`, `--top`) and
  `--export csv|html` (`--out`).
- **`find-duplicates`** options — `--keep oldest|newest|shortest-path|longest-path`,
  `--protect <path>` (reference folders never touched) and `--hard-link`
  (replace duplicates with NTFS hard links instead of deleting).
- **`scan-extras`** — find empty folders, 0-byte files and broken
  shortcuts/symlinks; optional delete to recycle bin.
- **`shred`** / **`wipe-free-space`** — secure multi-pass overwrite (file/folder)
  and free-space wiping; irreversible, opt-in only, SSD warning.
- **`list-programs`** / **`uninstall`** — installed-program inventory and
  UninstallString-based uninstall (MSI/NSIS/Inno silent detection; restore point
  first; leftover removal needs explicit consent).
- **`debloat`** — remove a curated, conservative set of preinstalled Store apps;
  dry-run default; restore point first.
- **`list-updates`** / **`update`** / **`install`** / **`schedule-update`** —
  `winget` wrappers for package updates.
- **`services`** — list and reversibly change service start type (registry
  backup + undo), incl. a conservative `safe-disable` profile.
- **`scan-privacy`** / **`privacy`** — read-only privacy audit and reversible
  telemetry/tracking/AI (Copilot/Recall) tweaks; restore point before
  machine-wide changes.
- **`block-telemetry`** — reversible, marked `hosts`-file section (with backup)
  blocking a conservative list of telemetry hosts.

### Changed
- **Architecture**: introduced an `ICommand` + reflection-based `CommandRegistry`.
  Each command is now a self-contained file; `Program.cs` is a thin dispatcher and
  the help is generated from the registry. Existing commands were migrated with no
  behavior change (version, per-command help, flag validation, stderr prompts,
  duplicate-delete confirmation all preserved).
- Shared infrastructure added: `TweakEngine` (reversible registry tweaks with
  JSON backup/undo), `RecycleBinHelper`, `JsonOut`, `Prompt`, `AppInfo`.
- `--json` commands now emit a single JSON document per run (no concatenated
  objects), including in real-action paths.
- Test suite expanded from 39 to 65 tests.

### Fixed
- `analyze-disk --depth ≥ 2` no longer double-counts `TotalBytes` (parent and
  child folders were both summed); totals now come from a single non-overlapping
  root measurement.
- `find-duplicates --keep newest` no longer keeps an unreadable/locked file by
  mistake (unreadable timestamps now sort last for both `newest` and `oldest`).
- `uninstall` leftover scan no longer offers a publisher's shared AppData folder
  (e.g. `Mozilla`, `Microsoft`) as a deletion candidate — prevents data loss for
  other still-installed programs; leftover removal always requires its own
  confirmation even with `--yes`.
- `scan-extras --delete` now removes broken **directory** symlinks/junctions
  (previously only files were handled).
- `winget` table parsing is locale-tolerant (German `ID` header) and no longer
  pollutes the available-version value when the source column is absent.

## [1.0.1] - 2026-06-17

### Changed
- CI: bump GitHub Actions off the deprecated Node 20 runtime —
  `actions/checkout@v6`, `actions/setup-dotnet@v5`,
  `actions/upload-artifact@v7`, `softprops/action-gh-release@v3`.

## [1.0.0] - 2026-06-17

First stable release.

### Added
- `scan-junk` / `clean-junk` — scan and remove junk (temp, prefetch, browser
  caches, Windows Error Reporting); dry-run by default, deletes to the recycle
  bin, only `Safe`-rated categories are auto-cleaned.
- `analyze-disk <path>` — largest folders/files by size, with percentages.
- `find-duplicates <path> [--delete]` — content-identical files via size →
  partial-hash → full SHA-256; `--delete` moves duplicates to the recycle bin
  after a confirmation prompt (`--yes` skips it).
- `startup-list` / `startup-disable <name>` — list and reversibly disable
  autostart entries (registry Run keys + startup folders); self-elevates via
  UAC for machine-wide entries.
- `create-restore-point [name]` — create a system restore point via WMI.
- `schedule-clean daily|weekly` / `unschedule-clean` — register/remove a
  scheduled cleanup at 03:00.
- `version` / `--version` — print the tool version.
- `--json` machine-readable output for `scan-junk`, `analyze-disk`,
  `find-duplicates`, `startup-list`.
- Self-contained single-file `win-x64` build published by CI; tagged releases
  (`v*`) attach the `.exe` to a GitHub Release.

### Fixed
- Scheduled `clean-junk` runs now pass `--yes`, so the non-interactive task no
  longer aborts silently at its confirmation prompt.
- `find-duplicates --delete` now moves files to the recycle bin (was a
  permanent delete) and requires confirmation.

### Security
- Unknown flags are rejected and unknown commands exit non-zero, preventing a
  typo'd `--no-dry-run`/`--yes` from being silently ignored.
