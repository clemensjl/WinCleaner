using WinCleaner.Core;
using WinCleaner.Util;

namespace WinCleaner.Commands;

public sealed class FindDuplicatesCommand : ICommand
{
    public string Name => "find-duplicates";
    public string Summary => "Doppelte Dateien finden/löschen oder per Hardlink ersetzen (Papierkorb, mit Rückfrage)";

    public string Usage =>
        "<Pfad> [--delete] [--keep oldest|newest|shortest-path|longest-path] " +
        "[--protect <Pfad[,Pfad...]>] [--hard-link] [--cache] [--no-dry-run] [--yes]";

    public string[] AllowedFlags => new[]
    {
        "--delete", "--yes", "--keep", "--protect", "--hard-link", "--cache", "--no-dry-run"
    };

    public int Execute(CommandContext ctx)
    {
        var path = ctx.FirstPositional;
        if (path is null)
        {
            Console.Error.WriteLine($"{Name} {Usage}");
            return 1;
        }

        // --keep-Strategie früh parsen, damit Tippfehler sofort gemeldet werden.
        KeepStrategy keep;
        try { keep = DuplicateFinder.ParseKeepStrategy(ctx.Option("--keep")); }
        catch (ArgumentException ex) { ctx.Logger.Error(ex.Message); return 1; }

        var protectedPaths = KeepProtectOptions.ParseProtected(ctx);
        bool hardLink = ctx.HasFlag("--hard-link");
        bool delete = ctx.HasFlag("--delete");

        // --delete und --hard-link schließen sich gegenseitig aus.
        if (delete && hardLink)
        {
            ctx.Logger.Error("--delete und --hard-link schließen sich gegenseitig aus – bitte genau eine Aktion wählen.");
            return 1;
        }

        // Eine Aktion (Löschen/Hardlink) findet nur bei --delete oder --hard-link statt.
        bool wantsAction = delete || hardLink;

        // Sicherheits-Default: Probelauf. Echte Aktion nur mit --no-dry-run.
        bool dryRun = !ctx.HasFlag("--no-dry-run");

        // Opt-in: persistenter Hash-Cache beschleunigt Wiederholungsläufe.
        HashCache? cache = null;
        if (ctx.HasFlag("--cache"))
            cache = HashCache.Load(HashCache.DefaultPath, ctx.Logger);

        var finder = new DuplicateFinder(ctx.Logger);
        var groups = finder.Find(path, cache);
        cache?.Save();

        // Nutzdaten (Fundliste) nach stdout.
        if (ctx.Json && !wantsAction)
        {
            JsonOut.Write(groups);
        }
        else if (!ctx.Json)
        {
            foreach (var g in groups)
            {
                Console.WriteLine($"\nHASH {g.Hash}  Dateien: {g.Files.Count}  Gesamt: {(g.TotalBytes / (1024 * 1024.0)):N1} MB");
                foreach (var f in g.Files) Console.WriteLine("  " + f);
            }
        }

        if (!wantsAction)
        {
            // Reiner Suchlauf: bei --json wurde oben bereits JSON ausgegeben.
            return 0;
        }

        if (groups.Count == 0)
        {
            if (ctx.Json) JsonOut.Write(new DuplicateActionResult(0, 0, 0, 0, 0, dryRun, hardLink, true,
                Array.Empty<DuplicateFileAction>()));
            else Console.WriteLine("\nKeine Duplikate zur Bearbeitung.");
            return 0;
        }

        // Zusammenfassung der geplanten Aktion nach stderr.
        string aktion = hardLink ? "durch NTFS-Hardlinks ersetzt" : "in den Papierkorb verschoben";
        ctx.Logger.Info($"Behalte-Strategie: {KeepLabel(keep)}.");
        if (protectedPaths.Count > 0)
            ctx.Logger.Info($"Geschützte Pfade (werden nie verändert): {string.Join(", ", protectedPaths)}");

        if (dryRun)
        {
            // Probelauf: verändert nichts, ermittelt nur die geplante Aktion.
            ctx.Logger.Info($"Probelauf (Standard) – es wird NICHTS verändert. Mit --no-dry-run echt ausführen.");
            var planned = finder.ProcessDuplicates(groups, keep, protectedPaths, hardLink,
                sendToRecycleBin: true, dryRun: true);
            return ReportAction(ctx, planned, aktion);
        }

        // Echte Aktion bestätigen (außer --yes); Prompt nach stderr.
        if (!ctx.HasFlag("--yes") && !ConfirmAction(groups, hardLink, protectedPaths, keep))
        {
            Console.Error.WriteLine("Abgebrochen.");
            return 1;
        }

        var result = finder.ProcessDuplicates(groups, keep, protectedPaths, hardLink,
            sendToRecycleBin: true, dryRun: false);

        return ReportAction(ctx, result, aktion);
    }

    private static int ReportAction(CommandContext ctx, DuplicateActionResult r, string aktion)
    {
        if (ctx.Json)
        {
            JsonOut.Write(r);
        }
        else
        {
            string praefix = r.DryRun ? "[Probelauf] Es würden" : "Es wurden";
            string verb = r.DryRun ? aktion.Replace("ersetzt", "ersetzt werden").Replace("verschoben", "verschoben werden") : aktion;
            Console.WriteLine($"\n{praefix} {r.FilesAffected} Duplikate {verb} " +
                              $"({DiskAnalyzer.FormatSize(r.BytesAffected)}, je Gruppe eine Datei behalten).");
            if (r.GroupsSkipped > 0)
                Console.WriteLine($"{r.GroupsSkipped} Gruppe(n) übersprungen (geschützt oder nicht verarbeitbar).");
            if (r.FilesSkipped > 0)
                Console.WriteLine($"{r.FilesSkipped} Datei(en) übersprungen (Details auf stderr, z. B. schon verlinkt oder anderes Volume).");
        }
        return 0;
    }

    private static bool ConfirmAction(List<DuplicateGroup> groups, bool hardLink,
        IReadOnlyCollection<string> protectedPaths, KeepStrategy keep)
    {
        // Grobe Schätzung der betroffenen Dateien/Bytes (je Gruppe eine behalten).
        int files = groups.Sum(g => Math.Max(0, g.Files.Count - 1));
        long bytes = groups.Sum(g => g.Files.Count > 0
            ? (g.TotalBytes / g.Files.Count) * (g.Files.Count - 1)
            : 0);

        string aktion = hardLink
            ? "durch NTFS-Hardlinks auf die behaltene Datei ersetzt (kein Datenverlust)"
            : "in den Papierkorb verschoben";

        Console.Error.WriteLine($"\nBis zu {files} doppelte Dateien ({DiskAnalyzer.FormatSize(bytes)}) werden {aktion} " +
                                $"(je Gruppe bleibt eine erhalten, Strategie: {KeepLabel(keep)}).");
        if (protectedPaths.Count > 0)
            Console.Error.WriteLine($"Geschützte Pfade bleiben unangetastet: {string.Join(", ", protectedPaths)}");

        return Prompt.Confirm("Fortfahren?");
    }

    private static string KeepLabel(KeepStrategy keep) => KeepProtectOptions.KeepLabel(keep);
}
