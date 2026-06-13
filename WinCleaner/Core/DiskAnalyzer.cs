namespace WinCleaner.Core;

public record DiskEntry(bool IsDir, string Path, long Bytes, int Files);

public class DiskAnalysis
{
    public List<DiskEntry> Entries { get; } = new();
    public long TotalBytes { get; set; }
}

/// <summary>
/// Analysiert ein Verzeichnis: berechnet für jedes direkte Kind (Ordner =
/// rekursive Summe, Datei = eigene Größe) Größe und Dateizahl, sortiert
/// absteigend und liefert die Top-N. Junctions/Symlinks werden übersprungen,
/// um Endlosschleifen zu vermeiden.
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

    public DiskAnalysis Analyze(string rootPath, int topN = 25)
    {
        var analysis = new DiskAnalysis();
        if (!Directory.Exists(rootPath))
        {
            _logger.Error($"Pfad nicht gefunden: {rootPath}");
            return analysis;
        }

        var all = new List<DiskEntry>();

        // Direkte Unterordner: rekursive Größe + Dateizahl.
        foreach (var dir in SafeEnumerateDirectories(rootPath))
        {
            var (bytes, files) = MeasureDirectory(dir);
            all.Add(new DiskEntry(IsDir: true, dir, bytes, files));
        }

        // Direkte Dateien im Wurzelordner.
        foreach (var file in SafeEnumerateFiles(rootPath))
        {
            try
            {
                long len = new FileInfo(file).Length;
                all.Add(new DiskEntry(IsDir: false, file, len, 1));
            }
            catch { /* gesperrt/weg */ }
        }

        analysis.TotalBytes = all.Sum(e => e.Bytes);
        analysis.Entries.AddRange(all.OrderByDescending(e => e.Bytes).Take(topN));

        _logger.Info($"Disk-Analyse fertig: {all.Count} Top-Level-Einträge, " +
                     $"gesamt {FormatSize(analysis.TotalBytes)}.");
        return analysis;
    }

    // ---- Helpers ----

    private (long bytes, int files) MeasureDirectory(string dir)
    {
        long bytes = 0; int files = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "*", DeepOpts))
        {
            try { bytes += new FileInfo(f).Length; files++; }
            catch { /* gesperrt/weg */ }
        }
        return (bytes, files);
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

    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes; int u = 0;
        while (size >= 1024 && u < units.Length - 1) { size /= 1024; u++; }
        return $"{size:N1} {units[u]}";
    }
}
