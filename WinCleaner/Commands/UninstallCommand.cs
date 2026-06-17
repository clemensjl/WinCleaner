using System.Diagnostics;
using System.Text.RegularExpressions;
using WinCleaner.Core;
using WinCleaner.SystemTools;
using WinCleaner.Util;

namespace WinCleaner.Commands;

/// <summary>
/// Deinstalliert ein installiertes Programm über seinen regulären
/// UninstallString aus der Registry (bevorzugt QuietUninstallString). Mit
/// <c>--silent</c> wird der Installer-Typ (MSI/NSIS/Inno) erkannt und um die
/// passenden Silent-Flags ergänzt. Folgt dem WinCleaner-Sicherheitsmodell:
/// DRY-RUN ist Standard, echte Deinstallation nur mit <c>--no-dry-run</c>,
/// davor Bestätigung (außer <c>--yes</c>) und – falls Admin – ein
/// Wiederherstellungspunkt. Anschließend werden mögliche Reste angezeigt und
/// auf Wunsch (Bestätigung) in den Papierkorb verschoben. Es findet KEIN
/// Forced-/Brute-Force-Uninstall statt – nur die hinterlegten UninstallStrings.
/// </summary>
public sealed class UninstallCommand : ICommand
{
    public string Name => "uninstall";
    public string Summary => "Programm über seinen UninstallString deinstallieren (Standard: Probelauf)";
    public string Usage => "<Name> [--silent] [--no-dry-run] [--yes]";
    public string[] AllowedFlags => new[] { "--silent", "--no-dry-run", "--yes" };

    public int Execute(CommandContext ctx)
    {
        var query = string.Join(' ', ctx.Positionals).Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            Console.Error.WriteLine($"Name fehlt: {Name} {Usage}");
            return 1;
        }

        var inventory = new ProgramInventory(ctx.Logger);
        var matches = inventory.Find(query);

        if (matches.Count == 0)
        {
            ctx.Logger.Error($"Kein Programm passend zu \"{query}\" gefunden.");
            return 1;
        }

        // Bei mehreren Treffern: bevorzugt exakte Namensgleichheit, sonst
        // mehrdeutig -> auflisten und abbrechen, damit nichts Falsches entfernt wird.
        InstalledProgram? program = SelectMatch(matches, query, ctx);
        if (program is null) return 1;

        bool silent  = ctx.HasFlag("--silent");
        bool dryRun  = !ctx.HasFlag("--no-dry-run");
        bool assumeYes = ctx.HasFlag("--yes");

        // Deinstallationsbefehl bestimmen (Datei + Argumente).
        var (fileName, arguments, kind) = BuildUninstallCommand(program, silent);
        if (fileName is null)
        {
            ctx.Logger.Error($"Kein verwertbarer Deinstallationsbefehl für \"{program.DisplayName}\".");
            return 1;
        }

        // Zusammenfassung der geplanten Aktion -> stderr (Diagnose).
        ctx.Logger.Info($"Programm: {program.DisplayName} {program.DisplayVersion ?? ""}".TrimEnd());
        ctx.Logger.Info($"Installer-Typ: {kind}");
        ctx.Logger.Info($"Befehl: {fileName} {arguments}".TrimEnd());

        if (dryRun)
        {
            // Probelauf: nichts ausführen, nur zeigen, was passieren würde.
            if (ctx.Json)
            {
                JsonOut.Write(new
                {
                    dryRun = true,
                    program = program.DisplayName,
                    version = program.DisplayVersion,
                    installerKind = kind.ToString(),
                    command = $"{fileName} {arguments}".TrimEnd(),
                    leftovers = ScanLeftovers(program)
                });
            }
            else
            {
                Console.WriteLine("[Probelauf] Es würde deinstalliert werden:");
                Console.WriteLine($"  {program.DisplayName} {program.DisplayVersion ?? ""}".TrimEnd());
                Console.WriteLine($"  Befehl: {fileName} {arguments}".TrimEnd());
                Console.WriteLine("\nKeine Änderung vorgenommen. Echte Deinstallation mit --no-dry-run.");
            }
            return 0;
        }

        // ---- Echte Deinstallation ----

        ctx.Logger.Info("ECHTE DEINSTALLATION (--no-dry-run aktiv).");
        if (!assumeYes && !Prompt.Confirm($"\"{program.DisplayName}\" wirklich deinstallieren?"))
        {
            Console.Error.WriteLine("Abgebrochen.");
            return 1;
        }

        // Vor der system-weiten Änderung – wenn möglich (Admin) – einen
        // Wiederherstellungspunkt anlegen (M1). Ohne Admin wird das übersprungen.
        if (Elevation.IsAdministrator())
        {
            new RestorePoint(ctx.Logger).Create($"WinCleaner Deinstallation: {program.DisplayName}");
        }
        else
        {
            ctx.Logger.Info("Kein Admin – kein Wiederherstellungspunkt. Der Installer fordert ggf. selbst Adminrechte (UAC) an.");
        }

        int exitCode = RunUninstaller(fileName, arguments, ctx.Logger);
        if (exitCode != 0)
        {
            ctx.Logger.Error($"Deinstallationsprogramm endete mit Code {exitCode}. Möglicherweise abgebrochen oder fehlgeschlagen.");
            // Trotzdem Reste anzeigen – falls teilweise entfernt.
        }
        else
        {
            ctx.Logger.Info("Deinstallation abgeschlossen.");
        }

        // Reste-Scan: typische Überbleibsel anzeigen.
        var leftovers = ScanLeftovers(program);

        if (ctx.Json)
        {
            JsonOut.Write(new
            {
                dryRun = false,
                program = program.DisplayName,
                exitCode,
                leftovers
            });
            return exitCode == 0 ? 0 : 2;
        }

        ReportAndOfferLeftoverCleanup(leftovers, ctx);
        return exitCode == 0 ? 0 : 2;
    }

    // ---- Auswahl bei mehreren Treffern ----

    private static InstalledProgram? SelectMatch(
        List<InstalledProgram> matches, string query, CommandContext ctx)
    {
        if (matches.Count == 1) return matches[0];

        // Exakte (case-insensitive) Namensübereinstimmung bevorzugen.
        var exact = matches
            .Where(p => string.Equals(p.DisplayName, query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (exact.Count == 1) return exact[0];

        // Mehrdeutig -> Treffer nach stderr auflisten und abbrechen.
        ctx.Logger.Error($"Mehrdeutig: {matches.Count} Programme passen zu \"{query}\". Bitte genauer angeben:");
        foreach (var p in matches)
            Console.Error.WriteLine($"  - {p.DisplayName} {p.DisplayVersion ?? ""}".TrimEnd());
        return null;
    }

    // ---- Befehlsaufbau ----

    internal enum InstallerKind { Msi, Nsis, InnoSetup, Generic }

    /// <summary>
    /// Baut Dateiname + Argumente für die Deinstallation. Ohne <c>--silent</c>
    /// wird – sofern vorhanden – der QuietUninstallString verwendet, sonst der
    /// reguläre UninstallString. Mit <c>--silent</c> wird der Installer-Typ
    /// erkannt und um die passenden Silent-Flags ergänzt.
    /// </summary>
    internal static (string? fileName, string arguments, InstallerKind kind) BuildUninstallCommand(
        InstalledProgram program, bool silent)
    {
        // MSI eindeutig anhand der Produkt-GUID im Schlüsselnamen erkennbar.
        string? msiGuid = TryGetMsiGuid(program);

        if (!silent)
        {
            // Ohne --silent: bevorzugt den vom Programm hinterlegten Quiet-String,
            // sonst den regulären String, jeweils unverändert übernehmen.
            string raw = program.QuietUninstallString ?? program.UninstallString ?? "";
            var (file, args) = SplitCommandLine(raw);
            var k = msiGuid is not null ? InstallerKind.Msi : DetectKind(program, raw);
            return (file, args, k);
        }

        // Mit --silent: für MSI selbst einen stillen msiexec-Aufruf bauen.
        if (msiGuid is not null)
            return ("msiexec.exe", $"/x{msiGuid} /qn /norestart", InstallerKind.Msi);

        // QuietUninstallString ist bereits "still" – falls vorhanden, nehmen.
        if (!string.IsNullOrWhiteSpace(program.QuietUninstallString))
        {
            var (file, args) = SplitCommandLine(program.QuietUninstallString!);
            return (file, args, DetectKind(program, program.QuietUninstallString!));
        }

        string rawUninstall = program.UninstallString ?? "";
        if (string.IsNullOrWhiteSpace(rawUninstall))
            return (null, "", InstallerKind.Generic);

        // msiexec-basierter UninstallString -> /qn ergänzen.
        if (rawUninstall.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
        {
            var (file, args) = SplitCommandLine(rawUninstall);
            // /i durch /x ersetzen (gelegentlich falsch hinterlegt) und still schalten.
            args = Regex.Replace(args, @"(?<![\w/])/i\b", "/x", RegexOptions.IgnoreCase);
            if (!ContainsQuietFlag(args)) args = (args + " /qn /norestart").Trim();
            return (file, args, InstallerKind.Msi);
        }

        var kind = DetectKind(program, rawUninstall);
        var (exe, baseArgs) = SplitCommandLine(rawUninstall);

        string silentFlags = kind switch
        {
            InstallerKind.Nsis      => "/S",                          // NSIS
            InstallerKind.InnoSetup => "/VERYSILENT /SUPPRESSMSGBOXES", // Inno Setup
            _                        => ""
        };

        string finalArgs = string.IsNullOrEmpty(silentFlags)
            ? baseArgs
            : (baseArgs + " " + silentFlags).Trim();

        return (exe, finalArgs, kind);
    }

    /// <summary>Erkennt den Installer-Typ heuristisch aus Schlüsselname, Dateiname und Argumenten.</summary>
    private static InstallerKind DetectKind(InstalledProgram program, string rawCommand)
    {
        if (TryGetMsiGuid(program) is not null ||
            rawCommand.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
            return InstallerKind.Msi;

        string lower = rawCommand.ToLowerInvariant();

        // Inno Setup: typischer Deinstaller heißt unins000.exe / unins001.exe.
        if (lower.Contains("unins0"))
            return InstallerKind.InnoSetup;

        // NSIS: Deinstaller heißt sehr häufig uninstall.exe / uninst.exe / uninstaller.exe.
        if (lower.Contains("uninstall.exe") || lower.Contains("uninst.exe") ||
            lower.Contains("uninstaller.exe") || lower.Contains("au_.exe"))
            return InstallerKind.Nsis;

        return InstallerKind.Generic;
    }

    /// <summary>
    /// Liefert die MSI-Produkt-GUID in der Form {XXXX...} , wenn der
    /// Registry-Schlüsselname eine GUID ist (typisch für MSI-Pakete).
    /// </summary>
    private static string? TryGetMsiGuid(InstalledProgram program)
    {
        var name = program.RegistryKeyName;
        if (Regex.IsMatch(name,
                @"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$"))
            return name;
        return null;
    }

    private static bool ContainsQuietFlag(string args)
        => Regex.IsMatch(args, @"/q(n|b)?\b", RegexOptions.IgnoreCase);

    /// <summary>
    /// Zerlegt eine Kommandozeile in (Programm, Argumente). Berücksichtigt einen
    /// in Anführungszeichen stehenden Pfad mit Leerzeichen.
    /// </summary>
    internal static (string fileName, string arguments) SplitCommandLine(string commandLine)
    {
        string s = commandLine.Trim();
        if (s.Length == 0) return ("", "");

        if (s[0] == '"')
        {
            int end = s.IndexOf('"', 1);
            if (end > 0)
            {
                string file = s.Substring(1, end - 1);
                string args = s[(end + 1)..].Trim();
                return (file, args);
            }
            // Kein schließendes Anführungszeichen -> alles als Dateiname.
            return (s.Trim('"'), "");
        }

        // Unquoted: bis zum ersten Leerzeichen ist der Programmpfad.
        int sp = s.IndexOf(' ');
        if (sp < 0) return (s, "");
        return (s[..sp], s[(sp + 1)..].Trim());
    }

    // ---- Prozessausführung ----

    /// <summary>
    /// Führt das Deinstallationsprogramm aus und wartet auf das Ende. Nutzt
    /// ShellExecute, damit der Installer bei Bedarf selbst die UAC-Erhöhung
    /// anfordern kann. Liefert den Exitcode (-1 bei Startfehler).
    /// </summary>
    private static int RunUninstaller(string fileName, string arguments, Logger logger)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName        = fileName,
                Arguments       = arguments,
                UseShellExecute = true   // ermöglicht UAC-Prompt des Installers
            };

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                logger.Error("Deinstallationsprogramm konnte nicht gestartet werden.");
                return -1;
            }

            proc.WaitForExit();
            return proc.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // Häufig: UAC abgelehnt (Code 1223) oder Datei nicht gefunden.
            logger.Error($"Start des Deinstallationsprogramms fehlgeschlagen: {ex.Message}");
            return -1;
        }
        catch (Exception ex)
        {
            logger.Error($"Unerwarteter Fehler bei der Deinstallation: {ex.Message}");
            return -1;
        }
    }

    // ---- Reste-Scan ----

    /// <summary>
    /// Sucht nach typischen Überbleibseln: ein noch vorhandener
    /// Installationsordner sowie gleichnamige Ordner in den AppData-Bereichen
    /// (Roaming, Local, LocalLow, ProgramData) anhand von Anzeigename und
    /// Herausgeber. Reine Suche – löscht nichts.
    /// </summary>
    internal static List<string> ScanLeftovers(InstalledProgram program)
    {
        var found = new List<string>();

        // 1) Übrig gebliebener Installationsordner.
        if (!string.IsNullOrWhiteSpace(program.InstallLocation))
        {
            try
            {
                var loc = program.InstallLocation!.Trim().Trim('"');
                if (Directory.Exists(loc)) found.Add(loc);
            }
            catch { /* ungültiger Pfad -> ignorieren */ }
        }

        // 2) Typische AppData-Ordner anhand von Name/Herausgeber.
        var candidates = NameCandidates(program);
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),       // Roaming
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),  // Local
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                         "AppData", "LocalLow"),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)  // ProgramData
        };

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

            foreach (var cand in candidates)
            {
                try
                {
                    var candidatePath = Path.Combine(root, cand);
                    if (Directory.Exists(candidatePath) &&
                        !found.Contains(candidatePath, StringComparer.OrdinalIgnoreCase))
                        found.Add(candidatePath);
                }
                catch { /* ungültiger Name -> ignorieren */ }
            }
        }

        return found;
    }

    /// <summary>
    /// Bekannte Sammel-Ordnernamen, die von mehreren Programmen eines Herstellers
    /// gemeinsam genutzt werden. Solche Ordner dürfen NIE als Lösch-Kandidat gelten,
    /// da sie Daten noch installierter Programme enthalten können.
    /// </summary>
    private static readonly HashSet<string> SharedFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Google", "Mozilla", "JetBrains", "Adobe", "Common Files", "Windows"
    };

    /// <summary>
    /// Mögliche Ordnernamen ausschließlich aus dem Anzeigenamen (bereinigt). Der
    /// Herausgeber wird NICHT als Lösch-Kandidat verwendet (nur Anzeige), da
    /// Hersteller-Sammelordner Daten weiterer Programme enthalten können.
    /// </summary>
    private static List<string> NameCandidates(InstalledProgram program)
    {
        var list = new List<string>();

        void AddIfSafe(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            // Versionssuffixe / Klammerzusätze grob entfernen.
            string cleaned = Regex.Replace(raw, @"\s*[\(\[].*$", "").Trim();
            cleaned = Regex.Replace(cleaned, @"\s+\d+(\.\d+)+.*$", "").Trim();
            if (cleaned.Length < 3) return; // zu kurz -> zu viele Fehltreffer
            if (cleaned.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return;
            if (SharedFolderNames.Contains(cleaned)) return; // Sammelordner ausschließen
            if (!list.Contains(cleaned, StringComparer.OrdinalIgnoreCase))
                list.Add(cleaned);
        }

        // Nur vom Anzeigenamen abgeleitete Kandidaten – kein Publisher.
        AddIfSafe(program.DisplayName);
        return list;
    }

    /// <summary>
    /// Zeigt gefundene Reste an und bietet das Verschieben in den Papierkorb an.
    /// Die Reste-Löschung erfordert IMMER eine eigene, explizite Bestätigung –
    /// auch wenn <c>--yes</c> für die Deinstallation gesetzt war (Datenschutz:
    /// Reste-Ordner können fälschlich erkannt werden). Löscht NIE ohne Zustimmung.
    /// </summary>
    private static void ReportAndOfferLeftoverCleanup(
        List<string> leftovers, CommandContext ctx)
    {
        if (leftovers.Count == 0)
        {
            Console.WriteLine("Keine offensichtlichen Reste gefunden.");
            return;
        }

        Console.WriteLine($"\nMögliche Reste ({leftovers.Count}):");
        foreach (var p in leftovers) Console.WriteLine("  " + p);

        Console.Error.WriteLine("\nHinweis: Bitte prüfen, ob diese Ordner wirklich zum entfernten " +
                                "Programm gehören – sie werden nur auf Bestätigung in den Papierkorb verschoben.");

        // Eigene, explizite Rückfrage – bewusst ohne --yes-Abkürzung.
        bool proceed = Prompt.Confirm("Diese Reste in den Papierkorb verschieben?");
        if (!proceed)
        {
            Console.Error.WriteLine("Reste belassen.");
            return;
        }

        int moved = 0;
        foreach (var path in leftovers)
        {
            try
            {
                RecycleBinHelper.DeleteDirectory(path); // Papierkorb -> umkehrbar
                moved++;
            }
            catch (Exception ex)
            {
                ctx.Logger.Error($"Konnte \"{path}\" nicht entfernen: {ex.Message}");
            }
        }
        Console.WriteLine($"{moved} von {leftovers.Count} Rest-Ordner(n) in den Papierkorb verschoben.");
    }
}
