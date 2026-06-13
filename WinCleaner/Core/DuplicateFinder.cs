using System.Security.Cryptography;

namespace WinCleaner.Core;

public record DuplicateGroup(string Hash, List<string> Files, long TotalBytes);

public class DuplicateFinder
{
    private readonly Logger _logger;
    public DuplicateFinder(Logger logger) => _logger = logger;

    public List<DuplicateGroup> Find(string rootPath)
    {
        var groups = new Dictionary<string, List<(string path, long size)>>();
        if (!Directory.Exists(rootPath)) return new();

        // Grob: erst nach Größe gruppieren, dann Hash (Platzhalter -> hier direkt Hash, um simpel zu bleiben)
        foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            try
            {
                var hash = QuickHash(file);
                var size = new FileInfo(file).Length;
                if (!groups.ContainsKey(hash)) groups[hash] = new();
                groups[hash].Add((file, size));
            }
            catch { /* ignore */ }
        }

        var result = new List<DuplicateGroup>();
        foreach (var (hash, files) in groups)
        {
            if (files.Count > 1)
                result.Add(new DuplicateGroup(hash, files.Select(f => f.path).ToList(), files.Sum(f => f.size)));
        }
        _logger.Info($"Duplikatsuche abgeschlossen. Gruppen: {result.Count}");
        return result;
    }

    public void DeleteDuplicates(List<DuplicateGroup> groups)
    {
        foreach (var g in groups)
        {
            // Behalte die erste Datei, lösche Rest
            foreach (var f in g.Files.Skip(1))
            {
                try { File.Delete(f); } catch { /* ignore */ }
            }
        }
    }

    private static string QuickHash(string path)
    {
        using var sha1 = SHA1.Create();
        using var fs = File.OpenRead(path);
        var hash = sha1.ComputeHash(fs);
        return Convert.ToHexString(hash);
    }
}
