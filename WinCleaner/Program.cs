using WinCleaner.Commands;
using WinCleaner.Core;
using WinCleaner.SystemTools;
using WinCleaner.Util;

namespace WinCleaner;

/// <summary>
/// Einstiegspunkt. Hält nur das Befehls-Dispatch: der erste Parameter wählt
/// einen per Reflection gefundenen <see cref="ICommand"/> (siehe
/// <see cref="CommandRegistry"/>). Alles Fachliche liegt im jeweiligen Befehl.
/// </summary>
public class Program
{
    public static int Main(string[] args)
    {
        var logger = new Logger();

        if (args.Length == 0)
        {
            HelpCommand.Print();
            return 0;
        }

        string first = args[0].ToLowerInvariant();

        // Version als Top-Level-Flag (der Befehl "version" läuft über die Registry).
        if (first is "--version")
        {
            Console.WriteLine($"WinCleaner {AppInfo.Version}");
            return 0;
        }

        // Hilfe als Top-Level-Flag: "--help"/"-h" [Befehl].
        if (first is "--help" or "-h")
        {
            if (args.Length > 1 && CommandRegistry.Find(args[1]) is { } hc)
                HelpCommand.PrintOne(hc);
            else
                HelpCommand.Print();
            return 0;
        }

        return Dispatch(args, logger);
    }

    /// <summary>
    /// Führt einen einzelnen Befehl aus: sucht ihn in der <see cref="CommandRegistry"/>,
    /// behandelt <c>--help</c>, validiert die Flags und ruft <see cref="ICommand.Execute"/>.
    /// <paramref name="args"/> ist die vollständige Argumentliste inkl. Befehlsname
    /// (wie von der Kommandozeile). Wird sowohl vom normalen Start als auch vom
    /// interaktiven <c>menu</c>-Befehl genutzt, damit beide exakt dasselbe
    /// Dispatch-Verhalten (inkl. Flag-Prüfung und Fehlerbehandlung) teilen.
    /// </summary>
    public static int Dispatch(string[] args, Logger logger)
    {
        if (args.Length == 0) { HelpCommand.Print(); return 0; }

        var cmd = CommandRegistry.Find(args[0]);
        if (cmd is null)
        {
            logger.Error($"Unbekannter Befehl: {args[0]}");
            HelpCommand.Print();
            return 1;
        }

        // "<befehl> --help" -> Detailhilfe.
        if (args.Skip(1).Any(a => string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase)))
        {
            HelpCommand.PrintOne(cmd);
            return 0;
        }

        // Unbekannte Optionen früh ablehnen (z. B. Tippfehler --no-dryrun).
        if (!ValidateFlags(cmd, args, logger)) return 1;

        var ctx = new CommandContext
        {
            Args = args.Skip(1).ToArray(),
            FullArgs = args,
            Logger = logger,
            Json = args.Contains("--json")
        };

        try
        {
            return cmd.Execute(ctx);
        }
        catch (Exception ex)
        {
            logger.Error($"Unerwarteter Fehler: {ex.Message}");
            logger.Debug(ex.ToString());
            return 2;
        }
    }

    /// <summary>
    /// Lehnt unbekannte <c>--Optionen</c> ab. Global erlaubt sind --json, --help,
    /// -h und --relaunched; dazu die befehls-spezifischen <see cref="ICommand.AllowedFlags"/>.
    /// Optionswerte (--name=wert) werden am '=' getrennt geprüft.
    /// </summary>
    internal static bool ValidateFlags(ICommand cmd, string[] args, Logger logger)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--json", "--help", "-h", Elevation.RelaunchFlag
        };
        foreach (var f in cmd.AllowedFlags) allowed.Add(f);

        foreach (var a in args.Skip(1))
        {
            if (!a.StartsWith("--", StringComparison.Ordinal)) continue;
            var key = a.Split('=', 2)[0];
            if (!allowed.Contains(key))
            {
                logger.Error($"Unbekannte Option \"{a}\" für Befehl \"{cmd.Name}\".");
                HelpCommand.PrintOne(cmd);
                return false;
            }
        }
        return true;
    }
}
