using System.Runtime.InteropServices;
using WinCleaner.Core;

namespace WinCleaner.Tests;

/// <summary>
/// Tests für die Review-Fixes an <see cref="DuplicateFinder.ProcessDuplicates"/>:
/// echte Dateigrößen statt Gruppen-Durchschnitt (Similar-Image-Gruppen haben
/// ungleiche Größen), Metadaten-Erhalt der Keep-Datei bei der Hardlink-Ersetzung
/// und das Hardlink-Limit inklusive geplanter Links im Probelauf.
/// </summary>
public class DuplicateFinderProcessFixTests
{
    private static DuplicateFinder NewFinder() => new(new Logger());

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string newLink, string existingFile, IntPtr securityAttributes);

    // ---- Fix A: echte Dateigrößen statt Durchschnitt ----

    [Fact]
    public void ProcessDuplicates_DryRun_UsesRealFileSizes_NotGroupAverage()
    {
        using var dir = new TempDir();
        var keep = dir.Write("a.bin", new string('x', 10)); // 10 Bytes
        var dup  = dir.Write("b.bin", new string('y', 30)); // 30 Bytes

        // Similar-Image-Gruppe: ungleiche Größen, TotalBytes = echte Summe (40).
        var group = new DuplicateGroup("beef", new List<string> { keep, dup }, 40);

        var result = NewFinder().ProcessDuplicates(new List<DuplicateGroup> { group },
            KeepStrategy.First, protectedPaths: null, hardLink: false,
            sendToRecycleBin: false, dryRun: true);

        Assert.Equal(1, result.FilesAffected);
        // Echte Größe der Nicht-Keep-Datei (30), nicht der Durchschnitt (20).
        Assert.Equal(30, result.BytesAffected);
        var act = Assert.Single(result.Actions);
        Assert.Equal(30, act.Bytes);
    }

    [Fact]
    public void ProcessDuplicates_RealRun_ReportsRealFileSize()
    {
        using var dir = new TempDir();
        var keep = dir.Write("a.bin", new string('x', 10)); // 10 Bytes
        var dup  = dir.Write("b.bin", new string('y', 30)); // 30 Bytes
        var group = new DuplicateGroup("beef", new List<string> { keep, dup }, 40);

        var result = NewFinder().ProcessDuplicates(new List<DuplicateGroup> { group },
            KeepStrategy.First, protectedPaths: null, hardLink: false,
            sendToRecycleBin: false, dryRun: false);

        Assert.Equal(1, result.FilesAffected);
        Assert.Equal(30, result.BytesAffected);
        Assert.False(File.Exists(dup));
    }

    // ---- Fix B: File.Replace darf Keep-Metadaten nicht verändern ----

    [Fact]
    public void ProcessDuplicates_HardLink_PreservesKeepFileMetadata()
    {
        using var dir = new TempDir();
        var keep = dir.Write("keep.txt", "META-SAME");
        var dup  = dir.Write("dup.txt", "META-SAME");

        var keepCreation = new DateTime(2024, 5, 1, 12, 0, 0, DateTimeKind.Utc);
        var keepWrite    = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        File.SetCreationTimeUtc(keep, keepCreation);
        File.SetLastWriteTimeUtc(keep, keepWrite);
        var keepAttrs = File.GetAttributes(keep);

        // Duplikat mit abweichenden Metadaten: Hidden + alte CreationTime.
        File.SetCreationTimeUtc(dup, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetAttributes(dup, File.GetAttributes(dup) | FileAttributes.Hidden);

        var group = new DuplicateGroup("feed", new List<string> { keep, dup }, 18);
        var result = NewFinder().ProcessDuplicates(new List<DuplicateGroup> { group },
            KeepStrategy.First, protectedPaths: null, hardLink: true,
            sendToRecycleBin: false, dryRun: false);

        Assert.Equal(1, result.FilesAffected);
        Assert.True(DuplicateFinder.AreHardLinked(keep, dup));

        // File.Replace überträgt sonst Attribute/CreationTime des Duplikats auf
        // den geteilten File-Record der Keep-Datei.
        Assert.Equal(keepAttrs, File.GetAttributes(keep));
        Assert.Equal(keepCreation, File.GetCreationTimeUtc(keep));
        Assert.Equal(keepWrite, File.GetLastWriteTimeUtc(keep));
    }

    // ---- Fix C: Hardlink-Limit zählt geplante Links im Probelauf mit ----

    [Fact]
    public void ProcessDuplicates_HardLink_DryRun_CountsPlannedLinksAgainstLimit()
    {
        using var dir = new TempDir();
        var keep = dir.Write("keep.txt", "LIMIT");
        // Keep-Datei auf 1023 Links bringen: genau ein weiterer Link ist erlaubt.
        for (int i = 0; i < DuplicateFinder.MaxHardLinksPerFile - 2; i++)
            Assert.True(CreateHardLink(Path.Combine(dir.Path, $"l{i}.txt"), keep, IntPtr.Zero));

        var c1 = dir.Write("c1.txt", "LIMIT");
        var c2 = dir.Write("c2.txt", "LIMIT");
        var group = new DuplicateGroup("1234", new List<string> { keep, c1, c2 }, 15);

        var result = NewFinder().ProcessDuplicates(new List<DuplicateGroup> { group },
            KeepStrategy.First, protectedPaths: null, hardLink: true,
            sendToRecycleBin: false, dryRun: true);

        // Nur EIN Link passt noch unters Limit; der zweite muss im Probelauf
        // bereits als skip-link-limit gemeldet werden (kein Schein-Sparpotenzial).
        Assert.Equal(1, result.FilesAffected);
        Assert.Equal(1, result.FilesSkipped);
        Assert.Single(result.Actions, a => a.Action == DuplicateFinder.ActSkipLinkLimit);
    }

    // ---- Fix C: unlesbare Datei-Identität -> konservativ überspringen ----

    [Fact]
    public void HardLinkBlocker_UnreadableKeepIdentity_SkipsConservatively()
    {
        using var dir = new TempDir();
        var dup = dir.Write("b.txt", "X");
        var missingKeep = Path.Combine(dir.Path, "fehlt.txt");

        Assert.Equal(DuplicateFinder.ActSkipIdentityUnknown,
            DuplicateFinder.HardLinkBlocker(missingKeep, dup, pendingLinks: 0));
    }

    [Fact]
    public void HardLinkBlocker_UnreadableDuplicateIdentity_SkipsConservatively()
    {
        using var dir = new TempDir();
        var keep = dir.Write("a.txt", "X");
        var missingDup = Path.Combine(dir.Path, "fehlt.txt");

        Assert.Equal(DuplicateFinder.ActSkipIdentityUnknown,
            DuplicateFinder.HardLinkBlocker(keep, missingDup, pendingLinks: 0));
    }

    [Fact]
    public void HardLinkBlocker_TwoNormalCopies_AllowsLink()
    {
        using var dir = new TempDir();
        var keep = dir.Write("a.txt", "X");
        var dup  = dir.Write("b.txt", "X");

        Assert.Null(DuplicateFinder.HardLinkBlocker(keep, dup, pendingLinks: 0));
        // Geplante Links zählen gegen das Limit (Dry-Run-Simulation).
        Assert.Equal(DuplicateFinder.ActSkipLinkLimit,
            DuplicateFinder.HardLinkBlocker(keep, dup,
                pendingLinks: DuplicateFinder.MaxHardLinksPerFile - 1));
    }

    // ---- Fix D: UNC-Pfade nicht pauschal als "kein NTFS" blocken ----

    [Fact]
    public void IsNtfs_UncPath_AllowsAttempt()
    {
        // v2.0.0 konnte Hardlinks über SMB; DriveInfo wirft für UNC-Roots immer.
        // Das Dateisystem ist nicht bestimmbar -> Versuch zulassen, CreateHardLink
        // meldet echte Fehler pro Datei.
        Assert.True(DuplicateFinder.IsNtfs(@"\\server\share\ordner\datei.txt"));
    }
}
