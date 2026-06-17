using WinCleaner.Core;

namespace WinCleaner.Commands;

/// <summary>
/// Laufzeitkontext eines Befehls: Argumente (ohne den Befehlsnamen), die
/// vollständigen Original-Argumente (für Elevation-Neustart), Logger und das
/// globale <c>--json</c>-Flag. Enthält kleine Parse-Helfer für Flags/Optionen.
/// </summary>
public sealed class CommandContext
{
    /// <summary>Argumente nach dem Befehlsnamen.</summary>
    public required string[] Args { get; init; }

    /// <summary>
    /// Vollständige Original-Argumente inklusive Befehlsname – wird für den
    /// elevierten Neustart (<see cref="WinCleaner.SystemTools.Elevation"/>) gebraucht.
    /// </summary>
    public required string[] FullArgs { get; init; }

    public required Logger Logger { get; init; }

    /// <summary>True, wenn <c>--json</c> gesetzt ist (maschinenlesbare Ausgabe).</summary>
    public bool Json { get; init; }

    /// <summary>Prüft, ob ein Flag (z. B. "--yes") vorhanden ist.</summary>
    public bool HasFlag(string flag) =>
        Args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    /// <summary>Erstes Nicht-Flag-Argument (Positional), oder null.</summary>
    public string? FirstPositional =>
        Args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));

    /// <summary>Alle Nicht-Flag-Argumente in Reihenfolge.</summary>
    public IEnumerable<string> Positionals =>
        Args.Where(a => !a.StartsWith("--", StringComparison.Ordinal));

    /// <summary>
    /// Wert einer Option im Format <c>--name wert</c> oder <c>--name=wert</c>.
    /// Null, wenn die Option fehlt.
    /// </summary>
    public string? Option(string name)
    {
        for (int i = 0; i < Args.Length; i++)
        {
            var a = Args[i];
            if (a.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                return a[(name.Length + 1)..];
            if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase) && i + 1 < Args.Length)
                return Args[i + 1];
        }
        return null;
    }

    /// <summary>Wie <see cref="Option"/>, aber als long geparst; <paramref name="fallback"/> bei Fehlen/Fehler.</summary>
    public long OptionLong(string name, long fallback)
        => long.TryParse(Option(name), out var v) ? v : fallback;

    /// <summary>Wie <see cref="Option"/>, aber als int geparst; <paramref name="fallback"/> bei Fehlen/Fehler.</summary>
    public int OptionInt(string name, int fallback)
        => int.TryParse(Option(name), out var v) ? v : fallback;
}
