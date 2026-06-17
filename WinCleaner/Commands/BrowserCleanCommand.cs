using WinCleaner.Core;
using WinCleaner.Util;

namespace WinCleaner.Commands;

/// <summary>
/// Granulares Browser-Cleaning pro Browser und Profil. Ermittelt für Chrome,
/// Edge, Brave (Chromium) und Firefox je Profil die Größe der Kategorien Cache,
/// Cookies, Verlauf und Sessions. Standard ist ein DRY-RUN (zeigt nur an, was
/// gelöscht würde); echtes Löschen erfordert <c>--no-dry-run</c> und <c>--yes</c>
/// und erfolgt umkehrbar in den Papierkorb. Cache ist immer dabei; Cookies,
/// Verlauf und Sessions sind Opt-in, weil dabei Logins/Verlauf/offene Tabs
/// verloren gehen.
/// </summary>
public sealed class BrowserCleanCommand : ICommand
{
    public string Name => "browser-clean";
    public string Summary => "Browser-Cache/Cookies/Verlauf/Sessions pro Profil anzeigen/löschen";
    public string Usage => "[--browser chrome|edge|brave|firefox] [--cookies] [--history] [--sessions] [--no-dry-run] [--yes]";
    public string[] AllowedFlags => new[]
    {
        "--browser", "--cookies", "--history", "--sessions", "--no-dry-run", "--yes"
    };

    public int Execute(CommandContext ctx)
    {
        // 1) Browser-Auswahl bestimmen (mehrfach --browser und/oder kommagetrennt; Default alle).
        var browsers = ParseBrowsers(ctx);
        if (browsers.Count == 0)
        {
            ctx.Logger.Error("Keine gültigen Browser ausgewählt (erlaubt: chrome, edge, brave, firefox).");
            return 1;
        }

        // 2) Kategorien: Cache immer, der Rest per Opt-in.
        var categories = BrowserCategory.Cache;
        if (ctx.HasFlag("--cookies"))  categories |= BrowserCategory.Cookies;
        if (ctx.HasFlag("--history"))  categories |= BrowserCategory.History;
        if (ctx.HasFlag("--sessions")) categories |= BrowserCategory.Sessions;

        bool dryRun = !ctx.HasFlag("--no-dry-run");

        var cleaner = new BrowserCleaner(ctx.Logger);
        List<BrowserTarget> targets;
        try
        {
            targets = cleaner.Collect(browsers, categories);
        }
        catch (Exception ex)
        {
            ctx.Logger.Error($"Browser-Analyse fehlgeschlagen: {ex.Message}");
            return 2;
        }

        long totalBytes = targets.Sum(t => t.TotalBytes);
        int totalFiles = targets.Sum(t => t.FileCount);

        // 3) Größenübersicht. Im JSON-Modus NICHT sofort schreiben, sondern bis
        //    nach dem (eventuellen) Löschen aufschieben, damit pro Lauf genau EIN
        //    Top-Level-JSON-Objekt entsteht (sonst ungültiges JSON).
        if (!ctx.Json)
        {
            if (targets.Count == 0)
            {
                Console.WriteLine("Nichts gefunden – keine löschbaren Browser-Daten für die Auswahl.");
            }
            else
            {
                var rows = targets.Select(t => new[]
                {
                    t.Browser,
                    t.Profile,
                    t.Category.ToString(),
                    t.FileCount.ToString(),
                    DiskAnalyzer.FormatSize(t.TotalBytes)
                });
                ConsoleTable.From(rows, "Browser", "Profil", "Kategorie", "Dateien", "Größe").Write();
                Console.WriteLine($"\nGesamt: {totalFiles} Dateien, {DiskAnalyzer.FormatSize(totalBytes)}.");
            }
        }

        // 4) Im Dry-Run (Default) nichts löschen – nur Hinweis nach stderr.
        if (dryRun)
        {
            // Im JSON-Modus das einzige Objekt jetzt ausgeben (analysierte items, kein Löschen).
            if (ctx.Json)
                WriteJson(dryRun: true, browsers, totalBytes, totalFiles, targets, deletedPaths: 0);
            ctx.Logger.Info("DRY-RUN (Standard): Es wurde NICHTS gelöscht. Für echtes Löschen " +
                            "--no-dry-run --yes setzen.");
            return 0;
        }

        if (targets.Count == 0)
        {
            // Nichts zu löschen: trotzdem das einzige JSON-Objekt emittieren.
            if (ctx.Json)
                WriteJson(dryRun: false, browsers, totalBytes, totalFiles, targets, deletedPaths: 0);
            return 0;
        }

        // 5) Echtes Löschen: Warnung + Bestätigung (außer --yes).
        ctx.Logger.Info($"ECHTES LÖSCHEN: {totalFiles} Dateien ({DiskAnalyzer.FormatSize(totalBytes)}) " +
                        "werden in den Papierkorb verschoben.");
        ctx.Logger.Info("Hinweis: Geöffnete Browser sperren ihre Dateien – diese werden " +
                        "übersprungen. Browser vorher schließen.");
        if (categories != BrowserCategory.Cache)
            ctx.Logger.Info("Achtung: Cookies/Verlauf/Sessions entfernen Logins, Chronik und " +
                            "offene Tabs.");

        if (!ctx.HasFlag("--yes") && !Prompt.Confirm("Fortfahren?"))
        {
            Console.Error.WriteLine("Abgebrochen.");
            return 1;
        }

        int deleted = cleaner.Delete(targets);

        if (ctx.Json)
        {
            // Einziges JSON-Objekt des Laufs: analysierte items + tatsächlich gelöschte Pfade.
            WriteJson(dryRun: false, browsers, totalBytes, totalFiles, targets, deletedPaths: deleted);
        }
        else
        {
            Console.WriteLine($"\n{deleted} Pfad(e) in den Papierkorb verschoben.");
        }

        return 0;
    }

    /// <summary>
    /// Schreibt das EINE Top-Level-JSON-Objekt pro Lauf. <paramref name="totalBytes"/>
    /// und <paramref name="totalFiles"/> sowie <paramref name="targets"/> beschreiben die
    /// analysierten Daten; <paramref name="deletedPaths"/> die Anzahl der tatsächlich in den
    /// Papierkorb verschobenen Pfade (0 im Dry-Run). Felder bewusst unterschiedlich benannt,
    /// damit analysierte Items nicht mit gelöschten Pfaden verwechselt werden.
    /// </summary>
    private static void WriteJson(
        bool dryRun,
        List<string> browsers,
        long totalBytes,
        int totalFiles,
        List<BrowserTarget> targets,
        int deletedPaths)
    {
        var items = targets.Select(t => new BrowserCleanResult(
            t.Browser, t.Profile, t.Category.ToString(), t.TotalBytes, t.FileCount,
            Deleted: !dryRun)).ToList();
        JsonOut.Write(new
        {
            dryRun,
            browsers,
            analyzedBytes = totalBytes,
            analyzedFiles = totalFiles,
            deletedPaths,
            items
        });
    }

    /// <summary>
    /// Wertet alle <c>--browser</c>-Angaben aus (mehrfach und/oder kommagetrennt)
    /// und liefert die eindeutige Liste gültiger Browser-Schlüssel. Ohne Angabe
    /// werden alle unterstützten Browser zurückgegeben.
    /// </summary>
    private static List<string> ParseBrowsers(CommandContext ctx)
    {
        // Alle Werte zu "--browser" einsammeln (Option() liefert nur den ersten Treffer,
        // daher Args selbst durchlaufen, um Mehrfachangaben zu unterstützen).
        var raw = new List<string>();
        var args = ctx.Args;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith("--browser=", StringComparison.OrdinalIgnoreCase))
                raw.Add(a["--browser=".Length..]);
            else if (a.Equals("--browser", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                raw.Add(args[++i]);
        }

        if (raw.Count == 0)
            return BrowserCleaner.AllBrowsers.ToList();

        var result = new List<string>();
        foreach (var entry in raw)
        {
            foreach (var part in entry.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var key = part.ToLowerInvariant();
                if (BrowserCleaner.AllBrowsers.Contains(key))
                {
                    if (!result.Contains(key)) result.Add(key);
                }
                else
                {
                    ctx.Logger.Error($"Unbekannter Browser '{part}' wird ignoriert " +
                                     "(erlaubt: chrome, edge, brave, firefox).");
                }
            }
        }
        return result;
    }
}
