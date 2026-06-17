namespace WinCleaner.Commands;

/// <summary>
/// Listet alle per Reflection gefundenen Befehle auf (auto-generierte Hilfe).
/// Unterstützt auch Hilfe zu einem einzelnen Befehl: <c>help &lt;befehl&gt;</c>.
/// </summary>
public sealed class HelpCommand : ICommand
{
    public string Name => "help";
    public string Summary => "Diese Hilfe oder Hilfe zu einem Befehl";
    public string Usage => "[Befehl]";

    public int Execute(CommandContext ctx)
    {
        var sub = ctx.FirstPositional;
        if (sub is not null && CommandRegistry.Find(sub) is { } c)
            PrintOne(c);
        else
            Print();
        return 0;
    }

    /// <summary>Vollständige Befehlsübersicht.</summary>
    public static void Print()
    {
        Console.WriteLine("WinCleaner (CLI)\n");
        Console.WriteLine("Befehle:");

        var rows = CommandRegistry.All
            .Select(c => (left: (c.Name + " " + c.Usage).TrimEnd(), c.Summary))
            .ToList();
        int width = rows.Count == 0 ? 0 : rows.Max(x => x.left.Length);

        foreach (var (left, summary) in rows)
            Console.WriteLine($"  {left.PadRight(width + 2)}{summary}");

        Console.WriteLine("\nGlobale Optionen:");
        Console.WriteLine("  --json     Maschinenlesbare Ausgabe (sofern vom Befehl unterstützt)");
        Console.WriteLine("  --help     Hilfe zu einem Befehl (z. B. clean-junk --help)");
        Console.WriteLine("  --version  Versionsnummer anzeigen");
    }

    /// <summary>Detailhilfe zu einem einzelnen Befehl.</summary>
    public static void PrintOne(ICommand c)
    {
        Console.WriteLine($"{c.Name} {c.Usage}".TrimEnd());
        Console.WriteLine($"  {c.Summary}");
        if (c.AllowedFlags.Length > 0)
            Console.WriteLine($"  Optionen: {string.Join(", ", c.AllowedFlags)}");
    }
}
