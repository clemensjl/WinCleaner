using System.Globalization;

namespace WinCleaner.Core;

/// <summary>Ein Top-Level-Eintrag (Ordner oder Datei) der Speicheranalyse.</summary>
public record DiskEntry(bool IsDir, string Path, long Bytes, int Files);

/// <summary>Eine nach Dateiendung gruppierte Speicheranalyse-Zeile.</summary>
public record ExtensionEntry(string Extension, long Bytes, int Files);

/// <summary>Ergebnis einer Top-Level-Analyse (größte Ordner/Dateien).</summary>
public class DiskAnalysis
{
    public List<DiskEntry> Entries { get; } = new();
    public long TotalBytes { get; set; }
}

/// <summary>Ergebnis einer Gruppierung nach Dateiendung (--by-type).</summary>
public class ExtensionAnalysis
{
    public List<ExtensionEntry> Entries { get; } = new();
    public long TotalBytes { get; set; }
}

/// <summary>
/// Filterkriterien für die Speicheranalyse. Alle Felder sind optional; null bzw.
/// leere Werte bedeuten "kein Filter". Wird sowohl von der Top-Level- als auch
/// von der Endungs-Gruppierung ausgewertet.
/// </summary>
public sealed class DiskFilter
{
    /// <summary>Nur Dateien ab dieser Größe (Bytes); null = kein Mindestmaß.</summary>
    public long? MinSizeBytes { get; init; }

    /// <summary>Nur diese Endungen (jeweils inkl. führendem Punkt, klein); null/leer = alle.</summary>
    public IReadOnlyCollection<string>? Extensions { get; init; }

    /// <summary>
    /// Altersfilter in Tagen relativ zur letzten Änderung (LastWriteTime). Positiv =
    /// nur Dateien älter als n Tage, negativ = nur Dateien jünger als |n| Tage,
    /// null = kein Altersfilter.
    /// </summary>
    public int? AgeDays { get; init; }

    /// <summary>True, wenn überhaupt ein Filter aktiv ist.</summary>
    public bool IsActive =>
        MinSizeBytes is not null ||
        (Extensions is { Count: > 0 }) ||
        AgeDays is not null;

    /// <summary>Prüft, ob eine Datei alle gesetzten Kriterien erfüllt.</summary>
    public bool Matches(FileInfo info)
    {
        if (MinSizeBytes is { } min && info.Length < min) return false;

        if (Extensions is { Count: > 0 })
        {
            var ext = info.Extension.ToLowerInvariant();
            if (!Extensions.Contains(ext)) return false;
        }

        if (AgeDays is { } days)
        {
            var ageDays = (DateTime.Now - info.LastWriteTime).TotalDays;
            if (days >= 0)
            {
                // Nur Dateien, die ÄLTER als n Tage sind.
                if (ageDays < days) return false;
            }
            else
            {
                // Nur Dateien, die JÜNGER als |n| Tage sind.
                if (ageDays > -days) return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Analysiert ein Verzeichnis: berechnet für jedes direkte Kind (Ordner =
/// rekursive Summe, Datei = eigene Größe) Größe und Dateizahl, sortiert
/// absteigend und liefert die Top-N. Optional Gruppierung nach Dateiendung,
/// Filterung nach Größe/Endung/Alter und einstellbare Aggregationstiefe.
/// Junctions/Symlinks werden übersprungen, um Endlosschleifen zu vermeiden.
/// </summary>
public class DiskAnalyzer
{
    private static readonly EnumerationOptions DeepOpts = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible    = true,
        AttributesToSkip      = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false
    };

    private readonly Logger _logger;
    public DiskAnalyzer(Logger logger) => _logger = logger;

    /// <summary>
    /// Top-Level-Analyse der größten Ordner/Dateien. Bestehendes Verhalten
    /// (kein Filter, Tiefe 1) bleibt der Standard.
    /// </summary>
    /// <param name="rootPath">Wurzelverzeichnis.</param>
    /// <param name="topN">Maximale Zahl zurückgegebener Einträge.</param>
    /// <param name="filter">Optionaler Datei-Filter; null = alle Dateien.</param>
    /// <param name="depth">Aggregationstiefe (1 = direkte Kinder, höher = tiefer aufschlüsseln).</param>
    public DiskAnalysis Analyze(string rootPath, int topN = 25, DiskFilter? filter = null, int depth = 1)
    {
        var analysis = new DiskAnalysis();
        if (!Directory.Exists(rootPath))
        {
            _logger.Error($"Pfad nicht gefunden: {rootPath}");
            return analysis;
        }

        if (depth < 1) depth = 1;
        var all = new List<DiskEntry>();

        // Ordner bis zur gewünschten Aggregationstiefe als eigene Einträge.
        foreach (var dir in EnumerateDirsToDepth(rootPath, depth))
        {
            var (bytes, files) = MeasureDirectory(dir, filter);
            if (bytes > 0 || files > 0)
                all.Add(new DiskEntry(IsDir: true, dir, bytes, files));
        }

        // Direkte Dateien im Wurzelordner (zählen immer zur obersten Ebene).
        foreach (var file in SafeEnumerateFiles(rootPath))
        {
            try
            {
                var info = new FileInfo(file);
                if (filter is not null && !filter.Matches(info)) continue;
                all.Add(new DiskEntry(IsDir: false, file, info.Length, 1));
            }
            catch { /* gesperrt/weg */ }
        }

        // Gesamtsumme aus EINER überlappungsfreien Quelle: die Wurzel genau einmal
        // messen (alle Dateien unter rootPath, gleich gefiltert wie die Einträge).
        // So zählt kein Pfad doppelt, auch wenn bei depth>=2 Eltern- UND Unterordner
        // je als eigener Eintrag erscheinen. Bei depth=1 ist das numerisch identisch.
        var (totalBytes, _) = MeasureDirectory(rootPath, filter);
        analysis.TotalBytes = totalBytes;
        analysis.Entries.AddRange(all.OrderByDescending(e => e.Bytes).Take(topN));

        _logger.Info($"Disk-Analyse fertig: {all.Count} Einträge (Tiefe {depth}), " +
                     $"gesamt {FormatSize(analysis.TotalBytes)}.");
        return analysis;
    }

    /// <summary>
    /// Gruppiert alle Dateien unterhalb des Wurzelverzeichnisses nach Dateiendung
    /// und liefert Summe der Größe und Anzahl je Endung, absteigend sortiert (Top-N).
    /// </summary>
    /// <param name="rootPath">Wurzelverzeichnis.</param>
    /// <param name="topN">Maximale Zahl zurückgegebener Endungen.</param>
    /// <param name="filter">Optionaler Datei-Filter; null = alle Dateien.</param>
    public ExtensionAnalysis AnalyzeByExtension(string rootPath, int topN = 25, DiskFilter? filter = null)
    {
        var analysis = new ExtensionAnalysis();
        if (!Directory.Exists(rootPath))
        {
            _logger.Error($"Pfad nicht gefunden: {rootPath}");
            return analysis;
        }

        // Aggregation je Endung.
        var byExt = new Dictionary<string, (long Bytes, int Files)>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in SafeEnumerateAllFiles(rootPath))
        {
            try
            {
                var info = new FileInfo(f);
                if (filter is not null && !filter.Matches(info)) continue;

                var ext = info.Extension.ToLowerInvariant();
                if (string.IsNullOrEmpty(ext)) ext = "(ohne Endung)";

                var cur = byExt.TryGetValue(ext, out var v) ? v : (0L, 0);
                byExt[ext] = (cur.Item1 + info.Length, cur.Item2 + 1);
            }
            catch { /* gesperrt/weg */ }
        }

        analysis.TotalBytes = byExt.Values.Sum(v => v.Bytes);
        analysis.Entries.AddRange(
            byExt.Select(kv => new ExtensionEntry(kv.Key, kv.Value.Bytes, kv.Value.Files))
                 .OrderByDescending(e => e.Bytes)
                 .Take(topN));

        _logger.Info($"Disk-Analyse (nach Endung) fertig: {byExt.Count} Endungen, " +
                     $"gesamt {FormatSize(analysis.TotalBytes)}.");
        return analysis;
    }

    // ---- Helpers ----

    private (long bytes, int files) MeasureDirectory(string dir, DiskFilter? filter)
    {
        long bytes = 0; int files = 0;
        foreach (var f in SafeDeepFiles(dir))
        {
            try
            {
                var info = new FileInfo(f);
                if (filter is not null && !filter.Matches(info)) continue;
                bytes += info.Length; files++;
            }
            catch { /* gesperrt/weg */ }
        }
        return (bytes, files);
    }

    /// <summary>
    /// Liefert alle Ordner, die als eigene Aggregationseinträge gelten sollen:
    /// alle Ordner ab Ebene 1 bis einschließlich <paramref name="depth"/> unterhalb
    /// der Wurzel. Bei depth=1 entspricht das den direkten Unterordnern.
    /// </summary>
    private static IEnumerable<string> EnumerateDirsToDepth(string root, int depth)
    {
        var current = new List<string> { root };
        for (int level = 1; level <= depth; level++)
        {
            var next = new List<string>();
            foreach (var parent in current)
            {
                foreach (var sub in SafeEnumerateDirectories(parent))
                {
                    next.Add(sub);
                    yield return sub;
                }
            }
            current = next;
            if (current.Count == 0) break;
        }
    }

    private static IEnumerable<string> SafeDeepFiles(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*", DeepOpts); }
        catch { return Enumerable.Empty<string>(); }
    }

    private static IEnumerable<string> SafeEnumerateAllFiles(string root)
    {
        try { return Directory.EnumerateFiles(root, "*", DeepOpts); }
        catch { return Enumerable.Empty<string>(); }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string root)
    {
        try { return Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly); }
        catch { return Enumerable.Empty<string>(); }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root)
    {
        try { return Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly); }
        catch { return Enumerable.Empty<string>(); }
    }

    /// <summary>
    /// Parst eine Größenangabe wie "100MB", "2 GB", "512K" oder "1048576" (reine Bytes)
    /// robust in Bytes. Liefert null bei ungültiger Eingabe. Akzeptiert Dezimalpunkt
    /// und -komma sowie optionales "B"/"iB"-Suffix; 1 KB = 1024 Bytes.
    /// </summary>
    public static long? ParseSize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var s = text.Trim();
        // Suffix vom Zahlenteil trennen.
        int i = 0;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] is '.' or ',' || char.IsWhiteSpace(s[i])))
            i++;

        var numberPart = s[..i].Replace(" ", "").Replace(',', '.');
        var unitPart   = s[i..].Trim().ToUpperInvariant();

        if (numberPart.Length == 0) return null;
        if (!double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return null;
        if (value < 0) return null;

        // "iB"-Schreibweise (KiB/MiB) wie auch reines "B" auf Basiseinheit reduzieren.
        unitPart = unitPart.Replace("IB", "").Replace("B", "");

        double factor = unitPart switch
        {
            ""  => 1,
            "K" => 1024d,
            "M" => 1024d * 1024,
            "G" => 1024d * 1024 * 1024,
            "T" => 1024d * 1024 * 1024 * 1024,
            "P" => 1024d * 1024 * 1024 * 1024 * 1024,
            _   => -1
        };
        if (factor < 0) return null;

        return (long)(value * factor);
    }

    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes; int u = 0;
        while (size >= 1024 && u < units.Length - 1) { size /= 1024; u++; }
        return $"{size:N1} {units[u]}";
    }
}
