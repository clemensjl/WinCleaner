# WinCleaner

Windows-only command-line system maintenance tool (C# / .NET 8). A scriptable,
safety-first, German-language CLI that cleans junk, analyzes disk usage, finds
duplicates, manages startup entries / services / scheduled tasks, removes
bloatware, applies reversible privacy & telemetry tweaks, wraps `winget` for
software updates, and more — without bundling, telemetry, or a resident service.

> **v1.1.0** greatly expands the command set and introduces a reflection-based
> command registry (each command is a self-contained `ICommand`). See
> [CHANGELOG.md](CHANGELOG.md).

## Design principles

- **Scriptable first** — `--json` output, exit codes, stderr-only diagnostics,
  and Task Scheduler integration make WinCleaner a building block for admin
  automation, where the GUI suites are click-only.
- **Safety first** — destructive commands default to a **dry run**; real action
  needs `--no-dry-run` plus a confirmation (or `--yes`). Deletions go to the
  **recycle bin**; system tweaks are **reversible** (registry backup + undo,
  restore points). Only irreversible commands (`shred`, `wipe-free-space`) erase
  permanently, and only with explicit opt-in.
- **No bloat** — no telemetry, no bundling, no background daemon, no
  "health-score" nags.

## Build

Requires the **.NET 8 SDK**.

```sh
dotnet build WinCleaner.sln -c Release
```

The project targets `net8.0-windows` because it uses Windows-only APIs:
`Microsoft.VisualBasic.FileIO` (recycle-bin deletion), the registry (startup
entries, services, privacy tweaks), WMI (restore points, drive type), and
`winget` / `powershell` via child processes.

## Test

```sh
dotnet test WinCleaner.sln
```

65 xUnit tests cover `DiskAnalyzer` (size parsing, by-extension grouping, depth
aggregation without double-counting, filters), `DuplicateFinder` (grouping,
keep-strategy selection, protected paths, dry-run), `JunkScanner` /
`JunkCleaner`, `TaskSchedulerHelper`, `Program` (flag validation, version), and
the `StartupManager` blob logic. CI runs build + test on `windows-latest`, then
publishes a self-contained single-file `win-x64` `.exe`; pushing a `v*` tag
attaches that `.exe` to a GitHub Release (`.github/workflows/ci.yml`).

## Commands

Run `WinCleaner help` for the full list (auto-generated from the command
registry) or `WinCleaner help <command>` / `<command> --help` for one command.

### Cleaning

| Command | Description |
|---------|-------------|
| `scan-junk` | List junk files (temp, prefetch, browser caches, WER, update cache). No deletion. |
| `clean-junk [--no-dry-run] [--yes]` | Clean junk (dry run by default → recycle bin). Only **Safe**-rated categories are removed. |
| `browser-clean [--browser chrome\|edge\|brave\|firefox] [--cookies] [--history] [--sessions] [--no-dry-run] [--yes]` | Per-browser/per-profile cache cleaning; cookies/history/sessions are opt-in. Dry run by default → recycle bin. |

### Disk & duplicates

| Command | Description |
|---------|-------------|
| `analyze-disk <path> [--by-type] [--min-size <e.g.100MB>] [--type <.ext,.ext>] [--age-days <n>] [--depth <n>] [--top <n>] [--export csv\|html] [--out <path>]` | Largest folders/files, or grouped by extension; size/type/age filters; CSV/HTML export. |
| `find-duplicates <path> [--delete] [--keep oldest\|newest\|shortest-path\|longest-path] [--protect <path[,path]>] [--hard-link] [--no-dry-run] [--yes]` | Content-identical files; choose which copy to keep, protect reference folders, or replace duplicates with NTFS hard links. Dry run by default → recycle bin. |
| `scan-extras <path> [--delete] [--no-dry-run] [--yes]` | Find empty folders, 0-byte files and broken shortcuts/symlinks; optional delete to recycle bin. |

### Secure delete (irreversible)

| Command | Description |
|---------|-------------|
| `shred <path> [--passes <n>] [--no-dry-run] [--yes]` | Multi-pass overwrite then delete a file/folder. **Irreversible.** Warns on SSDs (overwrite is ineffective under wear-leveling/TRIM). |
| `wipe-free-space <drive> [--no-dry-run] [--yes]` | Overwrite a drive's free space to erase remnants of deleted files. Keeps a reserve to avoid starving the system. |

### Programs & updates

| Command | Description |
|---------|-------------|
| `list-programs [search]` | List installed programs (registry Uninstall keys). |
| `uninstall <name> [--silent] [--no-dry-run] [--yes]` | Uninstall via the program's UninstallString (silent flag detection for MSI/NSIS/Inno). Restore point first; leftover removal needs its own explicit confirmation. |
| `debloat [--list] [--no-dry-run] [--yes]` | Remove a curated, conservative set of preinstalled Store apps (reinstallable from the Store). Dry run by default; restore point first. |
| `list-updates` | Show available package updates (`winget`). |
| `update [--no-dry-run] [--yes]` | Upgrade all packages via `winget` (dry run by default). |
| `install <id/name> [--yes]` | Install a package via `winget`. |
| `schedule-update daily\|weekly \| unschedule` | Schedule (or remove) automatic `winget` updates. |

### System & privacy

| Command | Description |
|---------|-------------|
| `startup-list` | List autostart entries (registry Run keys + startup folders). |
| `startup-disable <name>` | Reversibly disable an autostart entry (UAC for machine-wide entries). |
| `services [--list] [--set <name> manual\|disabled\|auto] [--undo <name>] [--profile safe-disable] [--no-dry-run] [--yes]` | List services and change start type **reversibly** (registry backup + undo). |
| `scan-privacy` | Read-only audit of telemetry/AI privacy tweaks (applied or not). |
| `privacy [--status] \| --apply [standard\|advanced] \| --undo [--no-dry-run] [--yes]` | Apply/undo reversible telemetry, tracking and AI (Copilot/Recall) tweaks. Restore point before machine-wide changes. |
| `block-telemetry [--status] [--apply [--no-dry-run] [--yes]] [--undo [--yes]]` | Block Microsoft telemetry hosts via a marked, reversible section in the `hosts` file (with backup). Conservative list — no update/store hosts. |
| `create-restore-point [name]` | Create a system restore point (self-elevates via UAC). |
| `schedule-clean daily\|weekly` / `unschedule-clean` | Register / remove the scheduled junk cleanup at 03:00. |

### Meta

| Command | Description |
|---------|-------------|
| `version` / `--version` | Print the tool version. |
| `help [command]` | Show help, or usage for a single command. |

### Global options

- `--json` — machine-readable output (where supported). Diagnostics and prompts
  go to **stderr**, so `... --json | jq` stays clean. Each command emits a
  single JSON document per run.

## Safety model

- **Dry run by default** for every destructive/modifying command; real action
  needs `--no-dry-run` and a confirmation (or `--yes`).
- **Reversible by default**: deletions go to the recycle bin; registry tweaks
  (privacy, services) are backed up and can be undone; `block-telemetry` only
  edits a marked section and backs up `hosts`; a restore point is created before
  machine-wide changes.
- **Irreversible only on demand**: `shred` and `wipe-free-space` overwrite data
  and are never the default; they warn loudly and on SSDs.
- **Conservative curation**: `debloat` and `services --profile safe-disable`
  never touch security-, boot-, or network-critical components.
- **Deliberately not built** (anti-features): aggressive registry "cleaning",
  RAM "optimization", a resident real-time monitor, drivers from unverified
  sources, bundling/telemetry. See `docs/research/99-gap-analyse.md`.

## Architecture

Each subcommand is a self-contained `ICommand` (in `Commands/`), discovered at
startup by reflection (`CommandRegistry`) — adding a command needs no change to
`Program.cs`, and the help text is generated from the registry. Shared
infrastructure: `TweakEngine` (reversible registry tweaks with JSON backup/undo),
`RecycleBinHelper`, `Elevation` (UAC self-relaunch), `RestorePoint`, `JsonOut`,
`Prompt`.

```
WinCleaner.sln
WinCleaner/                 # main CLI project
  Program.cs               # thin dispatcher (registry lookup + flag validation)
  Commands/                # one ICommand per subcommand + CommandRegistry, HelpCommand
  Core/                    # JunkScanner, JunkCleaner, DiskAnalyzer, DuplicateFinder,
                           #   BrowserCleaner, ExtraScanner, Logger
  SystemTools/             # StartupManager, RestorePoint, TaskSchedulerHelper, Elevation,
                           #   TweakEngine, PrivacyTweaks, ServiceManager, AppxManager,
                           #   ProgramInventory, SecureDelete, HostsBlocker, WingetWrapper
  Util/                    # ConsoleTable, JsonOut, Prompt, RecycleBinHelper, AppInfo
WinCleaner.Tests/          # xUnit tests
docs/research/             # competitor analysis & feature gap roadmap
```
