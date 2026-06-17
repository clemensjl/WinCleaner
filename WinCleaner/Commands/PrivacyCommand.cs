using WinCleaner.SystemTools;
using WinCleaner.Util;

namespace WinCleaner.Commands;

/// <summary>
/// Wendet kuratierte, umkehrbare Privacy-Tweaks an (Telemetrie, Tracking, KI:
/// Copilot/Recall) oder macht sie rückgängig und zeigt deren Status.
/// Sicherheit: <c>--apply</c> und <c>--undo</c> laufen standardmäßig als
/// Trockenlauf; die echte Änderung erfolgt nur mit <c>--no-dry-run</c> nach
/// Bestätigung (oder <c>--yes</c>). Vor systemweiten (HKLM-)Änderungen wird mit
/// Adminrechten neu gestartet und ein Wiederherstellungspunkt erstellt. Alle
/// Tweaks sind über <see cref="TweakEngine"/> reversibel.
/// </summary>
public sealed class PrivacyCommand : ICommand
{
    public string Name => "privacy";
    public string Summary => "Privacy-/Telemetrie-/KI-Tweaks anwenden, rückgängig machen oder prüfen (umkehrbar)";
    public string Usage => "[--status] | --apply [standard|advanced] | --undo [--no-dry-run] [--yes]";
    public string[] AllowedFlags => new[] { "--apply", "--undo", "--status", "--no-dry-run", "--yes" };

    public int Execute(CommandContext ctx)
    {
        bool apply = ctx.HasFlag("--apply");
        bool undo  = ctx.HasFlag("--undo");

        if (apply && undo)
        {
            ctx.Logger.Error("--apply und --undo schließen sich gegenseitig aus.");
            return 1;
        }

        if (apply) return RunApply(ctx);
        if (undo)  return RunUndo(ctx);

        // Default = Status anzeigen (read-only).
        return RunStatus(ctx);
    }

    // ------------------------------------------------------------------ APPLY

    private int RunApply(CommandContext ctx)
    {
        // Profil aus --apply=<wert> ODER erstem Positional (z. B. "privacy --apply advanced").
        string? profileArg = ctx.Option("--apply") ?? ctx.FirstPositional;
        var profile = PrivacyTweaks.ParseProfile(profileArg);
        if (profile is null)
        {
            ctx.Logger.Error($"Unbekanntes Profil \"{profileArg}\". Erlaubt: standard | advanced.");
            return 1;
        }

        var tweaks = PrivacyTweaks.ForProfile(profile.Value).ToList();
        bool needsAdmin = tweaks.Any(PrivacyTweaks.NeedsAdmin);
        bool dryRun = !ctx.HasFlag("--no-dry-run");

        // Trockenlauf: nur anzeigen, was passieren würde – nichts ändern.
        if (dryRun)
        {
            ctx.Logger.Info($"Trockenlauf (Profil: {profile}). Es wird nichts geändert. " +
                            "Mit --no-dry-run anwenden.");
            PrintPlan(ctx, tweaks, "würde angewendet");
            return 0;
        }

        // Echte Anwendung: Adminrechte, wenn systemweite (HKLM-)Tweaks dabei sind.
        if (needsAdmin && !Elevation.IsAdministrator())
        {
            ctx.Logger.Info("Systemweite Tweaks dabei – Adminrechte nötig, starte mit Rechteerhöhung neu (UAC)...");
            return Elevation.RelaunchAsAdmin(ctx.FullArgs, ctx.Logger) ? 0 : 1;
        }

        // Zusammenfassung + Bestätigung (außer --yes); Diagnose/Rückfrage nach stderr.
        ctx.Logger.Info($"Folgende Privacy-Tweaks werden angewendet (Profil: {profile}, umkehrbar via --undo):");
        foreach (var t in tweaks) ctx.Logger.Info($"  • {t.Description}");

        if (!ctx.HasFlag("--yes") && !Prompt.Confirm("Fortfahren und Tweaks anwenden?"))
        {
            ctx.Logger.Error("Abgebrochen.");
            return 1;
        }

        // Vor systemweiten Änderungen Wiederherstellungspunkt (nur als Admin sinnvoll/möglich).
        if (needsAdmin && Elevation.IsAdministrator())
            new RestorePoint(ctx.Logger).Create("WinCleaner Privacy-Tweaks");

        var engine = new TweakEngine(ctx.Logger);
        var results = new List<(RegistryTweak Tweak, bool Ok)>();
        foreach (var t in tweaks)
            results.Add((t, engine.Apply(t)));

        int ok = results.Count(r => r.Ok);
        ReportResults(ctx, results, "angewendet");
        ctx.Logger.Info($"{ok} von {results.Count} Tweaks angewendet. Rückgängig mit: privacy --undo --no-dry-run --yes");

        Prompt.PauseIfRelaunched(ctx.FullArgs);
        return ok == results.Count ? 0 : 2;
    }

    // ------------------------------------------------------------------- UNDO

    private int RunUndo(CommandContext ctx)
    {
        var engine = new TweakEngine(ctx.Logger);

        // Nur Tweaks rückgängig machen, für die eine Sicherung existiert (von WinCleaner gesetzt).
        var toUndo = PrivacyTweaks.AllTweaks.Where(t => engine.HasBackup(t.Id)).ToList();
        if (toUndo.Count == 0)
        {
            ctx.Logger.Info("Keine von WinCleaner gesetzten Privacy-Tweaks gefunden – nichts rückgängig zu machen.");
            return 0;
        }

        bool needsAdmin = toUndo.Any(PrivacyTweaks.NeedsAdmin);
        bool dryRun = !ctx.HasFlag("--no-dry-run");

        if (dryRun)
        {
            ctx.Logger.Info("Trockenlauf. Es wird nichts geändert. Mit --no-dry-run rückgängig machen.");
            PrintPlan(ctx, toUndo, "würde rückgängig gemacht");
            return 0;
        }

        if (needsAdmin && !Elevation.IsAdministrator())
        {
            ctx.Logger.Info("Systemweite Tweaks betroffen – Adminrechte nötig, starte mit Rechteerhöhung neu (UAC)...");
            return Elevation.RelaunchAsAdmin(ctx.FullArgs, ctx.Logger) ? 0 : 1;
        }

        ctx.Logger.Info("Folgende Privacy-Tweaks werden rückgängig gemacht (Ausgangszustand wiederherstellen):");
        foreach (var t in toUndo) ctx.Logger.Info($"  • {t.Description}");

        if (!ctx.HasFlag("--yes") && !Prompt.Confirm("Fortfahren und Tweaks rückgängig machen?"))
        {
            ctx.Logger.Error("Abgebrochen.");
            return 1;
        }

        // Vor systemweiten Änderungen Wiederherstellungspunkt (nur als Admin sinnvoll/möglich).
        if (needsAdmin && Elevation.IsAdministrator())
            new RestorePoint(ctx.Logger).Create("WinCleaner Privacy-Undo");

        var results = new List<(RegistryTweak Tweak, bool Ok)>();
        foreach (var t in toUndo)
            results.Add((t, engine.Undo(t)));

        int ok = results.Count(r => r.Ok);
        ReportResults(ctx, results, "rückgängig gemacht");
        ctx.Logger.Info($"{ok} von {results.Count} Tweaks rückgängig gemacht.");

        Prompt.PauseIfRelaunched(ctx.FullArgs);
        return ok == results.Count ? 0 : 2;
    }

    // ----------------------------------------------------------------- STATUS

    private int RunStatus(CommandContext ctx)
    {
        var engine = new TweakEngine(ctx.Logger);
        var rows = PrivacyTweaks.All.Select(e =>
        {
            var status = engine.Status(e.Tweak);
            return new
            {
                id = e.Tweak.Id,
                description = e.Tweak.Description,
                profile = e.Profile.ToString(),
                hive = e.Tweak.Hive.ToString(),
                status = status.ToString(),
                applied = status == TweakStatus.Applied
            };
        }).ToList();

        if (ctx.Json)
        {
            JsonOut.Write(rows);
            return 0;
        }

        var table = rows.Select(r => new[]
        {
            r.description,
            r.profile,
            r.hive,
            DescribeStatus(Enum.Parse<TweakStatus>(r.status))
        });
        ConsoleTable.From(table, "Tweak", "Profil", "Hive", "Status").Write();

        int applied = rows.Count(r => r.applied);
        Console.WriteLine($"\n{applied} von {rows.Count} Privacy-Tweaks aktiv.");
        return 0;
    }

    // ----------------------------------------------------------------- HELPER

    /// <summary>Zeigt den Plan im Trockenlauf (stdout = Nutzdaten, JSON-fähig).</summary>
    private static void PrintPlan(CommandContext ctx, List<RegistryTweak> tweaks, string verbLabel)
    {
        if (ctx.Json)
        {
            JsonOut.Write(tweaks.Select(t => new
            {
                id = t.Id,
                description = t.Description,
                hive = t.Hive.ToString(),
                action = verbLabel,
                dryRun = true
            }));
            return;
        }

        Console.WriteLine($"Trockenlauf – folgende Tweaks {verbLabel}:");
        foreach (var t in tweaks)
            Console.WriteLine($"  • {t.Description} ({t.Hive})");
    }

    /// <summary>Berichtet das Ergebnis je Tweak (JSON auf stdout, sonst Klartext).</summary>
    private static void ReportResults(CommandContext ctx,
        List<(RegistryTweak Tweak, bool Ok)> results, string verbLabel)
    {
        if (ctx.Json)
        {
            JsonOut.Write(results.Select(r => new
            {
                id = r.Tweak.Id,
                description = r.Tweak.Description,
                action = verbLabel,
                success = r.Ok
            }));
            return;
        }

        foreach (var r in results)
            Console.WriteLine($"  [{(r.Ok ? "OK" : "FEHLER")}] {r.Tweak.Description}");
    }

    private static string DescribeStatus(TweakStatus s) => s switch
    {
        TweakStatus.Applied    => "aktiv",
        TweakStatus.NotApplied => "nicht aktiv",
        _                      => "unbekannt"
    };
}
