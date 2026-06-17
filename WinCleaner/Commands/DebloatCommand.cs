using WinCleaner.SystemTools;
using WinCleaner.Util;

namespace WinCleaner.Commands;

/// <summary>
/// Entfernt kuratierte, unkritische Bloatware-Appx-Apps des aktuellen Benutzers
/// (z. B. Bing-News/Wetter, Solitaire, GetHelp, Feedback, Maps, People,
/// Clipchamp, Mixed Reality, Xbox-Gaming-Overlay). Sicherheitskritische bzw.
/// systemrelevante Pakete (Store, Defender, .NET-/VCLibs-Runtimes, Terminal,
/// Rechner, WebView2, App-Installer …) werden NIEMALS angefasst.
///
/// Standard ist ein DRY-RUN: es wird nur gezeigt, was entfernt würde. Echtes
/// Entfernen nur mit <c>--no-dry-run --yes</c>. Vor dem echten Entfernen wird –
/// falls Adminrechte vorhanden sind – ein Wiederherstellungspunkt erstellt.
/// Die Entfernung erfolgt per-User und ist umkehrbar (Neuinstallation über den
/// Microsoft Store möglich).
/// </summary>
public sealed class DebloatCommand : ICommand
{
    public string Name => "debloat";
    public string Summary => "Kuratierte Bloatware-Apps entfernen (Dry-Run-Default, umkehrbar via Store)";
    public string Usage => "[--list] [--no-dry-run] [--yes]";
    public string[] AllowedFlags => new[] { "--list", "--no-dry-run", "--yes" };

    /// <summary>
    /// Kuratierte Whitelist sicher entfernbarer Apps. Vergleich erfolgt gegen den
    /// Appx-<c>Name</c> (Groß-/Kleinschreibung egal, exakter Treffer). Bewusst
    /// konservativ: nur unkritische, nicht systemrelevante Apps.
    /// </summary>
    private static readonly string[] RemovableNames =
    {
        "Microsoft.BingNews",
        "Microsoft.BingWeather",
        "Microsoft.BingFinance",
        "Microsoft.BingSports",
        "Microsoft.BingSearch",
        "Microsoft.WindowsMaps",
        "Microsoft.People",
        "Microsoft.MicrosoftSolitaireCollection",
        "Microsoft.GetHelp",
        "Microsoft.Getstarted",                 // "Tipps"
        "Microsoft.WindowsFeedbackHub",
        "Microsoft.Microsoft3DViewer",
        "Microsoft.MixedReality.Portal",
        "Microsoft.MicrosoftOfficeHub",         // "Holen Sie sich Office" / Werbe-Hub
        "Microsoft.OfficePushNotificationUtility",
        "Microsoft.SkypeApp",
        "Microsoft.Todos",
        "Microsoft.PowerAutomateDesktop",
        "Microsoft.Clipchamp",
        "Clipchamp.Clipchamp",
        "Microsoft.XboxGamingOverlay",
        "Microsoft.XboxGameOverlay",
        "Microsoft.Xbox.TCUI",
        "Microsoft.XboxSpeechToTextOverlay",
        "Microsoft.ZuneMusic",                  // "Groove-Musik" / Media Player (Legacy)
        "Microsoft.ZuneVideo",                  // "Filme & TV"
        "Microsoft.WindowsCommunicationsApps",  // Mail & Kalender
        "MicrosoftCorporationII.QuickAssist",
        "MicrosoftTeams",                       // vorinstalliertes Consumer-Teams
        "Microsoft.Windows.DevHome",
    };

    public int Execute(CommandContext ctx)
    {
        var mgr = new AppxManager(ctx.Logger);
        var installed = mgr.ListInstalled();

        // --list: alle installierten Appx-Apps zeigen (kein Entfernen).
        if (ctx.HasFlag("--list"))
        {
            EmitList(ctx, installed);
            return 0;
        }

        // Schnittmenge aus Whitelist und tatsächlich installierten Apps bilden.
        var removeSet = new HashSet<string>(RemovableNames, StringComparer.OrdinalIgnoreCase);
        var candidates = installed
            .Where(p => removeSet.Contains(p.Name))
            .GroupBy(p => p.PackageFullName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        bool dryRun = !ctx.HasFlag("--no-dry-run");

        // JSON: maschinenlesbare Vorschau/Ergebnis, keine Deko auf stdout.
        if (ctx.Json && dryRun)
        {
            JsonOut.Write(new
            {
                dryRun       = true,
                wuerdeEntfernen = candidates.Select(c => new { c.Name, c.PackageFullName }),
                anzahl       = candidates.Count
            });
            return 0;
        }

        if (candidates.Count == 0)
        {
            if (ctx.Json)
                JsonOut.Write(new { dryRun, entfernt = Array.Empty<object>(), anzahl = 0 });
            else
                Console.WriteLine("Keine kuratierte Bloatware gefunden – nichts zu entfernen.");
            return 0;
        }

        if (dryRun)
        {
            // DRY-RUN (Default): nur anzeigen, was entfernt würde.
            Console.WriteLine($"DRY-RUN: {candidates.Count} App(s) würden entfernt (per-User, umkehrbar über den Store):");
            var rows = candidates.Select(c => new[] { c.Name, c.PackageFullName });
            ConsoleTable.From(rows, "Name", "PackageFullName").Write();
            // Leerzeile gehört optisch zur Tabelle -> auf stdout statt stderr.
            Console.WriteLine();
            ctx.Logger.Info("Dies war ein Probelauf. Mit --no-dry-run --yes wirklich entfernen.");
            return 0;
        }

        // Ab hier: echtes Entfernen gewünscht (--no-dry-run).
        ctx.Logger.Info($"{candidates.Count} App(s) zum Entfernen vorgemerkt (per-User):");
        foreach (var c in candidates)
            ctx.Logger.Info($"  - {c.Name}");
        ctx.Logger.Info("Hinweis: Entfernung gilt nur für den aktuellen Benutzer und ist über den Microsoft Store umkehrbar.");

        if (!ctx.HasFlag("--yes"))
        {
            // Bei --json keinen interaktiven Prompt: definiertes JSON auf stdout
            // statt "Abgebrochen." auf stderr (konsistent zu den anderen --json-Pfaden).
            if (ctx.Json)
            {
                JsonOut.Write(new { abgebrochen = true, grund = "--yes fehlt" });
                return 1;
            }

            if (!Prompt.Confirm($"{candidates.Count} App(s) jetzt wirklich entfernen?"))
            {
                Console.Error.WriteLine("Abgebrochen.");
                return 1;
            }
        }

        // Vor system-/benutzerweiten Änderungen, wenn Admin: Wiederherstellungspunkt.
        if (Elevation.IsAdministrator())
            new RestorePoint(ctx.Logger).Create("WinCleaner Debloat");

        var removed = new List<AppxPackage>();
        var failed = new List<AppxPackage>();
        foreach (var c in candidates)
        {
            if (mgr.Remove(c.PackageFullName))
                removed.Add(c);
            else
                failed.Add(c);
        }

        if (ctx.Json)
        {
            JsonOut.Write(new
            {
                dryRun       = false,
                entfernt     = removed.Select(c => new { c.Name, c.PackageFullName }),
                fehlgeschlagen = failed.Select(c => new { c.Name, c.PackageFullName }),
                anzahlEntfernt = removed.Count
            });
        }
        else
        {
            Console.WriteLine($"\n{removed.Count} von {candidates.Count} App(s) entfernt.");
            if (failed.Count > 0)
                Console.WriteLine($"{failed.Count} App(s) konnten nicht entfernt werden (Details siehe oben).");
            Console.WriteLine("Entfernte Apps lassen sich jederzeit über den Microsoft Store neu installieren.");
        }

        return failed.Count == 0 ? 0 : 2;
    }

    /// <summary>Gibt alle installierten Appx-Apps als JSON oder Tabelle aus (stdout).</summary>
    private static void EmitList(CommandContext ctx, List<AppxPackage> installed)
    {
        if (ctx.Json)
        {
            JsonOut.Write(installed.Select(p => new { p.Name, p.PackageFullName }));
            return;
        }

        if (installed.Count == 0)
        {
            Console.WriteLine("Keine Appx-Apps gefunden (oder Auflistung fehlgeschlagen – siehe stderr).");
            return;
        }

        var rows = installed.Select(p => new[] { p.Name, p.PackageFullName });
        ConsoleTable.From(rows, "Name", "PackageFullName").Write();
    }
}
