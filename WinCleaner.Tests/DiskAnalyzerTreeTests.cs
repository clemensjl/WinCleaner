using System.Diagnostics;
using WinCleaner.Core;

namespace WinCleaner.Tests;

public class DiskAnalyzerTreeTests
{
    private static DiskAnalyzer NewAnalyzer() => new(new Logger());

    [Fact]
    public void AnalyzeTree_BuildsHierarchy_WithRecursiveSizes()
    {
        using var dir = new TempDir();
        dir.Write("sub1/a.bin", new string('a', 100));
        dir.Write("sub1/nested/b.bin", new string('b', 200)); // sub1 = 300
        dir.Write("sub2/c.bin", new string('c', 50));          // sub2 = 50

        var tree = NewAnalyzer().AnalyzeTree(dir.Path);

        Assert.True(tree.IsDir);
        Assert.Equal(350, tree.Bytes);
        Assert.Equal(3, tree.Files);

        var sub1 = tree.Children.Single(c => c.Name == "sub1");
        Assert.Equal(300, sub1.Bytes);
        Assert.Equal(2, sub1.Files);
        var nested = sub1.Children.Single(c => c.Name == "nested");
        Assert.Equal(200, nested.Bytes);

        var sub2 = tree.Children.Single(c => c.Name == "sub2");
        Assert.Equal(50, sub2.Bytes);

        // Absteigend nach Größe sortiert.
        Assert.Equal(tree.Children.OrderByDescending(c => c.Bytes).Select(c => c.Name),
                     tree.Children.Select(c => c.Name));
    }

    [Fact]
    public void AnalyzeTree_AggregatesDirectFiles_WhenDirHasSubdirs()
    {
        using var dir = new TempDir();
        dir.Write("sub/a.bin", new string('a', 100));
        dir.Write("root1.bin", new string('r', 40));
        dir.Write("root2.bin", new string('r', 60)); // direkte Dateien = 100

        var tree = NewAnalyzer().AnalyzeTree(dir.Path);

        Assert.Equal(200, tree.Bytes);
        var filesNode = tree.Children.Single(c => !c.IsDir);
        Assert.Equal("(Dateien)", filesNode.Name);
        Assert.Equal(100, filesNode.Bytes);
        Assert.Equal(2, filesNode.Files);
        Assert.Empty(filesNode.Children);
    }

    [Fact]
    public void AnalyzeTree_RespectsMaxDepth()
    {
        using var dir = new TempDir();
        dir.Write("l1/l2/l3/deep.bin", new string('d', 100));

        var tree = NewAnalyzer().AnalyzeTree(dir.Path, maxDepth: 2);

        var l1 = tree.Children.Single(c => c.Name == "l1");
        var l2 = l1.Children.Single(c => c.Name == "l2");
        // Tiefe erschöpft: keine Kinder mehr, aber Größe stimmt rekursiv.
        Assert.Equal(100, l2.Bytes);
        Assert.Empty(l2.Children);
    }

    [Fact]
    public void AnalyzeTree_MissingPath_ReturnsEmptyRoot()
    {
        var tree = NewAnalyzer().AnalyzeTree(@"X:\does\not\exist");
        Assert.Equal(0, tree.Bytes);
        Assert.Empty(tree.Children);
    }

    [Fact]
    public void AnalyzeTree_CapsChildren_AndAggregatesRest()
    {
        using var dir = new TempDir();
        for (int i = 0; i < 6; i++)
            dir.Write($"sub{i}/f.bin", new string('x', 100 + i));

        var tree = NewAnalyzer().AnalyzeTree(dir.Path, maxChildren: 4);

        // 3 größte Ordner + Sammelknoten "(Weitere)".
        Assert.Equal(4, tree.Children.Count);
        var rest = tree.Children.Single(c => c.Name == "(Weitere)");
        Assert.Equal(tree.Bytes, tree.Children.Sum(c => c.Bytes));
        Assert.True(rest.Bytes > 0);
    }

    [Fact]
    public void AnalyzeTree_SkipsJunctions()
    {
        using var dir = new TempDir();
        dir.Write("real/a.bin", new string('a', 100));
        var junction = Path.Combine(dir.Path, "junction");

        var psi = new ProcessStartInfo("cmd.exe",
            $"/c mklink /J \"{junction}\" \"{Path.Combine(dir.Path, "real")}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        };
        using (var p = Process.Start(psi)) p!.WaitForExit();
        if (!Directory.Exists(junction)) return; // Junction nicht erstellbar -> nicht aussagekräftig

        var tree = NewAnalyzer().AnalyzeTree(dir.Path);

        Assert.Equal(100, tree.Bytes); // nicht doppelt gezählt
        Assert.DoesNotContain(tree.Children, c => c.Name == "junction");
    }
}
