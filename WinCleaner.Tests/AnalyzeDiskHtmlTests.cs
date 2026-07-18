using WinCleaner.Commands;
using WinCleaner.Core;

namespace WinCleaner.Tests;

public class AnalyzeDiskHtmlTests
{
    private static CommandContext Ctx(params string[] args) => new()
    {
        Args = args,
        FullArgs = new[] { "analyze-disk" }.Concat(args).ToArray(),
        Logger = new Logger()
    };

    [Fact]
    public void Execute_WithHtmlFlag_WritesSelfContainedReport()
    {
        using var dir = new TempDir();
        dir.Write("sub1/a.bin", new string('a', 300));
        dir.Write("sub2/b.bin", new string('b', 100));
        var outFile = Path.Combine(dir.Path, "report.html");

        var rc = new AnalyzeDiskCommand().Execute(Ctx(dir.Path, "--html", outFile));

        Assert.Equal(0, rc);
        Assert.True(File.Exists(outFile), "HTML-Report wurde nicht geschrieben.");

        var html = File.ReadAllText(outFile);
        Assert.Contains("wc-data", html);           // eingebettete Daten
        Assert.Contains("sub1", html);              // Verzeichnis im Report
        Assert.Contains("Treemap", html);
        Assert.DoesNotContain("https://", html);    // selbst-enthalten
    }

    [Fact]
    public void Execute_HtmlEqualsForm_WritesReport()
    {
        using var dir = new TempDir();
        dir.Write("sub/a.bin", new string('a', 100));
        var outFile = Path.Combine(dir.Path, "report.html");

        var rc = new AnalyzeDiskCommand().Execute(Ctx(dir.Path, $"--html={outFile}"));

        Assert.Equal(0, rc);
        Assert.True(File.Exists(outFile), "--html=pfad wurde ignoriert.");
    }

    [Fact]
    public void Execute_HtmlFollowedByFlag_Fails()
    {
        using var dir = new TempDir();
        dir.Write("a.bin", "x");

        // "--by-type" darf nicht als Dateiname interpretiert werden.
        var rc = new AnalyzeDiskCommand().Execute(Ctx(dir.Path, "--html", "--by-type"));

        Assert.Equal(1, rc);
        Assert.False(File.Exists("--by-type"));
    }

    [Fact]
    public void Execute_HtmlFlagWithoutPath_Fails()
    {
        using var dir = new TempDir();
        dir.Write("a.bin", "x");

        var rc = new AnalyzeDiskCommand().Execute(Ctx(dir.Path, "--html"));

        Assert.Equal(1, rc);
    }

    [Fact]
    public void Execute_HtmlCombinedWithByType_WritesReport()
    {
        using var dir = new TempDir();
        dir.Write("sub/a.jpg", new string('a', 200));
        var outFile = Path.Combine(dir.Path, "report.html");

        var rc = new AnalyzeDiskCommand().Execute(Ctx(dir.Path, "--by-type", "--html", outFile));

        Assert.Equal(0, rc);
        Assert.True(File.Exists(outFile));
        Assert.Contains(".jpg", File.ReadAllText(outFile));
    }
}
