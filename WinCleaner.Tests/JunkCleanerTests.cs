using WinCleaner.Core;

namespace WinCleaner.Tests;

public class JunkCleanerTests
{
    private static JunkReport ReportFor(string category, string path, Safety safety)
    {
        var report = new JunkReport();
        var (bytes, files) = JunkScanner.MeasureFolder(path);
        report.Items.Add(new JunkItem(category, path, bytes, files, safety));
        return report;
    }

    [Fact]
    public void Clean_DryRun_DeletesNothing()
    {
        using var dir = new TempDir();
        var root = dir.Write("junk/a.tmp", "data");
        dir.Write("junk/sub/b.tmp", "more");
        var junk = Path.Combine(dir.Path, "junk");

        new JunkCleaner(new Logger())
            .Clean(ReportFor("Temp", junk, Safety.Safe), dryRun: true, sendToRecycleBin: false);

        Assert.True(File.Exists(root));
        Assert.True(Directory.Exists(Path.Combine(junk, "sub")));
    }

    [Fact]
    public void Clean_RealRun_RemovesSafeContents_KeepsFolder()
    {
        using var dir = new TempDir();
        dir.Write("junk/a.tmp", "data");
        dir.Write("junk/sub/b.tmp", "more");
        var junk = Path.Combine(dir.Path, "junk");

        new JunkCleaner(new Logger())
            .Clean(ReportFor("Temp", junk, Safety.Safe), dryRun: false, sendToRecycleBin: false);

        // Inhalt weg, der Zielordner selbst bleibt bestehen.
        Assert.True(Directory.Exists(junk));
        Assert.Empty(Directory.EnumerateFileSystemEntries(junk));
    }

    [Fact]
    public void Clean_SkipsNonSafeCategories()
    {
        using var dir = new TempDir();
        var keep = dir.Write("upd/x.bin", "keepme");
        var upd = Path.Combine(dir.Path, "upd");

        new JunkCleaner(new Logger())
            .Clean(ReportFor("Update Cache", upd, Safety.Caution), dryRun: false, sendToRecycleBin: false);

        Assert.True(File.Exists(keep));
    }
}
