using WinCleaner.Core;
using WinCleaner.Util;

namespace WinCleaner.Commands;

/// <summary>
/// Interaktives Textmenü (TUI) über die bestehenden Befehle: Nutzer wählen per
/// Zahl statt sich Befehle/Flags zu merken. Jede Aktion delegiert an
/// <see cref="Program.Dispatch"/> – es gibt also KEINE zweite Codepfad-Logik,
/// die Menüpunkte rufen exakt dieselben Befehle wie die Kommandozeile. Das
/// Menü ist bewusst nur eine Bequemlichkeitsschale; die CLI bleibt das primäre,
/// skriptbare Interface. Gefährliche Aktionen laufen erst als Probelauf und
/// werden dann einzeln bestätigt.
/// </summary>
public sealed class MenuCommand : ICommand
{
    public string Name => "menu";
    public string Summary => "Interaktives Menü (einfache Bedienung ohne Befehle zu tippen)";
    public string Usage => "";

    private sealed record Entry(string Key, string Label, Action<Logger> Run);

    public int Execute(CommandContext ctx)
    {
        // Ein Menü braucht eine echte Tastatur. Bei umgeleiteter Eingabe (Pipe,
        // geplanter Task) würde die Leseschleife sofort ins Leere laufen.
        if (Console.IsInputRedirected)
        {
            ctx.Logger.Error("Das Menü braucht eine interaktive Konsole. " +
                             "Ohne Menü direkt einen Befehl aufrufen, z. B. \"WinCleaner scan-junk\".");
            return 1;
        }

        var entries = BuildEntries();

        while (true)
        {
            PrintMenu(entries);
            Console.Write("\nAuswahl: ");
            var choice = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(choice)) continue;
            if (choice is "0" or "q" or "Q") { Console.WriteLine("Beendet."); return 0; }

            var entry = entries.FirstOrDefault(e =>
                string.Equals(e.Key, choice, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                Console.WriteLine($"Ungültige Auswahl: \"{choice}\".");
                Pause();
                continue;
            }

            Console.WriteLine($"\n=== {entry.Label} ===\n");
            try { entry.Run(ctx.Logger); }
            catch (Exception ex) { ctx.Logger.Error($"Fehler: {ex.Message}"); }
            Pause();
        }
    }

    // ---- Menüaufbau ----

    private static List<Entry> BuildEntries() => new()
    {
        new("1", "Junk scannen",              log => Run(log, "scan-junk")),
        new("2", "Junk bereinigen",           log => RunWithDryRun(log, "Junk-Dateien", "clean-junk")),
        new("3", "Speicher analysieren",      RunAnalyze),
        new("4", "Duplikate finden",          RunDuplicates),
        new("5", "Browser bereinigen",        log => RunWithDryRun(log, "Browser-Daten", "browser-clean")),
        new("6", "Zusatz-Reste finden (leere Ordner, 0-Byte, kaputte Verknüpfungen)", RunExtras),
        new("7", "Autostart anzeigen",        log => Run(log, "startup-list")),
        new("8", "Privacy-Status prüfen",     log => Run(log, "scan-privacy")),
        new("9", "Programme auflisten",       RunListPrograms),
        new("10", "Verfügbare Updates anzeigen", log => Run(log, "list-updates")),
        new("11", "Wiederherstellungspunkt erstellen", log => Run(log, "create-restore-point")),
        new("12", "Beliebigen Befehl eingeben", RunRawCommand),
        new("13", "Alle Befehle anzeigen (Hilfe)", log => Run(log, "help")),
    };

    private static void PrintMenu(List<Entry> entries)
    {
        Console.WriteLine();
        Console.WriteLine("┌───────────────────────────────────────────────┐");
        Console.WriteLine($"│  WinCleaner {AppInfo.Version,-34}│");
        Console.WriteLine("└───────────────────────────────────────────────┘");
        foreach (var e in entries)
            Console.WriteLine($" {e.Key,2}) {e.Label}");
        Console.WriteLine("  0) Beenden");
    }

    // ---- Aktionen ----

    /// <summary>Führt einen Befehl unverändert aus.</summary>
    private static void Run(Logger log, params string[] args) => Program.Dispatch(args, log);

    /// <summary>
    /// Zeigt zuerst den Probelauf (verändert nichts) und bietet danach an, die
    /// Aktion wirklich auszuführen (<c>--no-dry-run --yes</c>). So sieht der
    /// Nutzer erst, was passieren würde, und bestätigt dann bewusst.
    /// </summary>
    private static void RunWithDryRun(Logger log, string was, string command)
    {
        Console.WriteLine($"Probelauf – es wird noch nichts verändert:\n");
        Program.Dispatch(new[] { command }, log);

        Console.WriteLine();
        if (!Prompt.Confirm($"{was} jetzt WIRKLICH bereinigen (in den Papierkorb)?"))
        {
            Console.WriteLine("Nichts verändert.");
            return;
        }
        Program.Dispatch(new[] { command, "--no-dry-run", "--yes" }, log);
    }

    private static void RunAnalyze(Logger log)
    {
        var path = AskPath("Welcher Ordner/Laufwerk?", DefaultUserFolder());
        if (path is null) return;
        Run(log, "analyze-disk", path);
    }

    private static void RunExtras(Logger log)
    {
        var path = AskPath("Welcher Ordner?", DefaultUserFolder());
        if (path is null) return;
        Run(log, "scan-extras", path);
    }

    private static void RunDuplicates(Logger log)
    {
        var path = AskPath("In welchem Ordner nach Duplikaten suchen?", DefaultUserFolder());
        if (path is null) return;

        // Nur suchen und anzeigen – Löschen bewusst nicht aus dem Menü heraus,
        // dafür ist der explizite CLI-Aufruf mit Behalte-Strategie gedacht.
        Run(log, "find-duplicates", path);
        Console.WriteLine("\nZum Löschen mit Strategie/Schutzordnern: " +
                          $"WinCleaner find-duplicates \"{path}\" --delete --keep oldest --no-dry-run");
    }

    private static void RunListPrograms(Logger log)
    {
        Console.Write("Suchbegriff (leer = alle): ");
        var q = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(q)) Run(log, "list-programs");
        else Run(log, "list-programs", q);
    }

    private static void RunRawCommand(Logger log)
    {
        Console.WriteLine("Befehl inkl. Optionen eingeben (z. B. \"analyze-disk C:\\ --by-type\"):");
        Console.Write("WinCleaner ");
        var line = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(line)) { Console.WriteLine("Abgebrochen."); return; }

        var args = SplitArgs(line);
        if (args.Length == 0) return;
        Program.Dispatch(args, log);
    }

    // ---- Eingabe-Helfer ----

    /// <summary>Fragt einen Pfad ab (Enter = Vorgabe). Leere Eingabe ohne Vorgabe bricht ab.</summary>
    private static string? AskPath(string question, string? defaultPath)
    {
        Console.Write(defaultPath is null ? $"{question} " : $"{question} [{defaultPath}] ");
        var input = Console.ReadLine()?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(input)) input = defaultPath;
        if (string.IsNullOrWhiteSpace(input)) { Console.WriteLine("Kein Pfad angegeben."); return null; }

        if (!Directory.Exists(input) && !File.Exists(input))
        {
            Console.WriteLine($"Pfad nicht gefunden: {input}");
            return null;
        }
        return input;
    }

    private static string DefaultUserFolder() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// Zerlegt eine Eingabezeile in Argumente; Anführungszeichen fassen ein
    /// Argument mit Leerzeichen zusammen (z. B. Pfade wie "C:\Program Files").
    /// </summary>
    internal static string[] SplitArgs(string line)
    {
        var args = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool inQuotes = false;

        foreach (var c in line)
        {
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (cur.Length > 0) { args.Add(cur.ToString()); cur.Clear(); }
            }
            else cur.Append(c);
        }
        if (cur.Length > 0) args.Add(cur.ToString());
        return args.ToArray();
    }

    private static void Pause()
    {
        Console.WriteLine("\n— Enter drücken für das Menü —");
        Console.ReadLine();
    }
}
