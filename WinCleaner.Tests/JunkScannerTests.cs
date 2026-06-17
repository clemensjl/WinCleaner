using WinCleaner.Core;

namespace WinCleaner.Tests;

public class JunkScannerTests
{
    [Fact]
    public void MeasureFolder_SumsRecursively()
    {
        using var dir = new TempDir();
        dir.Write("a.bin", new string('a', 100));
        dir.Write("sub/b.bin", new string('b', 50));

        var (bytes, files) = JunkScanner.MeasureFolder(dir.Path);

        Assert.Equal(150, bytes);
        Assert.Equal(2, files);
    }

    [Fact]
    public void MeasureFolder_MissingPath_ReturnsZero()
    {
        var (bytes, files) = JunkScanner.MeasureFolder(@"X:\does\not\exist");
        Assert.Equal(0, bytes);
        Assert.Equal(0, files);
    }

    [Fact]
    public void MeasureFolder_EmptyFolder_ReturnsZero()
    {
        using var dir = new TempDir();
        var (bytes, files) = JunkScanner.MeasureFolder(dir.Path);
        Assert.Equal(0, bytes);
        Assert.Equal(0, files);
    }
}
