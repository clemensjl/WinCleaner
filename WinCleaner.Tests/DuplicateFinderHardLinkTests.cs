using System.Runtime.InteropServices;
using WinCleaner.Core;

namespace WinCleaner.Tests;

/// <summary>
/// Tests für die Hardlink-Ersetzung von Duplikaten (M7). Laufen auf dem
/// NTFS-Dev-Laufwerk unter %TEMP% – Hardlinks sind dort real anlegbar.
/// </summary>
public class DuplicateFinderHardLinkTests
{
    private static DuplicateFinder NewFinder() => new(new Logger());

    // Test-eigener P/Invoke, um vorab existierende Hardlinks zu erzeugen.
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string newLink, string existingFile, IntPtr securityAttributes);

    // ---- File-Identity-Helfer ----

    [Fact]
    public void AreHardLinked_TrueForLinks_FalseForCopies()
    {
        using var dir = new TempDir();
        var original = dir.Write("orig.txt", "IDENTITY");
        var copy     = dir.Write("copy.txt", "IDENTITY");
        var link     = Path.Combine(dir.Path, "link.txt");
        Assert.True(CreateHardLink(link, original, IntPtr.Zero));

        Assert.True(DuplicateFinder.AreHardLinked(original, link));
        Assert.False(DuplicateFinder.AreHardLinked(original, copy));
    }

    [Fact]
    public void AreHardLinked_MissingFile_ReturnsFalse()
    {
        using var dir = new TempDir();
        var a = dir.Write("a.txt", "X");
        Assert.False(DuplicateFinder.AreHardLinked(a, Path.Combine(dir.Path, "fehlt.txt")));
    }

    [Fact]
    public void GetHardLinkCount_CountsAllLinks()
    {
        using var dir = new TempDir();
        var original = dir.Write("orig.txt", "COUNTME");
        Assert.Equal(1, DuplicateFinder.GetHardLinkCount(original));

        Assert.True(CreateHardLink(Path.Combine(dir.Path, "l1.txt"), original, IntPtr.Zero));
        Assert.True(CreateHardLink(Path.Combine(dir.Path, "l2.txt"), original, IntPtr.Zero));
        Assert.Equal(3, DuplicateFinder.GetHardLinkCount(original));
    }

    [Fact]
    public void GetHardLinkCount_MissingFile_ReturnsMinusOne()
    {
        using var dir = new TempDir();
        Assert.Equal(-1, DuplicateFinder.GetHardLinkCount(Path.Combine(dir.Path, "fehlt.txt")));
    }

    // ---- Echte Ersetzung ----

    [Fact]
    public void ProcessDuplicates_HardLink_ReplacesDuplicate_NoLeftovers()
    {
        using var dir = new TempDir();
        var a = dir.Write("a.txt", "SAME-CONTENT");
        var b = dir.Write("sub/b.txt", "SAME-CONTENT");

        var finder = NewFinder();
        var groups = finder.Find(dir.Path);
        var result = finder.ProcessDuplicates(groups, KeepStrategy.First,
            protectedPaths: null, hardLink: true, sendToRecycleBin: false, dryRun: false);

        Assert.Equal(1, result.FilesAffected);
        Assert.True(result.BytesAffected > 0);
        Assert.False(result.DryRun);
        Assert.True(result.HardLink);

        // Beide Pfade existieren weiter, zeigen aber auf dieselben Daten.
        Assert.True(File.Exists(a));
        Assert.True(File.Exists(b));
        Assert.True(DuplicateFinder.AreHardLinked(a, b));
        Assert.Equal("SAME-CONTENT", File.ReadAllText(b));

        // Keine Sicherungs-/Temp-Reste im Baum.
        Assert.Empty(Directory.GetFiles(dir.Path, "*.wcbak", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.wclnk", SearchOption.AllDirectories));

        // Aktionen-Liste für --json: genau eine Hardlink-Aktion mit Bytes.
        var act = Assert.Single(result.Actions, x => x.Action == DuplicateFinder.ActHardLink);
        Assert.True(act.Bytes > 0);
    }

    [Fact]
    public void ProcessDuplicates_HardLink_DryRun_ChangesNothing()
    {
        using var dir = new TempDir();
        var a = dir.Write("a.txt", "SAME");
        var b = dir.Write("b.txt", "SAME");

        var finder = NewFinder();
        var groups = finder.Find(dir.Path);
        var result = finder.ProcessDuplicates(groups, KeepStrategy.First,
            protectedPaths: null, hardLink: true, sendToRecycleBin: false, dryRun: true);

        Assert.True(result.DryRun);
        Assert.Equal(1, result.FilesAffected);           // geplant, mit Ersparnis
        Assert.True(result.BytesAffected > 0);
        Assert.False(DuplicateFinder.AreHardLinked(a, b)); // aber nichts verändert
        Assert.Single(result.Actions, x => x.Action == DuplicateFinder.ActPlanHardLink);
    }

    // ---- Guards ----

    [Fact]
    public void ProcessDuplicates_HardLink_AlreadyLinked_Skipped()
    {
        using var dir = new TempDir();
        var a = dir.Write("a.txt", "LINKED");
        var b = Path.Combine(dir.Path, "b.txt");
        Assert.True(CreateHardLink(b, a, IntPtr.Zero));

        var finder = NewFinder();
        var groups = finder.Find(dir.Path); // Hardlinks lesen identischen Inhalt -> Gruppe
        var result = finder.ProcessDuplicates(groups, KeepStrategy.First,
            protectedPaths: null, hardLink: true, sendToRecycleBin: false, dryRun: false);

        Assert.Equal(0, result.FilesAffected);
        Assert.Equal(1, result.FilesSkipped);
        Assert.True(File.Exists(a));
        Assert.True(File.Exists(b));
        Assert.Single(result.Actions, x => x.Action == DuplicateFinder.ActSkipAlreadyLinked);
    }

    [Fact]
    public void ProcessDuplicates_HardLink_AlreadyLinked_DryRunCountsNoSavings()
    {
        using var dir = new TempDir();
        var a = dir.Write("a.txt", "LINKED");
        Assert.True(CreateHardLink(Path.Combine(dir.Path, "b.txt"), a, IntPtr.Zero));

        var finder = NewFinder();
        var groups = finder.Find(dir.Path);
        var result = finder.ProcessDuplicates(groups, KeepStrategy.First,
            protectedPaths: null, hardLink: true, sendToRecycleBin: false, dryRun: true);

        // Guards greifen auch im Probelauf: keine Schein-Ersparnis melden.
        Assert.Equal(0, result.FilesAffected);
        Assert.Equal(0, result.BytesAffected);
        Assert.Equal(1, result.FilesSkipped);
    }

    [Fact]
    public void ProcessDuplicates_HardLink_OtherVolume_Skipped()
    {
        using var dir = new TempDir();
        var keep = dir.Write("a.txt", "SAME");
        // Fiktiver Pfad auf anderem Volume – wird vor jedem Dateizugriff
        // am Volume-Guard aussortiert.
        var other = @"Q:\gibtsnicht\b.txt";
        var group = new DuplicateGroup("deadbeef", new List<string> { keep, other }, 8);

        var finder = NewFinder();
        var result = finder.ProcessDuplicates(new List<DuplicateGroup> { group }, KeepStrategy.First,
            protectedPaths: null, hardLink: true, sendToRecycleBin: false, dryRun: false);

        Assert.Equal(0, result.FilesAffected);
        Assert.Equal(1, result.FilesSkipped);
        Assert.True(File.Exists(keep));
        Assert.Single(result.Actions, x => x.Action == DuplicateFinder.ActSkipOtherVolume);
    }

    [Fact]
    public void ProcessDuplicates_HardLink_MissingKeepFile_FailsSafely()
    {
        using var dir = new TempDir();
        var missingKeep = Path.Combine(dir.Path, "a.txt"); // existiert nicht
        var dup = dir.Write("b.txt", "SURVIVOR");
        var group = new DuplicateGroup("cafebabe", new List<string> { missingKeep, dup }, 16);

        var finder = NewFinder();
        var result = finder.ProcessDuplicates(new List<DuplicateGroup> { group }, KeepStrategy.First,
            protectedPaths: null, hardLink: true, sendToRecycleBin: false, dryRun: false);

        // Fehler je Datei behandeln, nichts kaputt machen.
        Assert.Equal(0, result.FilesAffected);
        Assert.True(File.Exists(dup));
        Assert.Equal("SURVIVOR", File.ReadAllText(dup));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.wcbak", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.wclnk", SearchOption.AllDirectories));
    }

    [Fact]
    public void ProcessDuplicates_HardLink_RespectsProtectedPaths()
    {
        using var dir = new TempDir();
        var prot  = dir.Write("keep/a.txt", "SAME");
        var other = dir.Write("trash/b.txt", "SAME");

        var finder = NewFinder();
        var groups = finder.Find(dir.Path);
        var result = finder.ProcessDuplicates(groups, KeepStrategy.First,
            protectedPaths: new[] { Path.Combine(dir.Path, "keep") },
            hardLink: true, sendToRecycleBin: false, dryRun: false);

        Assert.Equal(1, result.FilesAffected);
        Assert.True(DuplicateFinder.AreHardLinked(prot, other)); // geschützte Datei ist die behaltene
    }

    [Fact]
    public void Find_IgnoresOwnBackupAndTempLinkFiles()
    {
        using var dir = new TempDir();
        dir.Write("a.txt", "SAME");
        dir.Write("a.txt.wcbak", "SAME");
        dir.Write("a.txt.12345678.wclnk", "SAME");

        Assert.Empty(NewFinder().Find(dir.Path));
    }
}
