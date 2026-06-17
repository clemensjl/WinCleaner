# Changelog

All notable changes to WinCleaner are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
