using System.Text.Json;
using WinCleaner.Util;

namespace WinCleaner.Core;

/// <summary>Ein gespeicherter Eintrag eines Disk-Snapshots (Ordner oder Datei).</summary>
public sealed record SnapshotEntry(string Path, long Bytes, int Files, bool IsDir);

/// <summary>
/// Eine Zeile des Snapshot-Vergleichs. <see cref="OldBytes"/>/<see cref="NewBytes"/>
/// sind null, wenn der Pfad im jeweiligen Snapshot nicht vorkommt (neu bzw. entfernt).
/// </summary>
public sealed record SnapshotDiffEntry(string Path, long? OldBytes, long? NewBytes)
{
    public long DeltaBytes => (NewBytes ?? 0) - (OldBytes ?? 0);
}

/// <summary>Ergebnis eines Snapshot-Vergleichs (nach |Δ| absteigend sortiert).</summary>
public sealed class SnapshotDiff
{
    public required string OldRoot { get; init; }
    public required string NewRoot { get; init; }
    public required DateTime OldCreatedUtc { get; init; }
    public required DateTime NewCreatedUtc { get; init; }
    public long OldTotalBytes { get; init; }
    public long NewTotalBytes { get; init; }
    public long DeltaTotalBytes => NewTotalBytes - OldTotalBytes;
    public List<SnapshotDiffEntry> Entries { get; init; } = new();
}

/// <summary>
/// Persistierter Zustand einer Disk-Analyse (JSON-Datei), um zwei Läufe zu
/// vergleichen: "Was ist seit dem letzten Snapshot gewachsen/geschrumpft?"
/// Erstellt über <c>analyze-disk --snapshot &lt;Datei&gt;</c>, verglichen über
/// <c>disk-diff &lt;alt&gt; &lt;neu&gt;</c>. Der Vergleich selbst ist eine reine
/// In-Memory-Funktion (<see cref="Diff"/>) und damit gut testbar.
/// </summary>
public sealed class DiskSnapshot
{
    /// <summary>Formatkennung, damit fremde/ältere JSON-Dateien sauber abgelehnt werden können.</summary>
    public string Format { get; init; } = "wincleaner-disk-snapshot/1";

    public required string Root { get; init; }
    public DateTime CreatedUtc { get; init; }
    public long TotalBytes { get; init; }
    public List<SnapshotEntry> Entries { get; init; } = new();

    /// <summary>Baut einen Snapshot aus einem Analyse-Ergebnis.</summary>
    public static DiskSnapshot FromAnalysis(string root, DiskAnalysis analysis, DateTime? createdUtc = null) => new()
    {
        Root       = root,
        CreatedUtc = createdUtc ?? DateTime.UtcNow,
        TotalBytes = analysis.TotalBytes,
        Entries    = analysis.Entries
            .Select(e => new SnapshotEntry(e.Path, e.Bytes, e.Files, e.IsDir))
            .ToList()
    };

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOut.Options));

    /// <summary>
    /// Lädt einen Snapshot. Wirft <see cref="InvalidDataException"/> bei fremdem
    /// oder unbekanntem Format, damit versehentlich angegebene andere JSON-Dateien
    /// nicht als leerer Snapshot durchgehen.
    /// </summary>
    public static DiskSnapshot Load(string path)
    {
        DiskSnapshot? snap;
        try
        {
            snap = JsonSerializer.Deserialize<DiskSnapshot>(File.ReadAllText(path), JsonOut.Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Keine WinCleaner-Snapshot-Datei: {path} (erstellt mit analyze-disk --snapshot?).", ex);
        }
        if (snap is null || !string.Equals(snap.Format, "wincleaner-disk-snapshot/1", StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Keine WinCleaner-Snapshot-Datei: {path} (erstellt mit analyze-disk --snapshot?).");
        return snap;
    }

    /// <summary>
    /// Vergleicht zwei Snapshots pfadweise (case-insensitive). Liefert je Pfad
    /// Vorher-/Nachher-Größe; Pfade, die nur in einem Snapshot vorkommen, gelten
    /// als neu bzw. entfernt. Unveränderte Einträge (Δ = 0) werden weggelassen,
    /// Sortierung nach |Δ| absteigend.
    /// </summary>
    public static SnapshotDiff Diff(DiskSnapshot old, DiskSnapshot neu)
    {
        var oldByPath = old.Entries
            .GroupBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Bytes, StringComparer.OrdinalIgnoreCase);

        var entries = new List<SnapshotDiffEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var e in neu.Entries)
        {
            if (!seen.Add(e.Path)) continue; // doppelte Pfade im Snapshot ignorieren
            long? oldBytes = oldByPath.TryGetValue(e.Path, out var b) ? b : null;
            if (oldBytes == e.Bytes) continue; // unverändert -> uninteressant
            entries.Add(new SnapshotDiffEntry(e.Path, oldBytes, e.Bytes));
        }

        // Pfade, die es nur im alten Snapshot gibt (entfernt).
        foreach (var e in old.Entries)
        {
            if (seen.Contains(e.Path)) continue;
            if (!seen.Add(e.Path)) continue;
            entries.Add(new SnapshotDiffEntry(e.Path, e.Bytes, null));
        }

        entries.Sort((a, b) => Math.Abs(b.DeltaBytes).CompareTo(Math.Abs(a.DeltaBytes)));

        return new SnapshotDiff
        {
            OldRoot       = old.Root,
            NewRoot       = neu.Root,
            OldCreatedUtc = old.CreatedUtc,
            NewCreatedUtc = neu.CreatedUtc,
            OldTotalBytes = old.TotalBytes,
            NewTotalBytes = neu.TotalBytes,
            Entries       = entries
        };
    }
}
