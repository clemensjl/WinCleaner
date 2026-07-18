using WinCleaner.Commands;
using WinCleaner.Core;

namespace WinCleaner.Tests;

public class FindDuplicatesCommandTests
{
    private static CommandContext Ctx(params string[] args) => new()
    {
        Args = args,
        FullArgs = new[] { "find-duplicates" }.Concat(args).ToArray(),
        Logger = new Logger(),
        Json = false
    };

    [Fact]
    public void Execute_DeleteAndHardLink_MutuallyExclusive()
    {
        using var dir = new TempDir();
        var a = dir.Write("a.txt", "SAME");
        var b = dir.Write("b.txt", "SAME");

        var rc = new FindDuplicatesCommand().Execute(Ctx(dir.Path, "--delete", "--hard-link"));

        Assert.Equal(1, rc);                 // Validierungsfehler
        Assert.True(File.Exists(a));         // und nichts verändert
        Assert.True(File.Exists(b));
        Assert.False(DuplicateFinder.AreHardLinked(a, b));
    }

    [Fact]
    public void Execute_HardLink_DryRunByDefault_ChangesNothing()
    {
        using var dir = new TempDir();
        var a = dir.Write("a.txt", "SAME");
        var b = dir.Write("b.txt", "SAME");

        var rc = new FindDuplicatesCommand().Execute(Ctx(dir.Path, "--hard-link"));

        Assert.Equal(0, rc);
        Assert.True(File.Exists(a));
        Assert.True(File.Exists(b));
        Assert.False(DuplicateFinder.AreHardLinked(a, b)); // Probelauf: nichts passiert
    }
}
