# WinCleaner

Windows-only command-line cleanup tool (C# / .NET 8). Scans and removes junk,
analyzes disk usage, finds duplicates, and manages startup entries, restore
points, and scheduled cleanups.

## Build

Requires the **.NET 8 SDK**.

```sh
dotnet build WinCleaner.sln -c Release
```

The project targets `net8.0-windows` because it uses Windows-only APIs:
`Microsoft.VisualBasic.FileIO` (recycle-bin deletion), the registry
(startup entries), and WMI (system restore points).

## Test

```sh
dotnet test WinCleaner.sln
```

18 xUnit tests cover `DiskAnalyzer`, `DuplicateFinder`, and the
`StartupManager` enable/disable blob logic. CI runs build + test on
`windows-latest` (`.github/workflows/ci.yml`).

## Commands

| Command | Description |
|---------|-------------|
| `scan-junk` | List junk files (temp, prefetch, browser caches, WER, update cache). No deletion. |
| `clean-junk [--no-dry-run] [--yes]` | Clean junk. Default is a dry run; `--no-dry-run` deletes (to recycle bin) after a confirmation prompt, `--yes` skips the prompt. Only **Safe**-rated categories are removed. |
| `analyze-disk <path>` | Largest folders/files under a path, sorted by size, with percentages. |
| `find-duplicates <path> [--delete]` | Find content-identical files; `--delete` keeps one per group. |
| `startup-list` | List autostart entries (registry Run keys + startup folders) with enabled state. |
| `startup-disable <name>` | Disable an autostart entry (reversible, like Task Manager). Self-elevates via UAC for machine-wide entries. |
| `create-restore-point [name]` | Create a system restore point (self-elevates via UAC; needs System Protection on). |
| `schedule-clean daily\|weekly` | Register a scheduled cleanup task at 03:00. |
| `unschedule-clean` | Remove the scheduled cleanup task. |
| `help` | Show help. |

### Options

- `--json` — machine-readable output for `scan-junk`, `analyze-disk`,
  `find-duplicates`, `startup-list`. Diagnostics go to stderr, so
  `... --json | jq` stays clean.

## Safety

- `clean-junk` defaults to a **dry run**; real deletion requires
  `--no-dry-run` and an interactive confirmation (or `--yes`).
- Deletions go to the **recycle bin**, not permanent erase.
- Only categories rated `Safe` are auto-cleaned; `Caution` items (e.g.
  Windows Update cache) are listed but skipped.
- `startup-disable` writes a reversible disable flag — it does not delete the
  entry, so it can be re-enabled from Windows.

## Project layout

```
WinCleaner.sln
WinCleaner/            # main CLI project
  Program.cs          # command dispatch
  Core/               # JunkScanner, JunkCleaner, DiskAnalyzer, DuplicateFinder, Logger
  SystemTools/        # StartupManager, RestorePoint, TaskSchedulerHelper, Elevation
  Util/               # ConsoleTable
WinCleaner.Tests/     # xUnit tests
```
