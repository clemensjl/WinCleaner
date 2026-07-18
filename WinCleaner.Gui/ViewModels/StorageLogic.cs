using System.Text;
using WinCleaner.Core;
using WinCleaner.Gui.Services;

namespace WinCleaner.Gui.ViewModels;

/// <summary>Ein Eintrag der Behalte-Strategie-Auswahl (ComboBox).</summary>
public sealed record KeepStrategyOption(KeepStrategy Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Reine, testbare Logik der Speicher-Seite: Argument- und Textbausteine sowie
/// Datenkonvertierungen ohne UI-Abhängigkeit (kein Dispatcher, keine Dialoge).
/// Das ViewModel bleibt dadurch dünne Orchestrierung.
/// </summary>
public static class StorageLogic
{
    /// <summary>Auswahlliste der Behalte-Strategien; erste Option ist der Standard.</summary>
    public static readonly IReadOnlyList<KeepStrategyOption> KeepStrategies = new[]
    {
        new KeepStrategyOption(KeepStrategy.First,        "Erste Datei"),
        new KeepStrategyOption(KeepStrategy.Newest,       "Neueste"),
        new KeepStrategyOption(KeepStrategy.Oldest,       "Älteste"),
        new KeepStrategyOption(KeepStrategy.ShortestPath, "Kürzester Pfad"),
        new KeepStrategyOption(KeepStrategy.LongestPath,  "Längster Pfad")
    };

    /// <summary>
    /// Baut die Argumente für den elevated CLI-Lauf des NTFS-Schnellscans.
    /// Das Ergebnis kommt über die Snapshot-Datei zurück, weil sich stdout eines
    /// per UAC gestarteten Prozesses nicht umleiten lässt.
    /// </summary>
    public static string BuildFastScanArguments(string path, int top, string snapshotFile) =>
        $"analyze-disk {ElevatedCli.Quote(path)} --fast --top {top} " +
        $"--snapshot {ElevatedCli.Quote(snapshotFile)}";

    /// <summary>
    /// Preflight für den elevated Schnellscan: erkennt an der analyze-disk-Hilfe
    /// der CLI (<c>--help analyze-disk</c>), ob sie <c>--fast</c> unterstützt.
    /// Ältere CLIs (&lt; 2.1.0) würden sonst erst nach dem UAC-Prompt mit
    /// "Unbekannte Option" scheitern. null/leer = nicht unterstützt.
    /// </summary>
    public static bool CliSupportsFastScan(string? helpOutput) =>
        !string.IsNullOrEmpty(helpOutput) &&
        helpOutput.Contains("--fast", StringComparison.Ordinal);

    /// <summary>
    /// Wandelt eine Snapshot-Datei (Rückkanal des elevated Schnellscans) in das
    /// Analyse-Ergebnis der Seite um: absteigend sortiert, auf Top-N begrenzt.
    /// </summary>
    public static DiskAnalysis ToAnalysis(DiskSnapshot snapshot, int top)
    {
        var analysis = new DiskAnalysis { TotalBytes = snapshot.TotalBytes };
        analysis.Entries.AddRange(snapshot.Entries
            .OrderByDescending(e => e.Bytes)
            .Take(top)
            .Select(e => new DiskEntry(e.IsDir, e.Path, e.Bytes, e.Files)));
        return analysis;
    }

    /// <summary>
    /// Übersetzt Bild-Ähnlichkeitsgruppen in Duplikatgruppen, damit der bewährte
    /// Lösch-Flow (<see cref="DuplicateFinder.ProcessDuplicates"/>) greift.
    /// </summary>
    public static List<DuplicateGroup> ToDuplicateGroups(IEnumerable<SimilarImageGroup> groups) =>
        groups.Select(g => new DuplicateGroup(g.Hash, g.Files, g.TotalBytes)).ToList();

    /// <summary>Bestätigungstext vor der Hardlink-Ersetzung (aus dem Probelauf).</summary>
    public static string BuildHardLinkPreview(DuplicateActionResult plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{plan.FilesAffected} Duplikate in {plan.GroupsProcessed} Gruppen werden durch " +
                      "Hardlinks auf die behaltene Datei ersetzt.");
        sb.AppendLine($"Ersparnis: {DiskAnalyzer.FormatSize(plan.BytesAffected)}. " +
                      "Die ersetzten Originale wandern in den Papierkorb.");
        if (plan.FilesSkipped > 0)
            sb.AppendLine($"{plan.FilesSkipped} Dateien werden übersprungen " +
                          "(z. B. anderes Volume, kein NTFS, bereits verlinkt oder Datei-Identität nicht lesbar).");
        sb.AppendLine();
        sb.Append("Jetzt ersetzen?");
        return sb.ToString();
    }

    /// <summary>Ergebnistext nach der Hardlink-Ersetzung.</summary>
    public static string BuildHardLinkResultText(DuplicateActionResult result) =>
        $"{result.FilesAffected} Duplikate durch Hardlinks ersetzt, " +
        $"{result.FilesSkipped} übersprungen, " +
        $"{DiskAnalyzer.FormatSize(result.BytesAffected)} gespart.";

    /// <summary>Bestätigungstext vor dem Verschieben ähnlicher Bilder in den Papierkorb.</summary>
    public static string BuildImageDeletePreview(DuplicateActionResult plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{plan.FilesAffected} ähnliche Bilder aus {plan.GroupsProcessed} Gruppen werden in den " +
                      $"Papierkorb verschoben ({DiskAnalyzer.FormatSize(plan.BytesAffected)}); " +
                      "je Gruppe bleibt ein Bild erhalten.");
        sb.AppendLine("Achtung: \"ähnlich\" heißt nicht byte-identisch – Fundliste vorher prüfen.");
        sb.AppendLine();
        sb.Append("Jetzt verschieben?");
        return sb.ToString();
    }

    /// <summary>Ergebnistext nach dem Verschieben ähnlicher Bilder.</summary>
    public static string BuildImageDeleteResultText(DuplicateActionResult result) =>
        $"{result.FilesAffected} ähnliche Bilder in den Papierkorb verschoben " +
        $"({DiskAnalyzer.FormatSize(result.BytesAffected)}), {result.FilesSkipped} übersprungen.";

    /// <summary>Vorschlagsname für den HTML-Report im Speichern-Dialog.</summary>
    public static string DefaultReportFileName(DateTime now) =>
        $"speicheranalyse-{now:yyyyMMdd-HHmm}.html";
}
