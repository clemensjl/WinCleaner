using WinCleaner.Core;
using WinCleaner.Gui.ViewModels;

namespace WinCleaner.Tests;

/// <summary>
/// Sichert die UI-freie Logik der Speicher-Seite ab: Behalte-Strategie-Auswahl,
/// Argumentbau für den elevated Schnellscan, Snapshot-Konvertierung und die
/// Vorschau-/Ergebnistexte des Preview-first-Flows.
/// </summary>
public class StorageLogicTests
{
    // ---- Behalte-Strategien ----

    [Fact]
    public void KeepStrategies_CoverAllStrategiesExactlyOnce_DefaultIsFirst()
    {
        var values = StorageLogic.KeepStrategies.Select(o => o.Value).ToList();

        Assert.Equal(Enum.GetValues<KeepStrategy>().Length, values.Count);
        Assert.Equal(values.Count, values.Distinct().Count());
        Assert.Equal(KeepStrategy.First, StorageLogic.KeepStrategies[0].Value);
    }

    [Fact]
    public void KeepStrategies_HaveDistinctNonEmptyLabels()
    {
        var labels = StorageLogic.KeepStrategies.Select(o => o.Label).ToList();

        Assert.All(labels, l => Assert.False(string.IsNullOrWhiteSpace(l)));
        Assert.Equal(labels.Count, labels.Distinct().Count());
    }

    // ---- Schnellscan-Argumente (elevated CLI-Lauf) ----

    [Fact]
    public void BuildFastScanArguments_QuotesPathsAndSetsFlags()
    {
        var args = StorageLogic.BuildFastScanArguments(
            @"C:\Alte Daten", 50, @"C:\Temp Dir\snap.json");

        Assert.StartsWith("analyze-disk ", args);
        Assert.Contains("\"C:\\Alte Daten\"", args);
        Assert.Contains("--fast", args);
        Assert.Contains("--top 50", args);
        Assert.Contains("--snapshot \"C:\\Temp Dir\\snap.json\"", args);
    }

    [Fact]
    public void BuildFastScanArguments_LeavesSimplePathsUnquoted()
    {
        var args = StorageLogic.BuildFastScanArguments(@"C:\Fotos", 25, @"C:\snap.json");

        Assert.Contains(@"analyze-disk C:\Fotos --fast --top 25 --snapshot C:\snap.json", args);
    }

    // ---- CLI-Preflight für den elevated Schnellscan ----

    [Theory]
    [InlineData("analyze-disk <Pfad> [--fast] [--by-type]\n  Optionen: --fast, --top", true)]
    [InlineData("analyze-disk <Pfad> [--by-type] [--top <n>]\n  Optionen: --by-type, --top", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void CliSupportsFastScan_DetectsFastFlagInHelpOutput(string? helpOutput, bool expected)
    {
        Assert.Equal(expected, StorageLogic.CliSupportsFastScan(helpOutput));
    }

    // ---- Snapshot -> Analyse ----

    [Fact]
    public void ToAnalysis_SortsDescendingLimitsAndKeepsTotals()
    {
        var snapshot = new DiskSnapshot
        {
            Root = @"C:\",
            TotalBytes = 999,
            Entries =
            {
                new SnapshotEntry(@"C:\klein", 10, 1, IsDir: true),
                new SnapshotEntry(@"C:\gross", 300, 7, IsDir: true),
                new SnapshotEntry(@"C:\datei.bin", 20, 1, IsDir: false)
            }
        };

        var analysis = StorageLogic.ToAnalysis(snapshot, top: 2);

        Assert.Equal(999, analysis.TotalBytes);
        Assert.Equal(2, analysis.Entries.Count);
        Assert.Equal(@"C:\gross", analysis.Entries[0].Path);
        Assert.True(analysis.Entries[0].IsDir);
        Assert.Equal(7, analysis.Entries[0].Files);
        Assert.Equal(@"C:\datei.bin", analysis.Entries[1].Path);
        Assert.False(analysis.Entries[1].IsDir);
    }

    // ---- Bildgruppen -> Duplikatgruppen ----

    [Fact]
    public void ToDuplicateGroups_MapsHashFilesAndBytes()
    {
        var groups = new[]
        {
            new SimilarImageGroup("ABCD", new List<string> { "a.jpg", "b.jpg" }, 2048, MaxDistance: 3)
        };

        var dup = StorageLogic.ToDuplicateGroups(groups);

        var g = Assert.Single(dup);
        Assert.Equal("ABCD", g.Hash);
        Assert.Equal(new[] { "a.jpg", "b.jpg" }, g.Files);
        Assert.Equal(2048, g.TotalBytes);
    }

    // ---- Vorschau-/Ergebnistexte ----

    private static DuplicateActionResult Result(
        int groups, int groupsSkipped, int files, int filesSkipped, long bytes) =>
        new(groups, groupsSkipped, files, filesSkipped, bytes,
            DryRun: true, HardLink: true, SentToRecycleBin: true,
            Actions: Array.Empty<DuplicateFileAction>());

    [Fact]
    public void BuildHardLinkPreview_ContainsCountsSavingsAndSkips()
    {
        var text = StorageLogic.BuildHardLinkPreview(Result(3, 1, 5, 2, 1024L * 1024));

        Assert.Contains("5 Duplikate in 3 Gruppen", text);
        Assert.Contains(DiskAnalyzer.FormatSize(1024L * 1024), text);
        Assert.Contains("2 Dateien werden übersprungen", text);
        Assert.Contains("Papierkorb", text);
        Assert.Contains("Jetzt ersetzen?", text);
    }

    [Fact]
    public void BuildHardLinkPreview_OmitsSkipLineWhenNothingSkipped()
    {
        var text = StorageLogic.BuildHardLinkPreview(Result(2, 0, 4, 0, 100));

        Assert.DoesNotContain("übersprungen", text);
    }

    [Fact]
    public void BuildHardLinkResultText_ReportsReplacedSkippedAndSaved()
    {
        var text = StorageLogic.BuildHardLinkResultText(Result(2, 0, 4, 1, 4096));

        Assert.Contains("4 Duplikate durch Hardlinks ersetzt", text);
        Assert.Contains("1 übersprungen", text);
        Assert.Contains($"{DiskAnalyzer.FormatSize(4096)} gespart", text);
    }

    [Fact]
    public void BuildImageDeletePreview_WarnsAboutSimilarityAndShowsCounts()
    {
        var text = StorageLogic.BuildImageDeletePreview(Result(2, 0, 3, 0, 2048));

        Assert.Contains("3 ähnliche Bilder aus 2 Gruppen", text);
        Assert.Contains(DiskAnalyzer.FormatSize(2048), text);
        Assert.Contains("byte-identisch", text);
        Assert.Contains("Jetzt verschieben?", text);
    }

    [Fact]
    public void BuildImageDeleteResultText_ReportsMovedAndSkipped()
    {
        var text = StorageLogic.BuildImageDeleteResultText(Result(2, 0, 3, 1, 2048));

        Assert.Contains("3 ähnliche Bilder in den Papierkorb verschoben", text);
        Assert.Contains("1 übersprungen", text);
        Assert.Contains(DiskAnalyzer.FormatSize(2048), text);
    }

    [Fact]
    public void DefaultReportFileName_UsesTimestampAndHtmlExtension()
    {
        var name = StorageLogic.DefaultReportFileName(new DateTime(2026, 7, 19, 14, 5, 0));

        Assert.Equal("speicheranalyse-20260719-1405.html", name);
    }

    // ---- Anzeige-Zeile der Bildgruppen ----

    [Fact]
    public void ImageGroupRow_FormatsHeaderAndFileList()
    {
        var row = new ImageGroupRow(new SimilarImageGroup(
            "FFAA", new List<string> { @"C:\a.jpg", @"C:\b.jpg" }, 4096, MaxDistance: 3));

        Assert.Contains("2 Bilder", row.Header);
        Assert.Contains(DiskAnalyzer.FormatSize(4096), row.Header);
        Assert.Contains("max. Abstand 3", row.Header);
        Assert.Equal($"C:\\a.jpg\nC:\\b.jpg", row.Files);
    }
}
