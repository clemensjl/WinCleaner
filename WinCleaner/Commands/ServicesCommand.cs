using WinCleaner.SystemTools;
using WinCleaner.Util;

namespace WinCleaner.Commands;

/// <summary>
/// Verwaltet den Start-Typ von Windows-Diensten – auflisten, reversibel setzen,
/// rückgängig machen und ein kuratiertes, konservatives Deaktivierungs-Profil.
/// Sicherheit: Setzen/Profil laufen per DRY-RUN als Default und ändern nur mit
/// <c>--no-dry-run</c> (plus Bestätigung bzw. <c>--yes</c>) wirklich etwas.
/// Alle Änderungen sind über die <see cref="ServiceManager"/>/<see cref="TweakEngine"/>
/// umkehrbar; vor system-weiten Änderungen wird (als Admin) ein
/// Wiederherstellungspunkt erstellt.
/// </summary>
public sealed class ServicesCommand : ICommand
{
    public string Name => "services";
    public string Summary => "Windows-Dienste auflisten und Start-Typ reversibel ändern";
    public string Usage => "[--list] [--set <Name> manual|disabled|auto] [--undo <Name>] " +
                           "[--profile safe-disable] [--no-dry-run] [--yes]";
    public string[] AllowedFlags => new[]
        { "--list", "--set", "--undo", "--profile", "--no-dry-run", "--yes" };

    /// <summary>
    /// Kuratiertes Profil konservativ deaktivierbarer Dienste. Bewusst klein und
    /// risikoarm gehalten (Telemetrie/Demo/selten genutzt). Werte sind die
    /// internen Dienstnamen.
    /// </summary>
    private static readonly (string Name, string Hint)[] SafeDisableProfile =
    {
        ("DiagTrack",        "Verbindungsbenutzererfahrungen und Telemetrie"),
        ("dmwappushservice", "WAP-Push-Nachrichten-Routing (Telemetrie-bezogen)"),
        ("Fax",              "Faxdienst"),
        ("RetailDemo",       "Einzelhandelsdemo-Dienst")
    };

    public int Execute(CommandContext ctx)
    {
        // --list ist auch der Default, wenn keine andere Aktion angegeben ist.
        bool wantList    = ctx.HasFlag("--list");
        var  setName     = ctx.Option("--set");
        var  undoName    = ctx.Option("--undo");
        var  profileName = ctx.Option("--profile");

        bool anyAction = setName is not null || undoName is not null || profileName is not null;

        if (wantList || !anyAction)
            return ListServices(ctx);

        if (undoName is not null)
            return UndoService(ctx, undoName);

        if (setName is not null)
            return SetService(ctx, setName);

        // profileName ist hier garantiert nicht null.
        return ApplyProfile(ctx, profileName!);
    }

    // ---- --list ----

    private static int ListServices(CommandContext ctx)
    {
        var manager = new ServiceManager(ctx.Logger);
        var services = manager.List();

        if (ctx.Json)
        {
            JsonOut.Write(services.Select(s => new
            {
                name        = s.Name,
                displayName = s.DisplayName,
                startType   = ServiceManager.Describe(s.StartType)
            }));
            return 0;
        }

        if (services.Count == 0)
        {
            Console.WriteLine("Keine Dienste gefunden.");
            return 0;
        }

        var rows = services.Select(s => new[]
        {
            s.Name,
            Truncate(s.DisplayName, 50),
            ServiceManager.Describe(s.StartType)
        });
        ConsoleTable.From(rows, "Name", "Anzeigename", "Start-Typ").Write();
        Console.WriteLine($"\n{services.Count} Dienste.");
        return 0;
    }

    // ---- --undo <Name> ----

    private static int UndoService(CommandContext ctx, string name)
    {
        // Schreibzugriff auf HKLM -> Adminrechte nötig.
        if (!Elevation.IsAdministrator())
        {
            ctx.Logger.Info("Adminrechte nötig – starte mit Rechteerhöhung neu (UAC)...");
            return Elevation.RelaunchAsAdmin(ctx.FullArgs, ctx.Logger) ? 0 : 1;
        }

        var manager = new ServiceManager(ctx.Logger);
        bool ok = manager.UndoStartType(name);

        if (!ctx.Json)
            Console.WriteLine(ok
                ? $"Start-Typ von \"{name}\" auf den vorherigen Zustand zurückgesetzt."
                : $"Konnte \"{name}\" nicht rückgängig machen (keine Sicherung?).");
        else
            JsonOut.Write(new { action = "undo", service = name, success = ok });

        Prompt.PauseIfRelaunched(ctx.FullArgs);
        return ok ? 0 : 1;
    }

    // ---- --set <Name> <typ> ----

    private static int SetService(CommandContext ctx, string name)
    {
        // Der Ziel-Start-Typ steht als Positional; bei der Form "--set <Name> <typ>"
        // landet der Name selbst mit in den Positionals. Daher robust das erste
        // Positional nehmen, das NICHT dem Dienstnamen entspricht (case-insensitive).
        var requested = ctx.Positionals
            .FirstOrDefault(p => !string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
        if (requested is null)
        {
            Console.Error.WriteLine($"Ziel-Start-Typ fehlt. Beispiel: services --set {name} manual");
            return 1;
        }

        var type = ServiceManager.ParseRequested(requested);
        if (type is null)
        {
            Console.Error.WriteLine($"Unbekannter Start-Typ \"{requested}\". Erlaubt: manual | disabled | auto.");
            return 1;
        }

        var manager = new ServiceManager(ctx.Logger);
        var info = manager.ReadInfo(name);
        if (info is null)
        {
            Console.Error.WriteLine($"Dienst nicht gefunden: \"{name}\".");
            return 1;
        }

        bool dryRun = !ctx.HasFlag("--no-dry-run");

        // Dry-Run-Default: nur ankündigen, nichts ändern.
        ctx.Logger.Info($"Dienst \"{info.DisplayName}\" ({name}): {ServiceManager.Describe(info.StartType)} " +
                        $"-> {ServiceManager.Describe(type.Value)}.");

        if (dryRun)
        {
            ctx.Logger.Info("Vorschau (Dry-Run). Mit --no-dry-run wirklich anwenden (reversibel via --undo).");
            if (ctx.Json)
                JsonOut.Write(new { action = "set", service = name, target = ServiceManager.Describe(type.Value), dryRun = true, applied = false });
            return 0;
        }

        if (!ctx.HasFlag("--yes") && !Prompt.Confirm($"Start-Typ von \"{name}\" wirklich ändern?"))
        {
            Console.Error.WriteLine("Abgebrochen.");
            return 1;
        }

        // Schreibzugriff auf HKLM -> Adminrechte nötig.
        if (!Elevation.IsAdministrator())
        {
            ctx.Logger.Info("Adminrechte nötig – starte mit Rechteerhöhung neu (UAC)...");
            return Elevation.RelaunchAsAdmin(ctx.FullArgs, ctx.Logger) ? 0 : 1;
        }

        // Vor system-weiter Änderung einen Wiederherstellungspunkt anlegen (best effort).
        new RestorePoint(ctx.Logger).Create($"WinCleaner Dienst {name}");

        bool ok = manager.SetStartType(name, type.Value);
        if (!ctx.Json)
            Console.WriteLine(ok
                ? $"Start-Typ von \"{name}\" auf {ServiceManager.Describe(type.Value)} gesetzt (rückgängig: services --undo {name})."
                : $"Konnte Start-Typ von \"{name}\" nicht setzen.");
        else
            JsonOut.Write(new { action = "set", service = name, target = ServiceManager.Describe(type.Value), dryRun = false, applied = ok });

        Prompt.PauseIfRelaunched(ctx.FullArgs);
        return ok ? 0 : 1;
    }

    // ---- --profile safe-disable ----

    private static int ApplyProfile(CommandContext ctx, string profile)
    {
        if (!string.Equals(profile, "safe-disable", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Unbekanntes Profil \"{profile}\". Verfügbar: safe-disable.");
            return 1;
        }

        var manager = new ServiceManager(ctx.Logger);

        // Nur vorhandene Dienste betrachten, die noch nicht deaktiviert sind.
        var candidates = new List<(string Name, ServiceInfo Info, string Hint)>();
        foreach (var (name, hint) in SafeDisableProfile)
        {
            var info = manager.ReadInfo(name);
            if (info is null) continue;                                   // nicht vorhanden -> überspringen
            if (info.StartType is ServiceStartType.Disabled) continue;    // bereits deaktiviert
            candidates.Add((name, info, hint));
        }

        bool dryRun = !ctx.HasFlag("--no-dry-run");

        if (candidates.Count == 0)
        {
            if (ctx.Json)
                JsonOut.Write(new { action = "profile", profile = "safe-disable", dryRun, changed = Array.Empty<object>() });
            else
                Console.WriteLine("Profil \"safe-disable\": Nichts zu tun (alle Ziel-Dienste fehlen oder sind bereits deaktiviert).");
            return 0;
        }

        // Zusammenfassung dessen, was passieren würde/wird, nach stderr.
        ctx.Logger.Info("Profil \"safe-disable\" – folgende Dienste werden auf 'Deaktiviert' gesetzt:");
        foreach (var c in candidates)
            ctx.Logger.Info($"  {c.Name} ({ServiceManager.Describe(c.Info.StartType)}) – {c.Hint}");

        if (dryRun)
        {
            ctx.Logger.Info("Vorschau (Dry-Run). Mit --no-dry-run --yes wirklich anwenden (reversibel via --undo <Name>).");
            if (ctx.Json)
                JsonOut.Write(new
                {
                    action  = "profile",
                    profile = "safe-disable",
                    dryRun  = true,
                    applied = false,
                    candidates = candidates.Select(c => new { name = c.Name, from = ServiceManager.Describe(c.Info.StartType), to = "Deaktiviert" })
                });
            return 0;
        }

        if (!ctx.HasFlag("--yes") && !Prompt.Confirm($"{candidates.Count} Dienste wirklich deaktivieren?"))
        {
            Console.Error.WriteLine("Abgebrochen.");
            return 1;
        }

        // Schreibzugriff auf HKLM -> Adminrechte nötig.
        if (!Elevation.IsAdministrator())
        {
            ctx.Logger.Info("Adminrechte nötig – starte mit Rechteerhöhung neu (UAC)...");
            return Elevation.RelaunchAsAdmin(ctx.FullArgs, ctx.Logger) ? 0 : 1;
        }

        // Vor system-weiten Änderungen einen Wiederherstellungspunkt anlegen (best effort).
        new RestorePoint(ctx.Logger).Create("WinCleaner Profil safe-disable");

        var results = new List<(string Name, bool Ok)>();
        foreach (var c in candidates)
        {
            bool ok = manager.SetStartType(c.Name, ServiceStartType.Disabled);
            results.Add((c.Name, ok));
        }

        int done = results.Count(r => r.Ok);
        if (ctx.Json)
            JsonOut.Write(new
            {
                action  = "profile",
                profile = "safe-disable",
                dryRun  = false,
                applied = done,
                results = results.Select(r => new { name = r.Name, success = r.Ok })
            });
        else
            Console.WriteLine($"Profil \"safe-disable\" angewendet: {done}/{results.Count} Dienste deaktiviert " +
                              "(rückgängig: services --undo <Name>).");

        Prompt.PauseIfRelaunched(ctx.FullArgs);
        return done == results.Count ? 0 : 1;
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..(max - 1)] + "…";
}
