using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;

namespace WinCleaner.Core;

/// <summary>
/// Eine Gruppe visuell aehnlicher Bilder. <paramref name="Hash"/> ist der
/// dHash der ersten Datei (hex), <paramref name="MaxDistance"/> die groesste
/// Hamming-Distanz innerhalb der Gruppe (0 = alle Bilder exakt gleicher Hash).
/// </summary>
public sealed record SimilarImageGroup(string Hash, List<string> Files, long TotalBytes, int MaxDistance);

/// <summary>
/// Findet visuell aehnliche Bilder (verschieden skalierte oder neu komprimierte
/// Kopien) per dHash (difference hash, 64 Bit): Bild auf 9x8 Graustufen
/// verkleinern, horizontale Nachbarpixel vergleichen. Aehnlichkeit = Hamming-
/// Distanz zweier Hashes; Gruppierung per Union-Find ueber Distanz &lt;= Schwelle.
/// Nutzt System.Drawing (GDI+), daher Windows-only – passend zum
/// net8.0-windows-Target. Nicht dekodierbare Dateien werden mit stderr-Diagnose
/// uebersprungen, Reparse Points (Junctions) nie betreten.
/// </summary>
public class ImageSimilarityFinder
{
    /// <summary>Maximal sinnvolle Schwelle; darueber gruppiert sich fast alles.</summary>
    public const int MaxThreshold = 16;

    // Nur Formate, die GDI+ tatsächlich dekodiert. .webp bewusst NICHT dabei:
    // System.Drawing kann WebP nie öffnen (auch nicht mit installierter
    // WIC-Extension) — jede .webp-Datei würde nur als "nicht dekodierbar"
    // Lärm erzeugen.
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif"
    };

    private readonly Logger _logger;
    public ImageSimilarityFinder(Logger logger) => _logger = logger;

    /// <summary>
    /// Durchsucht <paramref name="rootPath"/> nach Bilddateien, berechnet die
    /// dHashes parallel und gruppiert Bilder mit Hamming-Distanz
    /// &lt;= <paramref name="threshold"/> (0 = nur exakt gleicher Hash).
    /// </summary>
    public List<SimilarImageGroup> Find(string rootPath, bool recurse, int threshold)
    {
        if (!Directory.Exists(rootPath))
        {
            _logger.Error($"Pfad nicht gefunden: {rootPath}");
            return new();
        }

        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = recurse,
            IgnoreInaccessible    = true,
            // Junctions/Symlinks nie folgen (versteckte Spiele-Save-Junctions!).
            AttributesToSkip      = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false
        };

        var candidates = Directory.EnumerateFiles(rootPath, "*", opts)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f)))
            .ToList();

        // Hashes parallel berechnen (CPU-lastig: dekodieren + verkleinern).
        var hashed = new ConcurrentBag<(string Path, ulong Hash, long Size)>();
        int skipped = 0;
        Parallel.ForEach(candidates, file =>
        {
            ulong? hash = TryHashFile(file);
            if (hash is null) { Interlocked.Increment(ref skipped); return; }

            long size;
            try { size = new FileInfo(file).Length; }
            catch { Interlocked.Increment(ref skipped); return; }

            hashed.Add((file, hash.Value, size));
        });

        // Deterministische Reihenfolge unabhaengig vom Parallel-Scheduling.
        var items = hashed.OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase).ToList();

        var clusters = ClusterIndices(items.Select(i => i.Hash).ToList(), threshold);

        var result = new List<SimilarImageGroup>();
        foreach (var cluster in clusters)
        {
            var members = cluster.OrderBy(i => items[i].Path, StringComparer.OrdinalIgnoreCase).ToList();

            int maxDistance = 0;
            for (int a = 0; a < members.Count; a++)
                for (int b = a + 1; b < members.Count; b++)
                    maxDistance = Math.Max(maxDistance,
                        HammingDistance(items[members[a]].Hash, items[members[b]].Hash));

            result.Add(new SimilarImageGroup(
                Hash: items[members[0]].Hash.ToString("X16"),
                Files: members.Select(i => items[i].Path).ToList(),
                TotalBytes: members.Sum(i => items[i].Size),
                MaxDistance: maxDistance));
        }

        result.Sort((a, b) => b.TotalBytes.CompareTo(a.TotalBytes));
        _logger.Info($"Bild-Aehnlichkeitssuche fertig: {items.Count} Bilder gehasht, " +
                     $"{skipped} uebersprungen, {result.Count} Gruppen (Schwelle {threshold}).");
        return result;
    }

    /// <summary>
    /// Berechnet den dHash einer Bilddatei; null, wenn die Datei nicht
    /// dekodierbar ist (Diagnose nach stderr, kein Abbruch).
    /// </summary>
    public ulong? TryHashFile(string path)
    {
        try
        {
            using var image = new Bitmap(path);
            return ComputeDHash(image);
        }
        catch (Exception ex)
        {
            _logger.Info($"Bild nicht dekodierbar, uebersprungen: {path} ({ex.Message})");
            return null;
        }
    }

    private const int HashCols = 9; // 9 Spalten -> 8 horizontale Vergleiche
    private const int HashRows = 8;

    /// <summary>
    /// dHash (difference hash), pure Funktion: Bild auf ein 9x8-Graustufenraster
    /// verkleinern und jedes Rasterfeld mit seinem rechten Nachbarn vergleichen
    /// (8 Vergleiche x 8 Zeilen = 64 Bit, MSB zuerst, zeilenweise).
    /// Die Verkleinerung ist exaktes Box-Averaging (Mittelwert je Rasterzelle)
    /// statt GDI+-DrawImage auf 9x8 – letzteres erzeugt Randartefakte, die
    /// einzelne Bits kippen. Robust gegen Skalierung und Neukompression.
    /// </summary>
    public static ulong ComputeDHash(Bitmap image)
    {
        // Auf 32bpp-ARGB normalisieren (behandelt indizierte GIF/PNG-Paletten)
        // und Transparenz auf einheitlichem Hintergrund fixieren, damit
        // PNG-mit-Alpha und JPEG-Kopie denselben Hash ergeben. Winzige Bilder
        // werden auf Rastergroesse gestreckt, damit keine Zelle leer bleibt.
        int w = Math.Max(image.Width, HashCols);
        int h = Math.Max(image.Height, HashRows);
        using var rgb = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(rgb))
        {
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode   = PixelOffsetMode.Half; // pixelgenaue 1:1-Kopie
            g.DrawImage(image, new Rectangle(0, 0, w, h),
                0, 0, image.Width, image.Height, GraphicsUnit.Pixel);
        }

        // Luma je Rasterzelle aufsummieren (LockBits + Zeilenpuffer statt
        // GetPixel: eine Groessenordnung schneller bei Fotos).
        var sums   = new double[HashRows, HashCols];
        var counts = new long[HashRows, HashCols];

        var data = rgb.LockBits(new Rectangle(0, 0, w, h),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[w * 4];
            for (int y = 0; y < h; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(
                    IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                int by = Math.Min(y * HashRows / h, HashRows - 1);
                for (int x = 0; x < w; x++)
                {
                    int bx = Math.Min(x * HashCols / w, HashCols - 1);
                    int i = x * 4; // BGRA
                    sums[by, bx] += 0.114 * row[i] + 0.587 * row[i + 1] + 0.299 * row[i + 2];
                    counts[by, bx]++;
                }
            }
        }
        finally
        {
            rgb.UnlockBits(data);
        }

        ulong hash = 0;
        for (int y = 0; y < HashRows; y++)
        {
            for (int x = 0; x < HashCols - 1; x++)
            {
                double left  = sums[y, x] / counts[y, x];
                double right = sums[y, x + 1] / counts[y, x + 1];
                hash = (hash << 1) | (left < right ? 1UL : 0UL);
            }
        }
        return hash;
    }

    /// <summary>Anzahl unterschiedlicher Bits zweier 64-Bit-Hashes (0..64).</summary>
    public static int HammingDistance(ulong a, ulong b) => BitOperations.PopCount(a ^ b);

    /// <summary>
    /// Gruppiert Hash-Indizes per Union-Find: alle Paare mit Hamming-Distanz
    /// &lt;= <paramref name="threshold"/> landen (transitiv) in derselben Gruppe.
    /// Nur Gruppen mit mindestens zwei Mitgliedern werden geliefert.
    /// </summary>
    internal static List<List<int>> ClusterIndices(IReadOnlyList<ulong> hashes, int threshold)
    {
        int n = hashes.Count;
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;

        int FindRoot(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]]; // Pfadhalbierung
                i = parent[i];
            }
            return i;
        }

        for (int a = 0; a < n; a++)
            for (int b = a + 1; b < n; b++)
                if (HammingDistance(hashes[a], hashes[b]) <= threshold)
                    parent[FindRoot(a)] = FindRoot(b);

        var byRoot = new Dictionary<int, List<int>>();
        for (int i = 0; i < n; i++)
        {
            int root = FindRoot(i);
            (byRoot.TryGetValue(root, out var list) ? list : byRoot[root] = new()).Add(i);
        }
        return byRoot.Values.Where(g => g.Count >= 2).ToList();
    }
}
