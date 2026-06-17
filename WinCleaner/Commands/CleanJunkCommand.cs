using WinCleaner.Core;
using WinCleaner.Util;

namespace WinCleaner.Commands;

public sealed class CleanJunkCommand : ICommand
{
    public string Name => "clean-junk";
    public string Summary => "Bereinigen (Standard: Dry-Run; --yes überspringt Abfrage)";
    public string Usage => "[--no-dry-run] [--yes]";
    public string[] AllowedFlags => new[] { "--no-dry-run", "--yes" };

    public int Execute(CommandContext ctx)
    {
        bool dryRun = !ctx.HasFlag("--no-dry-run");
        var report = new JunkScanner(ctx.Logger).Scan();

        // Vor echter Bereinigung bestätigen lassen (außer --yes).
        if (!dryRun && !ctx.HasFlag("--yes") && !ConfirmClean(report))
        {
            Console.Error.WriteLine("Abgebrochen.");
            return 1;
        }

        new JunkCleaner(ctx.Logger).Clean(report, dryRun: dryRun, sendToRecycleBin: true);
        Console.WriteLine(dryRun
            ? "Dry-Run abgeschlossen. Nutze --no-dry-run für echte Bereinigung."
            : "Bereinigung abgeschlossen.");
        return 0;
    }

    // Bestätigung vor echtem Löschen (Prompts nach stderr -> --json bleibt sauber).
    // Nur "Safe"-Einträge werden bereinigt.
    private static bool ConfirmClean(JunkReport report)
    {
        var safe = report.Items.Where(i => i.Safety == Safety.Safe).ToList();
        long bytes = safe.Sum(i => i.TotalBytes);
        int files = safe.Sum(i => i.FileCount);

        Console.Error.WriteLine($"\n{files} Dateien ({DiskAnalyzer.FormatSize(bytes)}) werden in den " +
                                "Papierkorb verschoben (nur als sicher eingestufte Kategorien).");

        return Prompt.Confirm("Fortfahren?");
    }
}
