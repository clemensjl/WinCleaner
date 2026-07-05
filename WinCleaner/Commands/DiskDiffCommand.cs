using WinCleaner.Core;
using WinCleaner.Util;

namespace WinCleaner.Commands;

/// <summary>
/// Vergleicht zwei mit <c>analyze-disk --snapshot</c> gespeicherte
/// Disk-Snapshots und zeigt, welche Ordner/Dateien gewachsen, geschrumpft,
/// neu oder verschwunden sind. Rein lesend – verändert nichts.
/// </summary>
public sealed class DiskDiffCommand : ICommand
{
    public string Name => "disk-diff";
    public string Summary => "Zwei Disk-Snapshots vergleichen: was ist gewachsen/geschrumpft?";
    public string Usage => "<alt.json> <neu.json> [--top <n>]";
    public string[] AllowedFlags => new[] { "--top" };

    public int Execute(CommandContext ctx)
    {
        var positionals = ctx.Positionals.ToList();
        if (positionals.Count < 2)
        {
            Console.Error.WriteLine($"Zwei Snapshot-Dateien nötig: {Name} {Usage}");
            return 1;
        }

        int top = ctx.OptionInt("--top", 25);
        if (top < 1) top = 25;

        DiskSnapshot oldSnap, newSnap;
        try
        {
            oldSnap = DiskSnapshot.Load(positionals[0]);
            newSnap = DiskSnapshot.Load(positionals[1]);
        }
        catch (Exception ex)
        {
            ctx.Logger.Error($"Snapshot konnte nicht geladen werden: {ex.Message}");
            return 1;
        }

        if (!string.Equals(oldSnap.Root, newSnap.Root, StringComparison.OrdinalIgnoreCase))
            ctx.Logger.Info($"Hinweis: Snapshots stammen von verschiedenen Wurzeln " +
                            $"(\"{oldSnap.Root}\" vs. \"{newSnap.Root}\") – Vergleich kann irreführend sein.");

        var diff = DiskSnapshot.Diff(oldSnap, newSnap);

        if (ctx.Json)
        {
            JsonOut.Write(new
            {
                diff.OldRoot,
                diff.NewRoot,
                diff.OldCreatedUtc,
                diff.NewCreatedUtc,
                diff.OldTotalBytes,
                diff.NewTotalBytes,
                diff.DeltaTotalBytes,
                Entries = diff.Entries.Take(top)
            });
            return 0;
        }

        Console.WriteLine($"Snapshot alt: {oldSnap.Root}  ({oldSnap.CreatedUtc:yyyy-MM-dd HH:mm} UTC, " +
                          $"{DiskAnalyzer.FormatSize(diff.OldTotalBytes)})");
        Console.WriteLine($"Snapshot neu: {newSnap.Root}  ({newSnap.CreatedUtc:yyyy-MM-dd HH:mm} UTC, " +
                          $"{DiskAnalyzer.FormatSize(diff.NewTotalBytes)})");
        Console.WriteLine($"Gesamt-Differenz: {FormatDelta(diff.DeltaTotalBytes)}\n");

        if (diff.Entries.Count == 0)
        {
            Console.WriteLine("Keine Unterschiede zwischen den Snapshots.");
            return 0;
        }

        var rows = diff.Entries.Take(top).Select(e => new[]
        {
            e.OldBytes is null ? "neu" : e.NewBytes is null ? "entfernt" : "geändert",
            e.Path,
            e.OldBytes is { } o ? DiskAnalyzer.FormatSize(o) : "-",
            e.NewBytes is { } n ? DiskAnalyzer.FormatSize(n) : "-",
            FormatDelta(e.DeltaBytes)
        });
        ConsoleTable.From(rows, "Status", "Pfad", "Vorher", "Nachher", "Δ").Write();

        if (diff.Entries.Count > top)
            Console.WriteLine($"\n... und {diff.Entries.Count - top} weitere Einträge (--top erhöhen).");

        return 0;
    }

    /// <summary>Größe mit Vorzeichen, z. B. "+1,2 GB" / "-300,0 MB".</summary>
    internal static string FormatDelta(long delta) =>
        (delta >= 0 ? "+" : "-") + DiskAnalyzer.FormatSize(Math.Abs(delta));
}
