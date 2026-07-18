using System.Drawing;
using System.Drawing.Imaging;
using WinCleaner.Commands;
using WinCleaner.Core;

namespace WinCleaner.Tests;

public class ImageSimilarityFinderTests
{
    private static ImageSimilarityFinder NewFinder() => new(new Logger());

    // ---- Testbild-Generatoren (zur Laufzeit gezeichnet, keine Fixtures) ----

    /// <summary>Horizontaler Grauverlauf: Helligkeit steigt strikt von links nach rechts.</summary>
    private static Bitmap HorizontalGradient(int w, int h)
    {
        var bmp = new Bitmap(w, h);
        for (int x = 0; x < w; x++)
        {
            int v = (int)Math.Round(x * 255.0 / (w - 1));
            for (int y = 0; y < h; y++)
                bmp.SetPixel(x, y, Color.FromArgb(v, v, v));
        }
        return bmp;
    }

    /// <summary>Vertikaler Grauverlauf: Nachbarn in x-Richtung sind identisch.</summary>
    private static Bitmap VerticalGradient(int w, int h)
    {
        var bmp = new Bitmap(w, h);
        for (int y = 0; y < h; y++)
        {
            int v = (int)Math.Round(y * 255.0 / (h - 1));
            for (int x = 0; x < w; x++)
                bmp.SetPixel(x, y, Color.FromArgb(v, v, v));
        }
        return bmp;
    }

    private static string Save(TempDir dir, string relativePath, Bitmap bmp, ImageFormat format)
    {
        var full = Path.Combine(dir.Path, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        bmp.Save(full, format);
        return full;
    }

    // ---- Hamming-Distanz (pure Funktion) ----

    [Theory]
    [InlineData(0UL, 0UL, 0)]
    [InlineData(0UL, ulong.MaxValue, 64)]
    [InlineData(0b1011UL, 0b0001UL, 2)]
    [InlineData(0x8000000000000000UL, 0UL, 1)]
    public void HammingDistance_CountsDifferingBits(ulong a, ulong b, int expected)
        => Assert.Equal(expected, ImageSimilarityFinder.HammingDistance(a, b));

    // ---- dHash (pure Funktion ueber Bitmap) ----

    [Fact]
    public void ComputeDHash_HorizontalGradient_AllBitsSet()
    {
        // Links dunkler als rechts in jeder Spalte -> alle 64 Vergleichsbits = 1.
        using var bmp = HorizontalGradient(64, 64);
        Assert.Equal(ulong.MaxValue, ImageSimilarityFinder.ComputeDHash(bmp));
    }

    [Fact]
    public void ComputeDHash_VerticalGradient_NoBitsSet()
    {
        // Nachbarpixel in x-Richtung gleich hell -> kein "links < rechts"-Bit.
        using var bmp = VerticalGradient(64, 64);
        Assert.Equal(0UL, ImageSimilarityFinder.ComputeDHash(bmp));
    }

    [Fact]
    public void ComputeDHash_IsScaleInvariant()
    {
        using var small = HorizontalGradient(48, 32);
        using var large = HorizontalGradient(160, 120);
        Assert.Equal(
            ImageSimilarityFinder.ComputeDHash(small),
            ImageSimilarityFinder.ComputeDHash(large));
    }

    [Fact]
    public void ComputeDHash_DifferentPatterns_LargeDistance()
    {
        using var a = HorizontalGradient(64, 64);
        using var b = VerticalGradient(64, 64);
        int dist = ImageSimilarityFinder.HammingDistance(
            ImageSimilarityFinder.ComputeDHash(a),
            ImageSimilarityFinder.ComputeDHash(b));
        Assert.True(dist > 16, $"Distanz war {dist}, erwartet > 16");
    }

    [Fact]
    public void TryHashFile_JpegRecompression_SmallDistance()
    {
        using var dir = new TempDir();
        using var bmp = HorizontalGradient(64, 64);
        var png = Save(dir, "a.png", bmp, ImageFormat.Png);
        var jpg = Save(dir, "a.jpg", bmp, ImageFormat.Jpeg);

        var finder = NewFinder();
        ulong? hPng = finder.TryHashFile(png);
        ulong? hJpg = finder.TryHashFile(jpg);

        Assert.NotNull(hPng);
        Assert.NotNull(hJpg);
        int dist = ImageSimilarityFinder.HammingDistance(hPng.Value, hJpg.Value);
        Assert.True(dist <= 5, $"Distanz war {dist}, erwartet <= 5");
    }

    [Fact]
    public void TryHashFile_NonDecodable_ReturnsNull()
    {
        using var dir = new TempDir();
        var fake = dir.Write("fake.png", "das ist kein Bild");
        Assert.Null(NewFinder().TryHashFile(fake));
    }

    // ---- Clustering (pure Funktion ueber Hashes) ----

    [Fact]
    public void ClusterIndices_WithinThreshold_Grouped()
    {
        var hashes = new ulong[] { 0UL, 0b0111UL, ulong.MaxValue }; // d(0,1)=3
        var groups = ImageSimilarityFinder.ClusterIndices(hashes, threshold: 3);
        var g = Assert.Single(groups);
        Assert.Equal(new[] { 0, 1 }, g.OrderBy(i => i).ToArray());
    }

    [Fact]
    public void ClusterIndices_AboveThreshold_NotGrouped()
    {
        var hashes = new ulong[] { 0UL, 0b0111UL }; // d=3
        Assert.Empty(ImageSimilarityFinder.ClusterIndices(hashes, threshold: 2));
    }

    [Fact]
    public void ClusterIndices_TransitiveChain_SingleGroup()
    {
        // d(a,b)=3, d(b,c)=3, aber d(a,c)=6 -> Union-Find verkettet alle drei.
        var hashes = new ulong[] { 0UL, 0b000111UL, 0b111111UL };
        var groups = ImageSimilarityFinder.ClusterIndices(hashes, threshold: 3);
        var g = Assert.Single(groups);
        Assert.Equal(3, g.Count);
    }

    [Fact]
    public void ClusterIndices_ThresholdZero_OnlyExactHashes()
    {
        var hashes = new ulong[] { 5UL, 5UL, 6UL }; // d(0,1)=0, d(*,2)>=1
        var groups = ImageSimilarityFinder.ClusterIndices(hashes, threshold: 0);
        var g = Assert.Single(groups);
        Assert.Equal(new[] { 0, 1 }, g.OrderBy(i => i).ToArray());
    }

    // ---- Find (Ende-zu-Ende ueber Dateisystem) ----

    [Fact]
    public void Find_GroupsScaledCopies_SeparatesDifferentPattern()
    {
        using var dir = new TempDir();
        using (var b1 = HorizontalGradient(64, 64)) Save(dir, "grad64.png", b1, ImageFormat.Png);
        using (var b2 = HorizontalGradient(128, 96)) Save(dir, "grad128.jpg", b2, ImageFormat.Jpeg);
        using (var b3 = VerticalGradient(64, 64)) Save(dir, "vert.png", b3, ImageFormat.Png);

        var groups = NewFinder().Find(dir.Path, recurse: false, threshold: 5);

        var g = Assert.Single(groups);
        Assert.Equal(2, g.Files.Count);
        Assert.Contains(g.Files, f => f.EndsWith("grad64.png"));
        Assert.Contains(g.Files, f => f.EndsWith("grad128.jpg"));
        Assert.True(g.MaxDistance <= 5);
        Assert.True(g.TotalBytes > 0);
    }

    [Fact]
    public void Find_NonDecodableFiles_SkippedWithoutCrash()
    {
        using var dir = new TempDir();
        using (var b1 = HorizontalGradient(64, 64)) Save(dir, "a.png", b1, ImageFormat.Png);
        using (var b2 = HorizontalGradient(96, 96)) Save(dir, "b.png", b2, ImageFormat.Png);
        dir.Write("kaputt.png", "kein Bild");
        dir.Write("kaputt.webp", "auch kein Bild");

        var groups = NewFinder().Find(dir.Path, recurse: false, threshold: 5);

        var g = Assert.Single(groups);
        Assert.Equal(2, g.Files.Count);
        Assert.DoesNotContain(g.Files, f => f.Contains("kaputt"));
    }

    [Fact]
    public void Find_NonImageExtensions_Ignored()
    {
        using var dir = new TempDir();
        using (var b1 = HorizontalGradient(64, 64)) Save(dir, "a.png", b1, ImageFormat.Png);
        dir.Write("notes.txt", "Text");
        dir.Write("data.bin", "Binaer");

        Assert.Empty(NewFinder().Find(dir.Path, recurse: false, threshold: 5));
    }

    [Fact]
    public void Find_WithoutRecurse_IgnoresSubdirectories()
    {
        using var dir = new TempDir();
        using (var b1 = HorizontalGradient(64, 64)) Save(dir, "a.png", b1, ImageFormat.Png);
        using (var b2 = HorizontalGradient(96, 96)) Save(dir, "sub/b.png", b2, ImageFormat.Png);

        Assert.Empty(NewFinder().Find(dir.Path, recurse: false, threshold: 5));
        var g = Assert.Single(NewFinder().Find(dir.Path, recurse: true, threshold: 5));
        Assert.Equal(2, g.Files.Count);
    }

    [Fact]
    public void Find_MissingPath_ReturnsEmpty()
        => Assert.Empty(NewFinder().Find(Path.Combine(Path.GetTempPath(), "wc_gibts_nicht_" + Guid.NewGuid().ToString("N")), false, 5));

    // ---- Kommando-Registrierung + Flags ----

    [Fact]
    public void Command_IsDiscoveredByRegistry()
        => Assert.NotNull(CommandRegistry.Find("find-similar-images"));

    [Fact]
    public void Command_ValidateFlags_KnownFlagsAllowed()
    {
        var cmd = CommandRegistry.Find("find-similar-images")!;
        Assert.True(Program.ValidateFlags(cmd,
            new[] { "find-similar-images", "--recurse", "--threshold", "3", "--delete", "--keep", "oldest", "--no-dry-run", "--yes" },
            new Logger()));
        Assert.False(Program.ValidateFlags(cmd,
            new[] { "find-similar-images", "--treshold" }, new Logger()));
    }
}
