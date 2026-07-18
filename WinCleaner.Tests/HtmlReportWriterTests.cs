using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using WinCleaner.Core;

namespace WinCleaner.Tests;

public class HtmlReportWriterTests
{
    // Der Report ist unabhängig von der Systemsprache deutsch formatiert.
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private static string SizeDe(long bytes) => DiskAnalyzer.FormatSize(bytes, De);

    private static DiskTreeNode Node(string name, string path, bool dir, long bytes, int files,
                                     params DiskTreeNode[] children)
        => new(name, path, dir, bytes, files, children);

    private static HtmlReportData SampleData(string rootPath = @"C:\Daten")
    {
        var tree = Node("Daten", rootPath, dir: true, bytes: 1500, files: 5,
            Node("Bilder", rootPath + @"\Bilder", dir: true, bytes: 1000, files: 3),
            Node("Musik",  rootPath + @"\Musik",  dir: true, bytes: 400,  files: 1),
            Node("(Dateien)", rootPath, dir: false, bytes: 100, files: 1));

        var ext = new ExtensionAnalysis { TotalBytes = 1500 };
        ext.Entries.Add(new ExtensionEntry(".jpg", 1000, 3));
        ext.Entries.Add(new ExtensionEntry(".mp3", 400, 1));
        ext.Entries.Add(new ExtensionEntry(".log", 100, 1));

        return new HtmlReportData
        {
            RootPath = rootPath,
            GeneratedAt = new DateTime(2026, 7, 18, 12, 30, 0),
            Tree = tree,
            Extensions = ext
        };
    }

    [Fact]
    public void Build_ContainsCoreMarkers()
    {
        var html = HtmlReportWriter.Build(SampleData());

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<html lang=\"de\">", html);
        Assert.Contains("Speicheranalyse", html);
        Assert.Contains(@"C:\Daten", html);
        Assert.Contains(SizeDe(1500), html); // Gesamtgröße human-readable, deutsch
        Assert.Contains("Treemap", html);
        Assert.Contains("Top-Verzeichnisse", html);
        Assert.Contains("Dateiendung", html);
        Assert.Contains("18.07.2026", html); // deutsches Datumsformat
    }

    [Fact]
    public void Build_EscapesHtmlSpecialCharactersInPaths()
    {
        var evil = @"C:\Täst & Co\<script>alert('x')</script>";
        var html = HtmlReportWriter.Build(SampleData(evil));

        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&amp; Co", html);
        Assert.Contains("Täst", html); // Umlaute bleiben lesbar (UTF-8)
    }

    [Fact]
    public void Build_EmbeddedJsonIsParseable_AndRoundTripsPaths()
    {
        var evil = @"C:\Täst\<script>alert('x')</script>";
        var html = HtmlReportWriter.Build(SampleData(evil));

        var m = Regex.Match(html,
            "<script type=\"application/json\" id=\"wc-data\">(.*?)</script>",
            RegexOptions.Singleline);
        Assert.True(m.Success, "Eingebettetes JSON (#wc-data) nicht gefunden.");

        var json = m.Groups[1].Value;
        // Kein rohes '<' im Datenblock -> kein Ausbruch aus dem <script>-Tag möglich.
        Assert.DoesNotContain("<", json);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(evil, root.GetProperty("rootPath").GetString());
        Assert.Equal(1500, root.GetProperty("tree").GetProperty("bytes").GetInt64());
        Assert.Equal(3, root.GetProperty("tree").GetProperty("children").GetArrayLength());
        Assert.Equal(3, root.GetProperty("extensions").GetProperty("entries").GetArrayLength());
        Assert.Equal(".jpg", root.GetProperty("extensions").GetProperty("entries")[0]
                                 .GetProperty("ext").GetString());
    }

    [Fact]
    public void Build_HasNoExternalReferences()
    {
        var html = HtmlReportWriter.Build(SampleData());

        Assert.DoesNotContain("http://", html);
        Assert.DoesNotContain("https://", html);
        Assert.DoesNotContain("@import", html);
        // Einziges erlaubtes <link> ist das eingebettete data:-Favicon.
        Assert.All(Regex.Matches(html, "<link[^>]*>").Cast<Match>(),
                   m => Assert.Contains("href=\"data:", m.Value));
    }

    [Fact]
    public void Build_SupportsDarkMode()
    {
        var html = HtmlReportWriter.Build(SampleData());
        Assert.Contains("prefers-color-scheme", html);
    }

    [Fact]
    public void Build_RendersTopDirectoriesTable()
    {
        var html = HtmlReportWriter.Build(SampleData());

        Assert.Contains("Bilder", html);
        Assert.Contains("Musik", html);
        Assert.Contains(SizeDe(1000), html);
        // Anteil (Bilder = 1000/1500), deutsch formatiert
        Assert.Contains(string.Format(De, "{0:N1}", 1000 * 100.0 / 1500), html);
    }

    [Fact]
    public void Build_RendersExtensionBreakdown()
    {
        var html = HtmlReportWriter.Build(SampleData());

        Assert.Contains(".jpg", html);
        Assert.Contains(".mp3", html);
        Assert.Contains(".log", html);
        Assert.Contains(SizeDe(400), html);
    }

    [Fact]
    public void Build_DirectoryNamedLikeTemplateToken_DoesNotCorruptReport()
    {
        // Ein Ordner darf legal "%%JSON%%" heißen – das darf die Platzhalter-
        // Ersetzung nicht erneut anstoßen (sonst landet der JSON-Blob in der Tabelle).
        var root = @"C:\Daten";
        var tree = Node("Daten", root, dir: true, bytes: 300, files: 2,
            Node("%%JSON%%", root + @"\%%JSON%%", dir: true, bytes: 200, files: 1),
            Node("%%GENERATED%%", root + @"\%%GENERATED%%", dir: true, bytes: 100, files: 1));
        var ext = new ExtensionAnalysis { TotalBytes = 300 };
        ext.Entries.Add(new ExtensionEntry(".bin", 300, 2));

        var html = HtmlReportWriter.Build(new HtmlReportData
        {
            RootPath = root,
            GeneratedAt = new DateTime(2026, 7, 18, 12, 0, 0),
            Tree = tree,
            Extensions = ext
        });

        // Ordnernamen erscheinen wörtlich, und es gibt weiterhin genau EIN Daten-Script.
        Assert.Contains("%%JSON%%", html);
        Assert.Contains("%%GENERATED%%", html);
        Assert.Single(Regex.Matches(html, "id=\"wc-data\""));

        var m = Regex.Match(html,
            "<script type=\"application/json\" id=\"wc-data\">(.*?)</script>",
            RegexOptions.Singleline);
        Assert.True(m.Success);
        using var doc = JsonDocument.Parse(m.Groups[1].Value); // JSON weiterhin intakt
        Assert.Equal("%%JSON%%", doc.RootElement.GetProperty("tree")
                                    .GetProperty("children")[0].GetProperty("name").GetString());
    }
}
