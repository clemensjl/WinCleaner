using WinCleaner.Core;

namespace WinCleaner.Tests;

public class DiskAnalyzerTests
{
    private static DiskAnalyzer NewAnalyzer() => new(new Logger());

    [Theory]
    [InlineData(500, " B")]
    [InlineData(1024, "KB")]
    [InlineData(1024 * 1024, "MB")]
    [InlineData(1024L * 1024 * 1024, "GB")]
    public void FormatSize_PicksCorrectUnit(long bytes, string expectedUnit)
    {
        Assert.EndsWith(expectedUnit, DiskAnalyzer.FormatSize(bytes));
    }

    [Fact]
    public void FormatSize_ScalesValue()
    {
        // 1536 B = 1,5 KB -> beginnt mit "1", Einheit KB (kulturunabhängig geprüft)
        var s = DiskAnalyzer.FormatSize(1536);
        Assert.StartsWith("1", s);
        Assert.EndsWith("KB", s);
    }

    [Fact]
    public void Analyze_SumsDirsRecursively_AndSortsBySize()
    {
        using var dir = new TempDir();
        dir.Write("sub1/a.bin", new string('a', 100));
        dir.Write("sub1/b.bin", new string('b', 200)); // sub1 = 300 B, 2 Dateien
        dir.Write("sub2/c.bin", new string('c', 50));  // sub2 = 50 B, 1 Datei
        dir.Write("root.bin",   new string('r', 500)); // Datei = 500 B

        var analysis = NewAnalyzer().Analyze(dir.Path, topN: 25);

        Assert.Equal(850, analysis.TotalBytes);
        Assert.Equal(3, analysis.Entries.Count);

        // Absteigend sortiert: root.bin (500) > sub1 (300) > sub2 (50)
        Assert.EndsWith("root.bin", analysis.Entries[0].Path);
        Assert.False(analysis.Entries[0].IsDir);
        Assert.Equal(500, analysis.Entries[0].Bytes);

        var sub1 = analysis.Entries.Single(e => e.Path.EndsWith("sub1"));
        Assert.True(sub1.IsDir);
        Assert.Equal(300, sub1.Bytes);
        Assert.Equal(2, sub1.Files);
    }

    [Fact]
    public void Analyze_MissingPath_ReturnsEmpty()
    {
        var analysis = NewAnalyzer().Analyze(@"X:\does\not\exist", topN: 10);
        Assert.Empty(analysis.Entries);
        Assert.Equal(0, analysis.TotalBytes);
    }
}
