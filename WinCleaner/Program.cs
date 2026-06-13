using WinCleaner.Core;
using WinCleaner.SystemTools;
using WinCleaner.Util;

namespace WinCleaner;

public class Program
{
    public static int Main(string[] args)
    {
        var logger = new Logger();

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
                    var rows = analyzer.Analyze(args[1], topN: 25);
                    ConsoleTable.From(rows, "Typ","Pfad/Name","Größe (MB)","Anzahl").Write();
                    break;
                }
                case "find-duplicates":
                {
                    if (args.Length < 2) { Console.WriteLine("Pfad fehlt: find-duplicates <Pfad>"); return 1; }
                    bool delete = args.Contains("--delete");
                    var finder = new DuplicateFinder(logger);
                    var groups = finder.Find(args[1]);
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
                    sm.Disable(string.Join(' ', args.Skip(1)));
                    break;
                }
                case "create-restore-point":
                {
                    string name = args.Length >= 2 ? string.Join(' ', args.Skip(1)) : $"WinCleaner {DateTime.Now:yyyy-MM-dd HH:mm}";
                    var rp = new RestorePoint(logger);
                    bool ok = rp.Create(name);
                    Console.WriteLine(ok
                        ? "Wiederherstellungspunkt erstellt."
                        : "Fehlgeschlagen (Systemschutz aktiv? Adminrechte?).");
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

    private static void PrintHelp()
    {
        Console.WriteLine("""
WinCleaner (CLI)

Befehle:
  scan-junk                          Junk-Dateien auflisten (kein Löschen)
  clean-junk [--no-dry-run]          Bereinigen (Standard: Dry-Run)
  analyze-disk <Pfad>                Größte Ordner/Dateien anzeigen
  find-duplicates <Pfad> [--delete]  Doppelte Dateien finden/löschen
  startup-list                       Autostart-Einträge auflisten
  startup-disable <Name>             Autostart-Eintrag deaktivieren
  create-restore-point [Name]        Wiederherstellungspunkt erstellen (Admin)
  schedule-clean daily|weekly        Automatische Bereinigung planen
  unschedule-clean                   Geplante Bereinigung entfernen
  help                               Diese Hilfe anzeigen
""");
    }
}
