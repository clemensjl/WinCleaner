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

    public static int Main(string[] args)
    {
        var logger = new Logger();
        bool json = args.Contains("--json");

        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        try
        {
            switch (args[0].ToLowerInvariant())
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
                        Console.WriteLine("Abgebrochen.");
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
                    if (args.Length < 2) { Console.WriteLine("Pfad fehlt: analyze-disk <Pfad>"); return 1; }
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
                    if (args.Length < 2) { Console.WriteLine("Pfad fehlt: find-duplicates <Pfad>"); return 1; }
                    bool delete = args.Contains("--delete");
                    var finder = new DuplicateFinder(logger);
                    var groups = finder.Find(args[1]);
                    if (json)
                    {
                        OutputJson(groups);
                        if (delete) finder.DeleteDuplicates(groups);
                        break;
                    }
                    foreach (var g in groups)
                    {
                        Console.WriteLine($"\nHASH {g.Hash}  Dateien: {g.Files.Count}  Gesamt: {(g.TotalBytes/(1024*1024.0)):N1} MB");
                        foreach (var f in g.Files) Console.WriteLine("  " + f);
                    }
                    if (delete)
                    {
                        finder.DeleteDuplicates(groups);
                        Console.WriteLine("\nDuplikate gelöscht (je Gruppe eine Datei behalten).");
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
                    if (args.Length < 2) { Console.WriteLine("Name fehlt: startup-disable <Name>"); return 1; }
                    var sm = new StartupManager(logger);
                    var name = string.Join(' ', args.Skip(1).Where(a => !a.StartsWith("--")));
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
                    if (args.Length < 2) { Console.WriteLine("Intervall fehlt: schedule-clean daily|weekly"); return 1; }
                    var tsh = new TaskSchedulerHelper(logger);
                    return tsh.CreateScheduledClean(args[1]) ? 0 : 1;
                }
                case "unschedule-clean":
                {
                    var tsh = new TaskSchedulerHelper(logger);
                    return tsh.RemoveScheduledClean() ? 0 : 1;
                }
                case "help":
                default:
                    PrintHelp();
                    break;
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

    // Bestätigung vor echtem Löschen. Nur "Safe"-Einträge werden bereinigt.
    private static bool ConfirmClean(JunkReport report)
    {
        var safe = report.Items.Where(i => i.Safety == Safety.Safe).ToList();
        long bytes = safe.Sum(i => i.TotalBytes);
        int files = safe.Sum(i => i.FileCount);

        Console.WriteLine($"\n{files} Dateien ({DiskAnalyzer.FormatSize(bytes)}) werden in den " +
                          $"Papierkorb verschoben (nur als sicher eingestufte Kategorien).");

        // Nicht-interaktiv ohne --yes: kein Rückfragekanal -> sicher abbrechen.
        if (Console.IsInputRedirected)
        {
            Console.WriteLine("Keine interaktive Konsole. Mit --yes bestätigen.");
            return false;
        }

        Console.Write("Fortfahren? [j/N] ");
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
  find-duplicates <Pfad> [--delete]  Doppelte Dateien finden/löschen
  startup-list                       Autostart-Einträge auflisten
  startup-disable <Name>             Autostart-Eintrag deaktivieren
  create-restore-point [Name]        Wiederherstellungspunkt erstellen (Admin)
  schedule-clean daily|weekly        Automatische Bereinigung planen
  unschedule-clean                   Geplante Bereinigung entfernen
  help                               Diese Hilfe anzeigen

Optionen:
  --json   Maschinenlesbare Ausgabe (scan-junk, analyze-disk,
           find-duplicates, startup-list)
""");
    }
}
