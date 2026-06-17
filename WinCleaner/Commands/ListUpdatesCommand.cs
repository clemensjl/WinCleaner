using WinCleaner.SystemTools;
using WinCleaner.Util;

namespace WinCleaner.Commands;

/// <summary>
/// Listet verfügbare Paket-Updates über <c>winget upgrade --include-unknown</c>.
/// Reiner Lese-Befehl ohne Änderungen am System.
/// </summary>
public sealed class ListUpdatesCommand : ICommand
{
    public string Name => "list-updates";
    public string Summary => "Verfügbare Paket-Updates anzeigen (winget)";
    public string Usage => "[--json]";
    public string[] AllowedFlags => Array.Empty<string>();

    public int Execute(CommandContext ctx)
    {
        var winget = new WingetWrapper(ctx.Logger);
        if (!winget.IsAvailable())
        {
            winget.ReportUnavailable();
            return 1;
        }

        ctx.Logger.Info("Suche nach verfügbaren Updates (winget)...");
        var updates = winget.ListUpdates();

        if (ctx.Json)
        {
            JsonOut.Write(updates);
            return 0;
        }

        if (updates.Count == 0)
        {
            Console.WriteLine("Keine Updates verfügbar.");
            return 0;
        }

        var rows = updates.Select(u => new[] { u.Name, u.Id, u.Current, u.Available });
        ConsoleTable.From(rows, "Name", "Id", "Aktuell", "Verfügbar").Write();
        Console.WriteLine($"\n{updates.Count} Update(s) verfügbar.");
        return 0;
    }
}
