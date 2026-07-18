using WinCleaner.Commands;
using WinCleaner.Core;
using Logger = WinCleaner.Core.Logger;

namespace WinCleaner.Tests;

/// <summary>
/// Tests für die Aggregations-Logik des NTFS-Schnellscans (pure Funktionen über
/// synthetische Dateilisten, ohne Adminrechte) sowie die Fallback-Erkennung.
/// Die Aggregation muss dieselben Ergebnisse liefern wie DiskAnalyzer.Analyze /
/// AnalyzeByExtension, damit --fast das Ausgabeformat nicht ändert.
/// </summary>
public class NtfsFastScannerTests
{
    private const string Root = @"C:\root";

    private static FastFile F(string relPath, long bytes, DateTime? lastWrite = null)
        => new(Root + '\\' + relPath, bytes, lastWrite ?? DateTime.Now);

    // ---- Aggregate (Top-Level, Semantik wie DiskAnalyzer.Analyze) ----

    [Fact]
    public void Aggregate_SumsDirsRecursively_AndSortsBySize()
    {
        var files = new List<FastFile>
        {
            F(@"sub1\a.bin", 100),
            F(@"sub1\b.bin", 200), // sub1 = 300 B, 2 Dateien
            F(@"sub2\c.bin", 50),  // sub2 = 50 B, 1 Datei
            F("root.bin", 500)     // Datei direkt in der Wurzel
        };

        var analysis = NtfsFastScanner.Aggregate(files, Root, topN: 25, filter: null, depth: 1);

        Assert.Equal(850, analysis.TotalBytes);
        Assert.Equal(3, analysis.Entries.Count);

        Assert.EndsWith("root.bin", analysis.Entries[0].Path);
        Assert.False(analysis.Entries[0].IsDir);
        Assert.Equal(500, analysis.Entries[0].Bytes);

        var sub1 = analysis.Entries.Single(e => e.Path.EndsWith("sub1"));
        Assert.True(sub1.IsDir);
        Assert.Equal(300, sub1.Bytes);
        Assert.Equal(2, sub1.Files);
    }

    [Fact]
    public void Aggregate_Depth2_ListsNestedDirs_WithoutDoubleCountingTotal()
    {
        var files = new List<FastFile>
        {
            F(@"sub\inner\a.bin", 100), // inner = 100
            F(@"sub\b.bin", 200)        // sub gesamt = 300
        };

        var analysis = NtfsFastScanner.Aggregate(files, Root, topN: 25, filter: null, depth: 2);

        Assert.Equal(300, analysis.TotalBytes); // keine Doppelzählung
        var sub = analysis.Entries.Single(e => e.Path.EndsWith(@"\sub"));
        Assert.Equal(300, sub.Bytes);
        Assert.Equal(2, sub.Files);
        var inner = analysis.Entries.Single(e => e.Path.EndsWith(@"\sub\inner"));
        Assert.Equal(100, inner.Bytes);
        Assert.Equal(1, inner.Files);
    }

    [Fact]
    public void Aggregate_RespectsTopN()
    {
        var files = new List<FastFile> { F("a.bin", 1), F("b.bin", 2), F("c.bin", 3) };
        var analysis = NtfsFastScanner.Aggregate(files, Root, topN: 2, filter: null, depth: 1);
        Assert.Equal(2, analysis.Entries.Count);
        Assert.Equal(6, analysis.TotalBytes); // Total bleibt ungefiltert von topN
        Assert.Equal(3, analysis.Entries[0].Bytes);
    }

    [Fact]
    public void Aggregate_AppliesMinSizeFilter_ToEntriesAndTotal()
    {
        var files = new List<FastFile>
        {
            F(@"sub\big.bin", 2000),
            F(@"sub\small.bin", 10),
            F("tiny.bin", 5)
        };
        var filter = new DiskFilter { MinSizeBytes = 100 };

        var analysis = NtfsFastScanner.Aggregate(files, Root, 25, filter, 1);

        Assert.Equal(2000, analysis.TotalBytes);
        var sub = Assert.Single(analysis.Entries);
        Assert.True(sub.IsDir);
        Assert.Equal(2000, sub.Bytes);
        Assert.Equal(1, sub.Files);
    }

    [Fact]
    public void Aggregate_IgnoresFilesOutsideRoot_AndMatchesRootCaseInsensitively()
    {
        var files = new List<FastFile>
        {
            new(@"C:\ROOT\a.bin", 100, DateTime.Now),   // andere Schreibweise -> zählt
            new(@"C:\rootless\b.bin", 999, DateTime.Now) // anderer Ordner -> ignorieren
        };

        var analysis = NtfsFastScanner.Aggregate(files, Root, 25, null, 1);

        Assert.Equal(100, analysis.TotalBytes);
        var entry = Assert.Single(analysis.Entries);
        Assert.EndsWith("a.bin", entry.Path);
    }

    [Fact]
    public void Aggregate_EmptyFileList_YieldsEmptyAnalysis()
    {
        var analysis = NtfsFastScanner.Aggregate(new List<FastFile>(), Root, 25, null, 1);
        Assert.Empty(analysis.Entries);
        Assert.Equal(0, analysis.TotalBytes);
    }

    // ---- AggregateByExtension (Semantik wie DiskAnalyzer.AnalyzeByExtension) ----

    [Fact]
    public void AggregateByExtension_GroupsAndSums()
    {
        var files = new List<FastFile>
        {
            F("a.txt", 100),
            F(@"sub\b.TXT", 200), // Groß-/Kleinschreibung -> gleiche Gruppe
            F("c.bin", 50),
            F("noext", 7)
        };

        var ext = NtfsFastScanner.AggregateByExtension(files, topN: 25, filter: null);

        Assert.Equal(357, ext.TotalBytes);
        var txt = ext.Entries.Single(e => e.Extension == ".txt");
        Assert.Equal(300, txt.Bytes);
        Assert.Equal(2, txt.Files);
        var none = ext.Entries.Single(e => e.Extension == "(ohne Endung)");
        Assert.Equal(7, none.Bytes);
    }

    [Fact]
    public void AggregateByExtension_AppliesAgeFilter_ViaLastWriteTime()
    {
        var files = new List<FastFile>
        {
            F("old.log", 100, DateTime.Now.AddDays(-40)),
            F("new.log", 50, DateTime.Now)
        };
        var filter = new DiskFilter { AgeDays = 30 }; // nur älter als 30 Tage

        var ext = NtfsFastScanner.AggregateByExtension(files, 25, filter);

        Assert.Equal(100, ext.TotalBytes);
        var log = Assert.Single(ext.Entries);
        Assert.Equal(1, log.Files);
    }

    // ---- DiskFilter-Overload (pfadbasiert, ohne FileInfo) ----

    [Fact]
    public void DiskFilter_PathOverload_ChecksSizeExtensionAndAge()
    {
        var filter = new DiskFilter
        {
            MinSizeBytes = 100,
            Extensions = new[] { ".jpg" },
            AgeDays = -7 // nur jünger als 7 Tage
        };

        Assert.True(filter.Matches(@"C:\x\photo.JPG", 200, DateTime.Now.AddDays(-1)));
        Assert.False(filter.Matches(@"C:\x\photo.jpg", 50, DateTime.Now.AddDays(-1)));   // zu klein
        Assert.False(filter.Matches(@"C:\x\video.mp4", 200, DateTime.Now.AddDays(-1))); // falsche Endung
        Assert.False(filter.Matches(@"C:\x\photo.jpg", 200, DateTime.Now.AddDays(-30))); // zu alt
    }

    // ---- Pfad-Rebase (relative Eingaben -> Ausgabe wie beim Standard-Scan) ----

    [Fact]
    public void RebaseEntryPaths_RewritesToUserInputForm()
    {
        var analysis = new DiskAnalysis { TotalBytes = 100 };
        analysis.Entries.Add(new DiskEntry(false, @"C:\Users\x\Downloads\iso.bin", 100, 1));
        analysis.Entries.Add(new DiskEntry(true, @"C:\Users\x\Downloads\sub", 50, 2));

        NtfsFastScanner.RebaseEntryPaths(analysis, @"C:\Users\x\Downloads", "Downloads");

        Assert.Equal(@"Downloads\iso.bin", analysis.Entries[0].Path);
        Assert.Equal(@"Downloads\sub", analysis.Entries[1].Path);
    }

    [Fact]
    public void RebaseEntryPaths_IdenticalRoots_LeavesPathsUntouched()
    {
        var analysis = new DiskAnalysis();
        analysis.Entries.Add(new DiskEntry(false, @"C:\root\a.bin", 1, 1));

        NtfsFastScanner.RebaseEntryPaths(analysis, @"C:\root", @"C:\root\");

        Assert.Equal(@"C:\root\a.bin", analysis.Entries[0].Path);
    }

    // ---- Fallback-Erkennung / Command-Integration ----

    [Fact]
    public void IsSupported_JunctionRoot_IsRejected()
    {
        using var dir = new TempDir();
        var target = Directory.CreateDirectory(System.IO.Path.Combine(dir.Path, "target")).FullName;
        var junction = System.IO.Path.Combine(dir.Path, "junction");

        // Junction anlegen (braucht keine Adminrechte). Schlägt das fehl
        // (z.B. Nicht-NTFS-Temp), ist der Fall hier nicht prüfbar -> beenden.
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe",
            $"/c mklink /J \"{junction}\" \"{target}\"")
        { CreateNoWindow = true, UseShellExecute = false };
        using (var p = System.Diagnostics.Process.Start(psi)) p!.WaitForExit();
        if (!Directory.Exists(junction)) return;

        Assert.False(NtfsFastScanner.IsSupported(junction, out var reason));
        Assert.Contains("Reparse", reason);
    }

    [Fact]
    public void IsSupported_MissingPath_ReturnsFalseWithReason()
    {
        Assert.False(NtfsFastScanner.IsSupported(@"X:\gibt\es\nicht", out var reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    // ---- Typisierter Grund (GUI reagiert auf den Enum, nicht auf Freitext) ----

    [Fact]
    public void IsSupported_MissingPath_ReportsTypedNotFound()
    {
        Assert.False(NtfsFastScanner.IsSupported(@"X:\gibt\es\nicht", out _, out var block));
        Assert.Equal(FastScanBlockReason.NotFound, block);
    }

    [Fact]
    public void IsSupported_JunctionRoot_ReportsTypedReparseRoot()
    {
        using var dir = new TempDir();
        var target = Directory.CreateDirectory(System.IO.Path.Combine(dir.Path, "target")).FullName;
        var junction = System.IO.Path.Combine(dir.Path, "junction");

        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe",
            $"/c mklink /J \"{junction}\" \"{target}\"")
        { CreateNoWindow = true, UseShellExecute = false };
        using (var p = System.Diagnostics.Process.Start(psi)) p!.WaitForExit();
        if (!Directory.Exists(junction)) return; // Nicht-NTFS-Temp -> nicht prüfbar

        Assert.False(NtfsFastScanner.IsSupported(junction, out _, out var block));
        Assert.Equal(FastScanBlockReason.ReparseRoot, block);
    }

    [Fact]
    public void IsSupported_SupportedOrAdminBlocked_MatchesElevationState()
    {
        // Auf dem lokalen NTFS-Dev-Laufwerk hängt das Ergebnis nur noch von den
        // Adminrechten ab: ohne Admin muss der typisierte Grund NeedsAdmin sein.
        using var dir = new TempDir();
        bool ok = NtfsFastScanner.IsSupported(dir.Path, out _, out var block);

        if (ok) Assert.Equal(FastScanBlockReason.None, block);
        else Assert.Equal(FastScanBlockReason.NeedsAdmin, block);
    }

    [Fact]
    public void TryAnalyze_MissingPath_ReturnsNull_InsteadOfThrowing()
    {
        var scanner = new NtfsFastScanner(new Logger());
        Assert.Null(scanner.TryAnalyze(@"X:\gibt\es\nicht"));
        Assert.Null(scanner.TryAnalyzeByExtension(@"X:\gibt\es\nicht"));
    }

    [Fact]
    public void AnalyzeDisk_FastFlag_IsAllowed()
    {
        var cmd = CommandRegistry.Find("analyze-disk")!;
        Assert.True(Program.ValidateFlags(cmd, new[] { "analyze-disk", @"C:\", "--fast" }, new Logger()));
        Assert.Contains("--fast", cmd.AllowedFlags);
    }
}
