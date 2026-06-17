using WinCleaner.SystemTools;
using WinCleaner.Util;

namespace WinCleaner.Commands;

/// <summary>
/// Aktualisiert alle installierten Pakete über winget. Sicherheitskonform:
/// Standard ist ein Dry-Run, der nur anzeigt, was aktualisiert würde. Das echte
/// Upgrade läuft erst mit <c>--no-dry-run</c> (oder <c>--yes</c>) und nach
/// Bestätigung. Bei system-weiter Änderung (Admin) wird vorab ein
/// Wiederherstellungspunkt versucht.
/// </summary>
public sealed class UpdateCommand : ICommand
{
    public string Name => "update";
    public string Summary => "Alle Pakete aktualisieren (Standard: Dry-Run; --no-dry-run für echt)";
    public string Usage => "[--no-dry-run] [--yes]";
    public string[] AllowedFlags => new[] { "--no-dry-run", "--yes" };

    public int Execute(CommandContext ctx)
    {
        var winget = new WingetWrapper(ctx.Logger);
        if (!winget.IsAvailable())
        {
            winget.ReportUnavailable();
            return 1;
        }

        bool dryRun = !ctx.HasFlag("--no-dry-run");

        // Verfügbare Updates ermitteln (dient zugleich als Dry-Run-Vorschau).
        ctx.Logger.Info("Suche nach verfügbaren Updates (winget)...");
        var updates = winget.ListUpdates();

        if (updates.Count == 0)
        {
            if (ctx.Json) JsonOut.Write(new { dryRun, updated = false, updates });
            else Console.WriteLine("Keine Updates verfügbar.");
            return 0;
        }

        // Dry-Run: nur anzeigen, was aktualisiert würde – nichts ändern.
        if (dryRun)
        {
            if (ctx.Json)
            {
                JsonOut.Write(new { dryRun = true, updated = false, updates });
            }
            else
            {
                var rows = updates.Select(u => new[] { u.Name, u.Id, u.Current, u.Available });
                ConsoleTable.From(rows, "Name", "Id", "Aktuell", "Verfügbar").Write();
                Console.WriteLine($"\nDry-Run: {updates.Count} Paket(e) würden aktualisiert. " +
                                  "Nutze --no-dry-run für das echte Upgrade.");
            }
            return 0;
        }

        // Echtes Upgrade: zusammenfassen und bestätigen lassen (außer --yes).
        Console.Error.WriteLine($"\n{updates.Count} Paket(e) werden aktualisiert (winget upgrade --all).");
        if (!ctx.HasFlag("--yes") && !Prompt.Confirm("Fortfahren?"))
        {
            Console.Error.WriteLine("Abgebrochen.");
            return 1;
        }

        // System-weite Änderung: bei Adminrechten vorab Wiederherstellungspunkt.
        if (Elevation.IsAdministrator())
            new RestorePoint(ctx.Logger).Create("WinCleaner Update");

        bool ok = winget.UpgradeAll();

        if (ctx.Json) JsonOut.Write(new { dryRun = false, updated = ok, updates });
        else Console.WriteLine(ok ? "Updates abgeschlossen." : "Updates teilweise/komplett fehlgeschlagen.");

        return ok ? 0 : 2;
    }
}
