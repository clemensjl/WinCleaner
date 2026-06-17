using WinCleaner.Core;

namespace WinCleaner.Tests;

public class DiskAnalyzerExtraTests
{
    private static DiskAnalyzer NewAnalyzer() => new(new Logger());

    [Theory]
    [InlineData("100MB", 104857600L)]
    [InlineData("2 GB", 2147483648L)]
    [InlineData("512K", 524288L)]
    [InlineData("1048576", 1048576L)]
    [InlineData("1,5GB", 1610612736L)]   // Dezimalkomma
    [InlineData("1KiB", 1024L)]          // KiB-Schreibweise
    public void ParseSize_ValidInputs(string input, long expected)
        => Assert.Equal(expected, DiskAnalyzer.ParseSize(input));

    [Theory]
    [InlineData("abc")]
    [InlineData("-5MB")]
    [InlineData("10XB")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseSize_InvalidInputs_ReturnNull(string? input)
        => Assert.Null(DiskAnalyzer.ParseSize(input));

    [Fact]
    public void AnalyzeByExtension_GroupsAndSums()
    {
        using var dir = new TempDir();
        dir.Write("a.txt", new string('a', 100));
        dir.Write("sub/b.txt", new string('b', 200)); // .txt gesamt = 300, 2 Dateien
        dir.Write("c.bin", new string('c', 50));       // .bin = 50, 1 Datei

        var ext = NewAnalyzer().AnalyzeByExtension(dir.Path, topN: 25);

        Assert.Equal(350, ext.TotalBytes);
        var txt = ext.Entries.Single(e => e.Extension == ".txt");
        Assert.Equal(300, txt.Bytes);
        Assert.Equal(2, txt.Files);
        var bin = ext.Entries.Single(e => e.Extension == ".bin");
        Assert.Equal(50, bin.Bytes);
        Assert.Equal(1, bin.Files);
    }

    [Fact]
    public void Analyze_Depth2_DoesNotDoubleCountTotal()
    {
        using var dir = new TempDir();
        dir.Write("sub/inner/a.bin", new string('a', 100)); // inner = 100
        dir.Write("sub/b.bin", new string('b', 200));        // sub gesamt = 300
        // Wurzel-Gesamt = 300. Vor dem Fix zählte depth=2 sub(300)+inner(100)=400.

        var analysis = NewAnalyzer().Analyze(dir.Path, topN: 25, filter: null, depth: 2);

        Assert.Equal(300, analysis.TotalBytes); // keine Doppelzählung
        // Bei depth=2 erscheinen sub UND inner als Einträge (Überlappung erlaubt),
        // aber die Gesamtsumme bleibt überlappungsfrei.
        Assert.Contains(analysis.Entries, e => e.Path.EndsWith("inner"));
    }

    [Fact]
    public void Analyze_Depth1_UnchangedTotal()
    {
        using var dir = new TempDir();
        dir.Write("sub1/a.bin", new string('a', 100));
        dir.Write("sub1/b.bin", new string('b', 200)); // sub1 = 300
        dir.Write("root.bin", new string('r', 500));    // 500

        var analysis = NewAnalyzer().Analyze(dir.Path, topN: 25, filter: null, depth: 1);

        Assert.Equal(800, analysis.TotalBytes);
    }

    [Fact]
    public void Analyze_MinSizeFilter_ExcludesSmallFiles()
    {
        using var dir = new TempDir();
        dir.Write("big.bin", new string('b', 2000));
        dir.Write("small.bin", new string('s', 10));

        var filter = new DiskFilter { MinSizeBytes = 1000 };
        var analysis = NewAnalyzer().Analyze(dir.Path, topN: 25, filter: filter, depth: 1);

        Assert.Equal(2000, analysis.TotalBytes); // small.bin herausgefiltert
    }
}
