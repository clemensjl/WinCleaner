namespace WinCleaner.Core;

public class DiskAnalyzer
{
    private readonly Logger _logger;
    public DiskAnalyzer(Logger logger) => _logger = logger;

    // Gibt einfache Zusammenfassung zurück: größte Ordner + größte Dateien (Platzhalter)
    public List<string[]> Analyze(string rootPath, int topN = 25)
    {
        var rows = new List<string[]>();
        if (!Directory.Exists(rootPath))
        {
            rows.Add(new[] { "Info", "Pfad nicht gefunden", "0", "0" });
            return rows;
        }

        // Größte Ordner (nach Summe)
        var dirSizes = new List<(string path, long size)>();
        foreach (var dir in Directory.EnumerateDirectories(rootPath, "*", SearchOption.TopDirectoryOnly))
        {
            long size = 0;
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { size += new FileInfo(f).Length; } catch {}
                }
                dirSizes.Add((dir, size));
            }
            catch {}
        }

        foreach (var d in dirSizes.OrderByDescending(d => d.size).Take(topN))
            rows.Add(new[] { "Ordner", d.path, (d.size/(1024*1024.0)).ToString("N1"), "-" });

        // Größte Dateien im Root (Top-Level)
        var files = new List<(string path, long size)>();
        foreach (var f in Directory.EnumerateFiles(rootPath, "*", SearchOption.TopDirectoryOnly))
        {
            try { var fi = new FileInfo(f); files.Add((f, fi.Length)); } catch {}
        }
        foreach (var f in files.OrderByDescending(x => x.size).Take(topN))
            rows.Add(new[] { "Datei", f.path, (f.size/(1024*1024.0)).ToString("N1"), "1" });

        _logger.Info("Disk-Analyse (Platzhalter) abgeschlossen.");
        return rows;
    }
}
