using System.Security.Cryptography;

namespace WinCleaner.Core;

public record DuplicateGroup(string Hash, List<string> Files, long TotalBytes);

/// <summary>
/// Findet inhaltsgleiche Dateien. Statt jede Datei voll zu hashen, filtert ein
/// dreistufiges Verfahren Kandidaten heraus: erst nach Dateigröße gruppieren,
/// dann ein günstiger Partial-Hash (erste 4 KB), und nur bei verbleibenden
/// Kollisionen der volle SHA-256-Hash. Das spart bei großen Bäumen massiv I/O.
/// </summary>
public class DuplicateFinder
{
    private const int PartialBytes = 4096;

    private static readonly EnumerationOptions DeepOpts = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible    = true,
        AttributesToSkip      = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false
    };

    private readonly Logger _logger;
    public DuplicateFinder(Logger logger) => _logger = logger;

    public List<DuplicateGroup> Find(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            _logger.Error($"Pfad nicht gefunden: {rootPath}");
            return new();
        }

        // Stufe 1: nach Größe gruppieren (leere Dateien überspringen).
        var bySize = new Dictionary<long, List<string>>();
        foreach (var file in Directory.EnumerateFiles(rootPath, "*", DeepOpts))
        {
            try
            {
                long size = new FileInfo(file).Length;
                if (size == 0) continue;
                (bySize.TryGetValue(size, out var list) ? list : bySize[size] = new()).Add(file);
            }
            catch { /* gesperrt/weg */ }
        }

        var result = new List<DuplicateGroup>();
        int fullHashes = 0;

        foreach (var (size, sameSize) in bySize)
        {
            if (sameSize.Count < 2) continue; // eindeutige Größe -> kein Duplikat

            // Stufe 2: Partial-Hash (erste 4 KB).
            foreach (var partialGroup in GroupByHash(sameSize, f => Hash(f, PartialBytes)))
            {
                if (partialGroup.Count < 2) continue;

                // Stufe 3: voller Hash nur für echte Kandidaten.
                foreach (var fullGroup in GroupByHash(partialGroup, f => Hash(f, -1)))
                {
                    fullHashes += fullGroup.Count;
                    if (fullGroup.Count < 2) continue;

                    string hash = Hash(fullGroup[0], -1);
                    result.Add(new DuplicateGroup(hash, fullGroup, size * fullGroup.Count));
                }
            }
        }

        result.Sort((a, b) => b.TotalBytes.CompareTo(a.TotalBytes));
        _logger.Info($"Duplikatsuche fertig: {result.Count} Gruppen, {fullHashes} volle Hashes berechnet.");
        return result;
    }

    public void DeleteDuplicates(List<DuplicateGroup> groups)
    {
        int deleted = 0;
        foreach (var g in groups)
        {
            // Erste Datei behalten, Rest löschen.
            foreach (var f in g.Files.Skip(1))
            {
                try { File.Delete(f); deleted++; }
                catch (Exception ex) { _logger.Debug($"Löschen fehlgeschlagen {f}: {ex.Message}"); }
            }
        }
        _logger.Info($"{deleted} Duplikate gelöscht (je Gruppe eine Datei behalten).");
    }

    // ---- Helpers ----

    private static List<List<string>> GroupByHash(List<string> files, Func<string, string> hasher)
    {
        var map = new Dictionary<string, List<string>>();
        foreach (var f in files)
        {
            string h;
            try { h = hasher(f); }
            catch { continue; } // nicht lesbare Datei kann kein verifiziertes Duplikat sein
            (map.TryGetValue(h, out var list) ? list : map[h] = new()).Add(f);
        }
        return map.Values.ToList();
    }

    /// <summary>SHA-256 über die ersten <paramref name="maxBytes"/> Bytes; -1 = ganze Datei.</summary>
    private static string Hash(string path, int maxBytes)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);

        if (maxBytes < 0)
            return Convert.ToHexString(sha.ComputeHash(fs));

        var buffer = new byte[maxBytes];
        int read = fs.Read(buffer, 0, maxBytes);
        return Convert.ToHexString(sha.ComputeHash(buffer, 0, read));
    }
}
