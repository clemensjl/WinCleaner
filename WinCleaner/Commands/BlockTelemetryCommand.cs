using WinCleaner.SystemTools;
using WinCleaner.Util;

namespace WinCleaner.Commands;

/// <summary>
/// Blockiert bekannte Microsoft-Telemetrie-Hosts über einen markierten Abschnitt in
/// der Windows-hosts-Datei (Null-Route auf <c>0.0.0.0</c>). Vollständig umkehrbar:
/// <c>--undo</c> entfernt ausschließlich den von WinCleaner verwalteten Abschnitt.
///
/// Sicherheit: Standard ist <c>--status</c> (read-only). <c>--apply</c> zeigt
/// standardmäßig nur einen Dry-Run der zu blockenden Hosts; echtes Schreiben verlangt
/// <c>--no-dry-run</c> (plus Bestätigung bzw. <c>--yes</c>). Vor jeder Änderung wird
/// die hosts-Datei gesichert. Schreibende Aktionen erfordern Adminrechte.
/// </summary>
public sealed class BlockTelemetryCommand : ICommand
{
    public string Name => "block-telemetry";
    public string Summary => "Microsoft-Telemetrie-Hosts via hosts-Datei blocken (umkehrbar, Admin)";
    public string Usage => "[--status] [--apply [--no-dry-run] [--yes]] [--undo [--yes]]";
    public string[] AllowedFlags => new[] { "--apply", "--undo", "--status", "--no-dry-run", "--yes" };

    public int Execute(CommandContext ctx)
    {
        bool apply = ctx.HasFlag("--apply");
        bool undo = ctx.HasFlag("--undo");

        if (apply && undo)
        {
            ctx.Logger.Error("--apply und --undo schließen sich aus.");
            return 1;
        }

        // Standard (auch bei explizitem --status): read-only Statusanzeige.
        if (!apply && !undo)
            return ShowStatus(ctx);

        return apply ? RunApply(ctx) : RunUndo(ctx);
    }

    /// <summary>Read-only: zeigt, ob der Block aktiv ist und wie viele Hosts enthalten sind.</summary>
    private static int ShowStatus(CommandContext ctx)
    {
        var blocker = new HostsBlocker(ctx.Logger);
        var status = blocker.GetStatus();

        if (ctx.Json)
        {
            JsonOut.Write(new
            {
                aktiv = status.Active,
                geblockteHosts = status.HostCount,
                kuratierteHosts = status.CuratedCount,
                hostsDatei = status.HostsPath
            });
            return 0;
        }

        Console.WriteLine(status.Active
            ? $"Telemetrie-Block AKTIV – {status.HostCount} Hosts geblockt."
            : "Telemetrie-Block NICHT aktiv.");
        Console.WriteLine($"Kuratierte Hosts: {status.CuratedCount}");
        Console.WriteLine($"hosts-Datei: {status.HostsPath}");
        return 0;
    }

    /// <summary>Schreibt/aktualisiert den Block – Default Dry-Run, echtes Schreiben mit --no-dry-run.</summary>
    private int RunApply(CommandContext ctx)
    {
        bool dryRun = !ctx.HasFlag("--no-dry-run");

        // Dry-Run: nur Anzeige der Hosts, keine Änderung, keine Adminrechte nötig.
        if (dryRun)
        {
            ctx.Logger.Info($"Dry-Run: {HostsBlocker.TelemetryHosts.Count} Telemetrie-Hosts würden in die " +
                            "hosts-Datei eingetragen (0.0.0.0). Nutze --no-dry-run --yes zum Schreiben.");
            PrintHostList(ctx);
            return 0;
        }

        // Ab hier echte Änderung -> Adminrechte erforderlich.
        if (!Elevation.IsAdministrator())
        {
            ctx.Logger.Info("Adminrechte nötig – starte mit Rechteerhöhung neu (UAC)...");
            return Elevation.RelaunchAsAdmin(ctx.FullArgs, ctx.Logger) ? 0 : 1;
        }

        // Bestätigung (außer --yes); Zusammenfassung nach stderr.
        if (!ctx.HasFlag("--yes"))
        {
            Console.Error.WriteLine($"\n{HostsBlocker.TelemetryHosts.Count} Microsoft-Telemetrie-Hosts werden in der " +
                                    "hosts-Datei auf 0.0.0.0 umgeleitet (umkehrbar via --undo). " +
                                    "Eine Sicherung wird zuvor angelegt.");
            if (!Prompt.Confirm("Fortfahren?"))
            {
                Console.Error.WriteLine("Abgebrochen.");
                Prompt.PauseIfRelaunched(ctx.FullArgs);
                return 1;
            }
        }

        // Vor systemweiter Änderung Wiederherstellungspunkt versuchen (best effort).
        new RestorePoint(ctx.Logger).Create("WinCleaner Telemetrie-Block");

        bool ok = new HostsBlocker(ctx.Logger).Apply();
        if (ctx.Json)
            // Maschinenlesbares Ergebnis bei echtem apply mit --json.
            JsonOut.Write(new { aktion = "apply", erfolg = ok, hosts = HostsBlocker.TelemetryHosts.Count });
        else
            Console.WriteLine(ok
                ? "Telemetrie-Block aktiviert."
                : "Telemetrie-Block konnte nicht geschrieben werden.");

        Prompt.PauseIfRelaunched(ctx.FullArgs);
        return ok ? 0 : 2;
    }

    /// <summary>Entfernt den markierten Block – read-only Datei bleibt sonst unberührt.</summary>
    private int RunUndo(CommandContext ctx)
    {
        if (!Elevation.IsAdministrator())
        {
            ctx.Logger.Info("Adminrechte nötig – starte mit Rechteerhöhung neu (UAC)...");
            return Elevation.RelaunchAsAdmin(ctx.FullArgs, ctx.Logger) ? 0 : 1;
        }

        if (!ctx.HasFlag("--yes"))
        {
            Console.Error.WriteLine("\nDer WinCleaner-Telemetrie-Block wird aus der hosts-Datei entfernt " +
                                    "(alle übrigen Einträge bleiben erhalten).");
            if (!Prompt.Confirm("Fortfahren?"))
            {
                Console.Error.WriteLine("Abgebrochen.");
                Prompt.PauseIfRelaunched(ctx.FullArgs);
                return 1;
            }
        }

        bool ok = new HostsBlocker(ctx.Logger).Undo();
        if (ctx.Json)
            // Maschinenlesbares Ergebnis bei echtem undo mit --json.
            JsonOut.Write(new { aktion = "undo", erfolg = ok, hosts = HostsBlocker.TelemetryHosts.Count });
        else
            Console.WriteLine(ok
                ? "Telemetrie-Block entfernt."
                : "Telemetrie-Block konnte nicht entfernt werden.");

        Prompt.PauseIfRelaunched(ctx.FullArgs);
        return ok ? 0 : 2;
    }

    /// <summary>Gibt die kuratierte Host-Liste aus (stdout, bzw. JSON bei --json).</summary>
    private static void PrintHostList(CommandContext ctx)
    {
        if (ctx.Json)
        {
            JsonOut.Write(new
            {
                dryRun = true,
                kuratierteHosts = HostsBlocker.TelemetryHosts.Count,
                hosts = HostsBlocker.TelemetryHosts
            });
            return;
        }

        var rows = HostsBlocker.TelemetryHosts.Select(h => new[] { "0.0.0.0", h });
        ConsoleTable.From(rows, "Ziel", "Host").Write();
    }
}
