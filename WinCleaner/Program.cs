using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinCleaner.Core;
using WinCleaner.SystemTools;
using WinCleaner.Util;

namespace WinCleaner;

public class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // lesbare Pfade statt "
        Converters = { new JsonStringEnumConverter() }
    };

    // Erlaubte Optionen je Befehl. --relaunched/--help werden global zugelassen.
    private static readonly Dictionary<string, string[]> AllowedFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["scan-junk"]            = new[] { "--json" },
        ["clean-junk"]           = new[] { "--no-dry-run", "--yes" },
        ["analyze-disk"]         = new[] { "--json" },
        ["find-duplicates"]      = new[] { "--delete", "--json", "--yes" },
        ["startup-list"]         = new[] { "--json" },
        ["startup-disable"]      = Array.Empty<string>(),
        ["create-restore-point"] = Array.Empty<string>(),
        ["schedule-clean"]       = Array.Empty<string>(),
        ["unschedule-clean"]     = Array.Empty<string>(),
    };

    // Kurz-Hilfe je Befehl (für `help <cmd>`, `<cmd> --help` und Fehlermeldungen).
    private static readonly Dictionary<string, string> Usage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["scan-junk"]            = "scan-junk [--json]\n  Junk-Dateien auflisten (kein Löschen).",
        ["clean-junk"]           = "clean-junk [--no-dry-run] [--yes]\n  Bereinigen. Standard: Dry-Run. --no-dry-run löscht (Papierkorb) nach Rückfrage, --yes überspringt sie.",
        ["analyze-disk"]         = "analyze-disk <Pfad> [--json]\n  Größte Ordner/Dateien unter <Pfad> anzeigen.",
        ["find-duplicates"]      = "find-duplicates <Pfad> [--delete] [--yes] [--json]\n  Inhaltsgleiche Dateien finden. --delete verschiebt Duplikate in den Papierkorb (je Gruppe bleibt eine), --yes ohne Rückfrage.",
        ["startup-list"]         = "startup-list [--json]\n  Autostart-Einträge auflisten.",
        ["startup-disable"]      = "startup-disable <Name>\n  Autostart-Eintrag reversibel deaktivieren (UAC für systemweite Einträge).",
        ["create-restore-point"] = "create-restore-point [Name]\n  System-Wiederherstellungspunkt erstellen (Admin, Systemschutz aktiv).",
        ["schedule-clean"]       = "schedule-clean daily|weekly\n  Automatische Bereinigung um 03:00 einrichten.",
        ["unschedule-clean"]     = "unschedule-clean\n  Geplante Bereinigung entfernen.",
    };

    public static int Main(string[] args)
    {
        var logger = new Logger();

        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        string cmd = args[0].ToLowerInvariant();

        // Version (Befehl oder Top-Level-Flag).
        if (cmd is "version" or "--version")
        {
            Console.WriteLine($"WinCleaner {AppVersion}");
            return 0;
        }

        // Hilfe: "help", "help <cmd>", "--help"/"-h".
        if (cmd is "help" or "--help" or "-h")
        {
            if (args.Length > 1 && Usage.TryGetValue(args[1].ToLowerInvariant(), out var sub))
                Console.WriteLine(sub);
            else
                PrintHelp();
            return 0;
        }
        // "<cmd> --help" nur als Hilfe behandeln, wenn der Befehl bekannt ist;
        // sonst (unbekannter Befehl) durchfallen lassen -> Exit-Code 1.
        if ((args.Contains("--help") || args.Contains("-h")) && Usage.TryGetValue(cmd, out var cmdUsage))
        {
            Console.WriteLine(cmdUsage);
            return 0;
        }

        // Unbekannte Optionen früh ablehnen (z. B. Tippfehler --no-dryrun).
        if (!ValidateFlags(cmd, args, logger)) return 1;

        bool json = args.Contains("--json");

        try
        {
            switch (cmd)
            {
                case "scan-junk":
                {
                    var scanner = new JunkScanner(logger);
                    var report = scanner.Scan();
                    if (json)
                    {
                        OutputJson(new { report.TotalFiles, report.TotalBytes, report.Items });
                        break;
                    }
                    ConsoleTable.From(
                        report.Items.Select(i => new[] {
                            i.Category, i.Path, i.FileCount.ToString(),
                            (i.TotalBytes/(1024*1024.0)).ToString("N1")
                        }),
                        "Kategorie","Pfad","Dateien","Größe (MB)"
                    ).Write();
                    Console.WriteLine($"\nGesamt: {report.TotalFiles} Dateien, {(report.TotalBytes/(1024*1024.0)):N1} MB.");
                    break;
                }
                case "clean-junk":
                {
                    bool dryRun = !args.Contains("--no-dry-run");
                    var scanner = new JunkScanner(logger);
                    var report = scanner.Scan();

                    // Vor echter Bereinigung bestätigen lassen (außer --yes).
                    if (!dryRun && !args.Contains("--yes") && !ConfirmClean(report))
                    {
                        Console.Error.WriteLine("Abgebrochen.");
                        return 1;
                    }

                    var cleaner = new JunkCleaner(logger);
                    cleaner.Clean(report, dryRun: dryRun, sendToRecycleBin: true);
                    Console.WriteLine(dryRun
                        ? "Dry-Run abgeschlossen. Nutze --no-dry-run für echte Bereinigung."
                        : "Bereinigung abgeschlossen.");
                    break;
                }
                case "analyze-disk":
                {
                    if (args.Length < 2 || args[1].StartsWith("--")) { Console.Error.WriteLine(Usage["analyze-disk"]); return 1; }
                    var analyzer = new DiskAnalyzer(logger);
                    var analysis = analyzer.Analyze(args[1], topN: 25);
                    long total = analysis.TotalBytes;
                    if (json)
                    {
                        OutputJson(new { analysis.TotalBytes, analysis.Entries });
                        break;
                    }
                    var rows = analysis.Entries.Select(e => new[]
                    {
                        e.IsDir ? "Ordner" : "Datei",
                        e.Path,
                        DiskAnalyzer.FormatSize(e.Bytes),
                        e.Files.ToString(),
                        total > 0 ? $"{(e.Bytes * 100.0 / total):N1}%" : "-"
                    });
                    ConsoleTable.From(rows, "Typ","Pfad/Name","Größe","Dateien","%").Write();
                    Console.WriteLine($"\nGesamt (Top-Level): {DiskAnalyzer.FormatSize(total)}");
                    break;
                }
                case "find-duplicates":
                {
                    if (args.Length < 2 || args[1].StartsWith("--")) { Console.Error.WriteLine(Usage["find-duplicates"]); return 1; }
                    bool delete = args.Contains("--delete");
                    var finder = new DuplicateFinder(logger);
                    var groups = finder.Find(args[1]);

                    if (json)
                    {
                        OutputJson(groups);
                    }
                    else
                    {
                        foreach (var g in groups)
                        {
                            Console.WriteLine($"\nHASH {g.Hash}  Dateien: {g.Files.Count}  Gesamt: {(g.TotalBytes/(1024*1024.0)):N1} MB");
                            foreach (var f in g.Files) Console.WriteLine("  " + f);
                        }
                    }

                    if (delete)
                    {
                        if (groups.Count == 0)
                        {
                            if (!json) Console.WriteLine("\nKeine Duplikate zum Löschen.");
                            break;
                        }
                        // Echtes Löschen bestätigen (außer --yes); Prompts nach stderr,
                        // damit --json-stdout sauber bleibt.
                        if (!args.Contains("--yes") && !ConfirmDeleteDuplicates(groups))
                        {
                            Console.Error.WriteLine("Abgebrochen.");
                            return 1;
                        }
                        finder.DeleteDuplicates(groups); // Standard: Papierkorb
                        if (!json) Console.WriteLine("\nDuplikate in den Papierkorb verschoben (je Gruppe eine Datei behalten).");
                    }
                    break;
                }
                case "startup-list":
                {
                    var sm = new StartupManager(logger);
                    var items = sm.List();
                    if (json)
                    {
                        OutputJson(items);
                        break;
                    }
                    ConsoleTable.From(
                        items.Select(i => new[] { i.Source, i.Name, i.Path, i.Enabled ? "Ja" : "Nein" }),
                        "Quelle","Name","Pfad","Aktiv"
                    ).Write();
                    break;
                }
                case "startup-disable":
                {
                    var name = string.Join(' ', args.Skip(1).Where(a => !a.StartsWith("--")));
                    if (string.IsNullOrWhiteSpace(name)) { Console.Error.WriteLine(Usage["startup-disable"]); return 1; }
                    var sm = new StartupManager(logger);
                    var result = sm.Disable(name);

                    if (result == DisableResult.NeedsAdmin && !Elevation.IsAdministrator())
                    {
                        logger.Info("Starte mit Rechteerhöhung neu (UAC)...");
                        return Elevation.RelaunchAsAdmin(args, logger) ? 0 : 1;
                    }

                    PauseIfRelaunched(args);
                    return result is DisableResult.Success or DisableResult.AlreadyDisabled ? 0 : 1;
                }
                case "create-restore-point":
                {
                    // Wiederherstellungspunkte brauchen Adminrechte -> ggf. eleviert neu starten.
                    if (!Elevation.IsAdministrator())
                    {
                        logger.Info("Adminrechte nötig – starte mit Rechteerhöhung neu (UAC)...");
                        return Elevation.RelaunchAsAdmin(args, logger) ? 0 : 1;
                    }

                    var nameParts = args.Skip(1).Where(a => !a.StartsWith("--"));
                    string name = nameParts.Any()
                        ? string.Join(' ', nameParts)
                        : $"WinCleaner {DateTime.Now:yyyy-MM-dd HH:mm}";

                    var rp = new RestorePoint(logger);
                    bool ok = rp.Create(name);
                    Console.WriteLine(ok
                        ? "Wiederherstellungspunkt erstellt."
                        : "Fehlgeschlagen (Systemschutz aktiv? Adminrechte?).");

                    PauseIfRelaunched(args);
                    break;
                }
                case "schedule-clean":
                {
                    if (args.Length < 2) { Console.Error.WriteLine(Usage["schedule-clean"]); return 1; }
                    var tsh = new TaskSchedulerHelper(logger);
                    return tsh.CreateScheduledClean(args[1]) ? 0 : 1;
                }
                case "unschedule-clean":
                {
                    var tsh = new TaskSchedulerHelper(logger);
                    return tsh.RemoveScheduledClean() ? 0 : 1;
                }
                default:
                    logger.Error($"Unbekannter Befehl: {args[0]}");
                    PrintHelp();
                    return 1;
            }
            return 0;
        }
        catch (Exception ex)
        {
            logger.Error($"Unerwarteter Fehler: {ex.Message}");
            logger.Debug(ex.ToString());
            return 2;
        }
    }

    private static void OutputJson(object data)
        => Console.WriteLine(JsonSerializer.Serialize(data, JsonOpts));

    // Informational-/Assembly-Version, ohne Build-Metadaten (+hash).
    internal static string AppVersion
    {
        get
        {
            var asm = typeof(Program).Assembly;
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(info)) return info.Split('+')[0];
            return asm.GetName().Version?.ToString() ?? "unbekannt";
        }
    }

    // Lehnt unbekannte --Optionen ab; --relaunched/--help/-h sind global erlaubt.
    internal static bool ValidateFlags(string cmd, string[] args, Logger logger)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Elevation.RelaunchFlag, "--help", "-h"
        };
        if (AllowedFlags.TryGetValue(cmd, out var extra))
            foreach (var f in extra) allowed.Add(f);

        foreach (var a in args.Skip(1))
        {
            if (a.StartsWith("--") && !allowed.Contains(a))
            {
                logger.Error($"Unbekannte Option \"{a}\" für Befehl \"{cmd}\".");
                if (Usage.TryGetValue(cmd, out var u)) Console.Error.WriteLine(u);
                return false;
            }
        }
        return true;
    }

    // Bestätigung vor echtem Löschen. Nur "Safe"-Einträge werden bereinigt.
    private static bool ConfirmClean(JunkReport report)
    {
        var safe = report.Items.Where(i => i.Safety == Safety.Safe).ToList();
        long bytes = safe.Sum(i => i.TotalBytes);
        int files = safe.Sum(i => i.FileCount);

        Console.Error.WriteLine($"\n{files} Dateien ({DiskAnalyzer.FormatSize(bytes)}) werden in den " +
                          $"Papierkorb verschoben (nur als sicher eingestufte Kategorien).");

        // Nicht-interaktiv ohne --yes: kein Rückfragekanal -> sicher abbrechen.
        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("Keine interaktive Konsole. Mit --yes bestätigen.");
            return false;
        }

        Console.Error.Write("Fortfahren? [j/N] ");
        var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
        return answer is "j" or "ja" or "y" or "yes";
    }

    // Bestätigung vor echtem Duplikat-Löschen. Prompts nach stderr -> --json bleibt sauber.
    private static bool ConfirmDeleteDuplicates(List<DuplicateGroup> groups)
    {
        int files = groups.Sum(g => g.Files.Count - 1);
        long bytes = groups.Sum(g => g.Files.Count > 0
            ? (g.TotalBytes / g.Files.Count) * (g.Files.Count - 1)
            : 0);

        Console.Error.WriteLine($"\n{files} doppelte Dateien ({DiskAnalyzer.FormatSize(bytes)}) werden in den " +
                                "Papierkorb verschoben (je Gruppe bleibt eine erhalten).");

        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("Keine interaktive Konsole. Mit --yes bestätigen.");
            return false;
        }

        Console.Error.Write("Fortfahren? [j/N] ");
        var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
        return answer is "j" or "ja" or "y" or "yes";
    }

    // Eleviertes Fenster offen halten, damit das Ergebnis lesbar bleibt.
    private static void PauseIfRelaunched(string[] args)
    {
        if (!args.Contains(Elevation.RelaunchFlag)) return;
        Console.WriteLine("\nTaste drücken zum Schließen...");
        try { Console.ReadKey(true); } catch { /* keine interaktive Konsole */ }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
WinCleaner (CLI)

Befehle:
  scan-junk                          Junk-Dateien auflisten (kein Löschen)
  clean-junk [--no-dry-run] [--yes]  Bereinigen (Standard: Dry-Run; --yes überspringt Abfrage)
  analyze-disk <Pfad>                Größte Ordner/Dateien anzeigen
  find-duplicates <Pfad> [--delete]  Doppelte Dateien finden/löschen (Papierkorb, mit Rückfrage)
  startup-list                       Autostart-Einträge auflisten
  startup-disable <Name>             Autostart-Eintrag deaktivieren
  create-restore-point [Name]        Wiederherstellungspunkt erstellen (Admin)
  schedule-clean daily|weekly        Automatische Bereinigung planen
  unschedule-clean                   Geplante Bereinigung entfernen
  version                            Versionsnummer anzeigen
  help [Befehl]                      Diese Hilfe oder Hilfe zu einem Befehl

Optionen:
  --json   Maschinenlesbare Ausgabe (scan-junk, analyze-disk,
           find-duplicates, startup-list)
  --help   Hilfe zu einem Befehl (z. B. clean-junk --help)
""");
    }
}
