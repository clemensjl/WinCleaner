using WinCleaner.Core;
using WinCleaner.SystemTools;
using WinCleaner.Util;

namespace WinCleaner.Commands;

/// <summary>
/// Listet alle installierten Programme aus den Uninstall-Schlüsseln der Registry
/// (HKLM, HKLM\WOW6432Node, HKCU). Reine Leseoperation. Optionaler
/// Positional-Suchbegriff filtert nach Teilzeichenkette im Anzeigenamen.
/// </summary>
public sealed class ListProgramsCommand : ICommand
{
    public string Name => "list-programs";
    public string Summary => "Installierte Programme auflisten (Registry-Uninstall-Keys)";
    public string Usage => "[Suchbegriff]";

    public int Execute(CommandContext ctx)
    {
        var inventory = new ProgramInventory(ctx.Logger);

        var filter = ctx.FirstPositional;
        var programs = string.IsNullOrWhiteSpace(filter)
            ? inventory.List()
            : inventory.Find(filter);

        if (ctx.Json)
        {
            JsonOut.Write(programs);
            return 0;
        }

        if (programs.Count == 0)
        {
            Console.WriteLine(string.IsNullOrWhiteSpace(filter)
                ? "Keine installierten Programme gefunden."
                : $"Kein Programm passend zu \"{filter}\" gefunden.");
            return 0;
        }

        ConsoleTable.From(
            programs.Select(p => new[]
            {
                p.DisplayName,
                p.DisplayVersion ?? "-",
                p.Publisher ?? "-",
                FormatSize(p.EstimatedSizeKb),
                p.Source
            }),
            "Name", "Version", "Herausgeber", "Größe", "Quelle"
        ).Write();

        Console.WriteLine($"\n{programs.Count} Programm(e).");
        return 0;
    }

    /// <summary>Formatiert die geschätzte Größe (in KB) menschenlesbar; "-" bei 0/unbekannt.</summary>
    private static string FormatSize(long estimatedSizeKb)
        => estimatedSizeKb > 0 ? DiskAnalyzer.FormatSize(estimatedSizeKb * 1024L) : "-";
}
