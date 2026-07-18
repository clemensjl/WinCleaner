using WinCleaner.Core;

namespace WinCleaner.Commands;

/// <summary>
/// Gemeinsame CLI-Helfer fuer Duplikat-artige Befehle (find-duplicates,
/// find-similar-images): --protect-Parsing und Anzeigename der
/// <see cref="KeepStrategy"/>. Zentral, damit beide Befehle identisch ticken.
/// </summary>
internal static class KeepProtectOptions
{
    /// <summary>
    /// Sammelt alle --protect-Werte. Unterstuetzt mehrfaches Vorkommen
    /// (<c>--protect a --protect b</c>) UND Kommatrennung je Vorkommen
    /// (<c>--protect a,b</c>), in beiden Schreibweisen <c>--protect=...</c>.
    /// </summary>
    internal static List<string> ParseProtected(CommandContext ctx)
    {
        var list = new List<string>();
        var args = ctx.Args;
        for (int i = 0; i < args.Length; i++)
        {
            string? value = null;
            if (args[i].StartsWith("--protect=", StringComparison.OrdinalIgnoreCase))
                value = args[i]["--protect=".Length..];
            else if (string.Equals(args[i], "--protect", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                value = args[++i];

            if (string.IsNullOrWhiteSpace(value)) continue;
            foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                list.Add(part);
        }
        return list;
    }

    internal static string KeepLabel(KeepStrategy keep) => keep switch
    {
        KeepStrategy.First        => "erste Datei",
        KeepStrategy.Oldest       => "älteste",
        KeepStrategy.Newest       => "neueste",
        KeepStrategy.ShortestPath => "kürzester Pfad",
        KeepStrategy.LongestPath  => "längster Pfad",
        _                         => "erste Datei"
    };
}
