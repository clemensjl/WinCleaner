using WinCleaner.Core;

namespace WinCleaner.Tests;

public class DuplicateFinderKeepTests
{
    private static DuplicateFinder NewFinder() => new(new Logger());

    [Theory]
    [InlineData(null, KeepStrategy.First)]
    [InlineData("first", KeepStrategy.First)]
    [InlineData("oldest", KeepStrategy.Oldest)]
    [InlineData("newest", KeepStrategy.Newest)]
    [InlineData("shortest-path", KeepStrategy.ShortestPath)]
    [InlineData("longest-path", KeepStrategy.LongestPath)]
    public void ParseKeepStrategy_KnownValues(string? input, KeepStrategy expected)
        => Assert.Equal(expected, DuplicateFinder.ParseKeepStrategy(input));

    [Fact]
    public void ParseKeepStrategy_Unknown_Throws()
        => Assert.Throws<ArgumentException>(() => DuplicateFinder.ParseKeepStrategy("biggest"));

    [Fact]
    public void ProcessDuplicates_KeepNewest_KeepsNewestFile()
    {
        using var dir = new TempDir();
        var older = dir.Write("a.txt", "SAME");
        var newer = dir.Write("sub/b.txt", "SAME");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-10));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

        var finder = NewFinder();
        var groups = finder.Find(dir.Path);
        var result = finder.ProcessDuplicates(groups, KeepStrategy.Newest,
            protectedPaths: null, hardLink: false, sendToRecycleBin: false, dryRun: false);

        Assert.Equal(1, result.FilesAffected);
        Assert.True(File.Exists(newer));   // neueste behalten
        Assert.False(File.Exists(older));  // ältere gelöscht
    }

    [Fact]
    public void ProcessDuplicates_ProtectedPath_NeverDeleted()
    {
        using var dir = new TempDir();
        var prot = dir.Write("keep/a.txt", "SAME");
        var other = dir.Write("trash/b.txt", "SAME");
        var protRoot = Path.Combine(dir.Path, "keep");

        var finder = NewFinder();
        var groups = finder.Find(dir.Path);
        var result = finder.ProcessDuplicates(groups, KeepStrategy.First,
            protectedPaths: new[] { protRoot }, hardLink: false, sendToRecycleBin: false, dryRun: false);

        Assert.True(File.Exists(prot));    // geschützte Datei bleibt
        Assert.False(File.Exists(other));  // ungeschütztes Duplikat entfernt
        Assert.Equal(1, result.FilesAffected);
    }

    [Fact]
    public void ProcessDuplicates_AllProtected_GroupSkipped()
    {
        using var dir = new TempDir();
        var a = dir.Write("keep/a.txt", "SAME");
        var b = dir.Write("keep/b.txt", "SAME");

        var finder = NewFinder();
        var groups = finder.Find(dir.Path);
        var result = finder.ProcessDuplicates(groups, KeepStrategy.First,
            protectedPaths: new[] { Path.Combine(dir.Path, "keep") }, hardLink: false,
            sendToRecycleBin: false, dryRun: false);

        Assert.True(File.Exists(a));
        Assert.True(File.Exists(b));       // beide geschützt -> nichts gelöscht
        Assert.Equal(0, result.FilesAffected);
    }

    [Fact]
    public void ProcessDuplicates_DryRun_DeletesNothing()
    {
        using var dir = new TempDir();
        var a = dir.Write("a.txt", "SAME");
        var b = dir.Write("sub/b.txt", "SAME");

        var finder = NewFinder();
        var groups = finder.Find(dir.Path);
        var result = finder.ProcessDuplicates(groups, KeepStrategy.First,
            protectedPaths: null, hardLink: false, sendToRecycleBin: false, dryRun: true);

        Assert.True(result.DryRun);
        Assert.Equal(1, result.FilesAffected); // geplant
        Assert.True(File.Exists(a));
        Assert.True(File.Exists(b));           // aber nichts verändert
    }
}
