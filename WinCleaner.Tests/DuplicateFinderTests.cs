using WinCleaner.Core;

namespace WinCleaner.Tests;

public class DuplicateFinderTests
{
    private static DuplicateFinder NewFinder() => new(new Logger());

    [Fact]
    public void Find_GroupsOnlyIdenticalContent()
    {
        using var dir = new TempDir();
        dir.Write("a.txt",     "DUPLICATE");     // 9 Bytes
        dir.Write("sub/a2.txt","DUPLICATE");     // gleicher Inhalt -> Duplikat
        dir.Write("b.txt",     "DIFFEREN1");     // 9 Bytes, andere Daten -> KEIN Duplikat
        dir.Write("c.txt",     "SHORT");          // andere Größe

        var groups = NewFinder().Find(dir.Path);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Files.Count);
        Assert.Contains(group.Files, f => f.EndsWith("a.txt"));
        Assert.Contains(group.Files, f => f.EndsWith("a2.txt"));
        Assert.DoesNotContain(group.Files, f => f.EndsWith("b.txt"));
    }

    [Fact]
    public void Find_SkipsEmptyFiles()
    {
        using var dir = new TempDir();
        dir.Write("e1.txt", "");
        dir.Write("e2.txt", "");

        Assert.Empty(NewFinder().Find(dir.Path));
    }

    [Fact]
    public void Find_NoDuplicates_ReturnsEmpty()
    {
        using var dir = new TempDir();
        dir.Write("x.txt", "one");
        dir.Write("y.txt", "two");

        Assert.Empty(NewFinder().Find(dir.Path));
    }

    [Fact]
    public void DeleteDuplicates_KeepsOnePerGroup()
    {
        using var dir = new TempDir();
        var keep   = dir.Write("a.txt",      "SAME");
        var remove = dir.Write("sub/a2.txt", "SAME");

        var finder = NewFinder();
        var groups = finder.Find(dir.Path);
        finder.DeleteDuplicates(groups);

        // Genau eine Datei pro Gruppe bleibt übrig.
        var first = groups.Single().Files[0];
        Assert.True(File.Exists(first));
        var deleted = first.EndsWith("a.txt") ? remove : keep;
        Assert.False(File.Exists(deleted));
    }
}
