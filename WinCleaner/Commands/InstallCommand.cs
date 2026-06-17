using WinCleaner.SystemTools;
using WinCleaner.Util;

namespace WinCleaner.Commands;

/// <summary>
/// Installiert ein Paket über <c>winget install</c>. Erwartet eine Paket-Id
/// oder einen Suchbegriff/Namen als Positional-Argument. Vor der Installation
/// wird bestätigt (außer <c>--yes</c>).
/// </summary>
public sealed class InstallCommand : ICommand
{
    public string Name => "install";
    public string Summary => "Paket per winget installieren (mit Rückfrage)";
    public string Usage => "<Id/Name> [--yes]";
    public string[] AllowedFlags => new[] { "--yes" };

    public int Execute(CommandContext ctx)
    {
        // Alle Positionals zusammenfügen, damit auch mehrteilige Namen funktionieren.
        var parts = ctx.Positionals.ToList();
        if (parts.Count == 0)
        {
            Console.Error.WriteLine($"{Name} {Usage}");
            return 1;
        }
        string query = string.Join(' ', parts);

        var winget = new WingetWrapper(ctx.Logger);
        if (!winget.IsAvailable())
        {
            winget.ReportUnavailable();
            return 1;
        }

        // Installation bestätigen lassen (außer --yes); Rückfrage nach stderr.
        Console.Error.WriteLine($"\nPaket \"{query}\" wird per winget installiert.");
        if (!ctx.HasFlag("--yes") && !Prompt.Confirm("Fortfahren?"))
        {
            Console.Error.WriteLine("Abgebrochen.");
            return 1;
        }

        bool ok = winget.Install(query);

        if (ctx.Json) JsonOut.Write(new { query, installed = ok });
        else Console.WriteLine(ok
            ? $"Paket \"{query}\" installiert."
            : $"Installation von \"{query}\" fehlgeschlagen.");

        return ok ? 0 : 2;
    }
}
