using WinCleaner.Core;
using WinCleaner.Util;

namespace WinCleaner.Commands;

/// <summary>
/// Findet visuell aehnliche Bilder (skalierte/neu komprimierte Kopien) per
/// dHash und behandelt sie mit demselben Sicherheits-Flow wie find-duplicates:
/// Standard nur auflisten, Loeschen erst mit --delete --no-dry-run --yes,
/// immer in den Papierkorb, Behalte-Strategie und Schutzpfade identisch.
/// </summary>
public sealed class FindSimilarImagesCommand : ICommand
{
    private const int DefaultThreshold = 5;

    public string Name => "find-similar-images";
    public string Summary => "Visuell ähnliche Bilder finden/löschen (dHash; Papierkorb, mit Rückfrage)";

    public string Usage =>
        "<Pfad> [--threshold <0-16>] [--recurse] [--delete] " +
        "[--keep oldest|newest|shortest-path|longest-path] " +
        "[--protect <Pfad[,Pfad...]>] [--no-dry-run] [--yes]";

    public string[] AllowedFlags => new[]
    {
        "--threshold", "--recurse", "--delete", "--yes", "--keep", "--protect", "--no-dry-run"
    };

    public int Execute(CommandContext ctx)
    {
        var path = ctx.FirstPositional;
        if (path is null)
        {
            Console.Error.WriteLine($"{Name} {Usage}");
            return 1;
        }

        // --threshold streng parsen: Tippfehler/ausser Bereich sofort melden.
        int threshold = DefaultThreshold;
        var rawThreshold = ctx.Option("--threshold");
        if (rawThreshold is not null &&
            (!int.TryParse(rawThreshold, out threshold) || threshold < 0 || threshold > ImageSimilarityFinder.MaxThreshold))
        {
            ctx.Logger.Error($"Ungültiger --threshold '{rawThreshold}'. Erlaubt: 0-{ImageSimilarityFinder.MaxThreshold} (0 = nur exakt gleicher Hash).");
            return 1;
        }

        // --keep-Strategie frueh parsen, damit Tippfehler sofort gemeldet werden.
        KeepStrategy keep;
        try { keep = DuplicateFinder.ParseKeepStrategy(ctx.Option("--keep")); }
        catch (ArgumentException ex) { ctx.Logger.Error(ex.Message); return 1; }

        var protectedPaths = KeepProtectOptions.ParseProtected(ctx);
        bool wantsAction = ctx.HasFlag("--delete");

        // Sicherheits-Default: Probelauf. Echte Aktion nur mit --no-dry-run.
        bool dryRun = !ctx.HasFlag("--no-dry-run");

        var finder = new ImageSimilarityFinder(ctx.Logger);
        var groups = finder.Find(path, recurse: ctx.HasFlag("--recurse"), threshold: threshold);

        // Nutzdaten (Fundliste) nach stdout.
        if (ctx.Json && !wantsAction)
        {
            JsonOut.Write(groups);
        }
        else if (!ctx.Json)
        {
            foreach (var g in groups)
            {
                Console.WriteLine($"\ndHash {g.Hash}  Bilder: {g.Files.Count}  max. Abstand: {g.MaxDistance}  " +
                                  $"Gesamt: {DiskAnalyzer.FormatSize(g.TotalBytes)}");
                foreach (var f in g.Files) Console.WriteLine("  " + f);
            }
            if (groups.Count == 0) Console.WriteLine("Keine ähnlichen Bilder gefunden.");
        }

        if (!wantsAction) return 0;

        // Fuer Loeschen/Keep/Protect die bewaehrte Duplikat-Logik wiederverwenden
        // (TotalBytes = echte Summe der Dateigroessen je Gruppe).
        var dupGroups = groups
            .Select(g => new DuplicateGroup(g.Hash, g.Files, g.TotalBytes))
            .ToList();

        if (dupGroups.Count == 0)
        {
            if (ctx.Json) JsonOut.Write(new DuplicateActionResult(0, 0, 0, 0, 0, dryRun, false, true, Array.Empty<DuplicateFileAction>()));
            else Console.WriteLine("\nKeine ähnlichen Bilder zur Bearbeitung.");
            return 0;
        }

        ctx.Logger.Info($"Behalte-Strategie: {KeepProtectOptions.KeepLabel(keep)}.");
        if (protectedPaths.Count > 0)
            ctx.Logger.Info($"Geschützte Pfade (werden nie verändert): {string.Join(", ", protectedPaths)}");

        var dupFinder = new DuplicateFinder(ctx.Logger);

        if (dryRun)
        {
            ctx.Logger.Info("Probelauf (Standard) – es wird NICHTS verändert. Mit --no-dry-run echt ausführen.");
            var planned = dupFinder.ProcessDuplicates(dupGroups, keep, protectedPaths,
                hardLink: false, sendToRecycleBin: true, dryRun: true);
            return ReportAction(ctx, planned);
        }

        // Echte Aktion bestaetigen (ausser --yes); Prompt nach stderr.
        if (!ctx.HasFlag("--yes") && !ConfirmAction(dupGroups, protectedPaths, keep))
        {
            Console.Error.WriteLine("Abgebrochen.");
            return 1;
        }

        var result = dupFinder.ProcessDuplicates(dupGroups, keep, protectedPaths,
            hardLink: false, sendToRecycleBin: true, dryRun: false);

        return ReportAction(ctx, result);
    }

    private static int ReportAction(CommandContext ctx, DuplicateActionResult r)
    {
        if (ctx.Json)
        {
            JsonOut.Write(r);
        }
        else
        {
            string praefix = r.DryRun ? "[Probelauf] Es würden" : "Es wurden";
            string verb = r.DryRun ? "in den Papierkorb verschoben werden" : "in den Papierkorb verschoben";
            Console.WriteLine($"\n{praefix} {r.FilesAffected} ähnliche Bilder {verb} " +
                              $"({DiskAnalyzer.FormatSize(r.BytesAffected)}, je Gruppe eine Datei behalten).");
            if (r.GroupsSkipped > 0)
                Console.WriteLine($"{r.GroupsSkipped} Gruppe(n) übersprungen (geschützt oder nicht verarbeitbar).");
        }
        return 0;
    }

    private static bool ConfirmAction(List<DuplicateGroup> groups,
        IReadOnlyCollection<string> protectedPaths, KeepStrategy keep)
    {
        int files = groups.Sum(g => Math.Max(0, g.Files.Count - 1));
        long bytes = groups.Sum(g => g.Files.Count > 0
            ? (g.TotalBytes / g.Files.Count) * (g.Files.Count - 1)
            : 0);

        Console.Error.WriteLine($"\nBis zu {files} ähnliche Bilder ({DiskAnalyzer.FormatSize(bytes)}) werden in den " +
                                $"Papierkorb verschoben (je Gruppe bleibt eines erhalten, Strategie: {KeepProtectOptions.KeepLabel(keep)}).");
        Console.Error.WriteLine("Achtung: \"ähnlich\" heißt nicht byte-identisch – Fundliste vorher prüfen.");
        if (protectedPaths.Count > 0)
            Console.Error.WriteLine($"Geschützte Pfade bleiben unangetastet: {string.Join(", ", protectedPaths)}");

        return Prompt.Confirm("Fortfahren?");
    }
}
