using WinCleaner.Core;
using WinCleaner.Util;

namespace WinCleaner.Commands;

public sealed class ScanJunkCommand : ICommand
{
    public string Name => "scan-junk";
    public string Summary => "Junk-Dateien auflisten (kein Löschen)";
    public string Usage => "";

    public int Execute(CommandContext ctx)
    {
        var report = new JunkScanner(ctx.Logger).Scan();

        if (ctx.Json)
        {
            JsonOut.Write(new { report.TotalFiles, report.TotalBytes, report.Items });
            return 0;
        }

        ConsoleTable.From(
            report.Items.Select(i => new[]
            {
                i.Category, i.Path, i.FileCount.ToString(),
                (i.TotalBytes / (1024 * 1024.0)).ToString("N1")
            }),
            "Kategorie", "Pfad", "Dateien", "Größe (MB)"
        ).Write();

        Console.WriteLine($"\nGesamt: {report.TotalFiles} Dateien, {(report.TotalBytes / (1024 * 1024.0)):N1} MB.");
        return 0;
    }
}
