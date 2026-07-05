using WinCleaner.SystemTools;

namespace WinCleaner.Tests;

public class LeftoverScannerTests
{
    // ---- PathReferences ----

    [Theory]
    [InlineData(@"""C:\Program Files\AlteApp\svc.exe"" -run", @"C:\Program Files\AlteApp")]
    [InlineData(@"C:\Program Files\AlteApp\svc.exe", @"C:\Program Files\AlteApp")]
    [InlineData(@"C:\Program Files\AlteApp", @"C:\Program Files\AlteApp\")] // Slash am Ende tolerieren
    [InlineData(@"C:\Program Files\AlteApp\sub\tool.exe --flag", @"""C:\Program Files\AlteApp""")] // Anführungszeichen tolerieren
    public void PathReferences_Matches(string text, string installLocation)
        => Assert.True(LeftoverScanner.PathReferences(text, installLocation));

    [Theory]
    [InlineData(@"C:\Program Files\AlteApp2\svc.exe", @"C:\Program Files\AlteApp")] // Präfix ≠ Treffer
    [InlineData(@"C:\Anderswo\svc.exe", @"C:\Program Files\AlteApp")]
    [InlineData("", @"C:\Program Files\AlteApp")]
    [InlineData(null, @"C:\Program Files\AlteApp")]
    [InlineData(@"C:\irgendwas", @"C:\")] // zu kurzer Wurzelpfad wäre ein Alles-Treffer
    public void PathReferences_NoMatch(string? text, string installLocation)
        => Assert.False(LeftoverScanner.PathReferences(text, installLocation));

    [Fact]
    public void PathReferences_IsCaseInsensitive()
        => Assert.True(LeftoverScanner.PathReferences(
            @"c:\program files\alteapp\SVC.EXE", @"C:\PROGRAM FILES\AlteApp"));

    // ---- CSV-Zerlegung (schtasks-Format) ----

    [Fact]
    public void SplitCsvLine_HandlesQuotedFieldsWithCommas()
    {
        var fields = LeftoverScanner.SplitCsvLine(
            "\"PC1\",\"\\Aufgabe, mit Komma\",\"C:\\Tools\\app.exe\"");

        Assert.Equal(3, fields.Count);
        Assert.Equal("PC1", fields[0]);
        Assert.Equal("\\Aufgabe, mit Komma", fields[1]);
        Assert.Equal("C:\\Tools\\app.exe", fields[2]);
    }

    [Fact]
    public void SplitCsvLine_HandlesEscapedQuotes()
    {
        var fields = LeftoverScanner.SplitCsvLine("\"a\",\"mit \"\"Zitat\"\"\"");
        Assert.Equal(2, fields.Count);
        Assert.Equal("mit \"Zitat\"", fields[1]);
    }

    // ---- Task-CSV-Filterung ----

    [Fact]
    public void ParseTaskCsv_FindsTasksReferencingInstallLocation_SkipsHeaderAndDedupes()
    {
        // Nachbau der schtasks-/V-CSV: Kopfzeile (beliebige Sprache) + Datenzeilen;
        // dieselbe Aufgabe erscheint bei mehreren Triggern mehrfach.
        string csv =
            "\"Hostname\",\"Aufgabenname\",\"Nächste Laufzeit\",\"Auszuführende Aufgabe\"\r\n" +
            "\"PC1\",\"\\AlteApp Updater\",\"N/A\",\"C:\\Program Files\\AlteApp\\update.exe /silent\"\r\n" +
            "\"PC1\",\"\\AlteApp Updater\",\"N/A\",\"C:\\Program Files\\AlteApp\\update.exe /silent\"\r\n" +
            "\"PC1\",\"\\Andere Aufgabe\",\"N/A\",\"C:\\Windows\\System32\\cmd.exe\"\r\n";

        var found = LeftoverScanner.ParseTaskCsv(csv, @"C:\Program Files\AlteApp");

        var task = Assert.Single(found);
        Assert.Equal(@"\AlteApp Updater", task.TaskName);
    }

    [Fact]
    public void ParseTaskCsv_EmptyInstallLocation_FindsNothing()
        => Assert.Empty(LeftoverScanner.ParseTaskCsv("\"PC1\",\"\\X\",\"C:\\A\\b.exe\"", ""));

    // ---- Dienste-Matching (reine Logik über PathReferences, Registry-frei) ----

    [Fact]
    public void FindServiceLeftovers_EmptyInstallLocation_ReturnsEmpty()
        => Assert.Empty(LeftoverScanner.FindServiceLeftovers(null, new WinCleaner.Core.Logger()));
}
