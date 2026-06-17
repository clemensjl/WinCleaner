using WinCleaner;
using Logger = WinCleaner.Core.Logger;

namespace WinCleaner.Tests;

public class ProgramTests
{
    private static readonly Logger Log = new();

    [Fact]
    public void ValidateFlags_KnownFlags_Allowed()
        => Assert.True(Program.ValidateFlags("clean-junk", new[] { "clean-junk", "--no-dry-run", "--yes" }, Log));

    [Fact]
    public void ValidateFlags_TypoFlag_Rejected()
        => Assert.False(Program.ValidateFlags("clean-junk", new[] { "clean-junk", "--no-dryrun" }, Log));

    [Fact]
    public void ValidateFlags_FlagFromOtherCommand_Rejected()
        => Assert.False(Program.ValidateFlags("scan-junk", new[] { "scan-junk", "--delete" }, Log));

    [Fact]
    public void ValidateFlags_GlobalRelaunchFlag_Allowed()
        => Assert.True(Program.ValidateFlags("startup-disable", new[] { "startup-disable", "Foo", "--relaunched" }, Log));

    [Fact]
    public void ValidateFlags_HelpFlag_AlwaysAllowed()
        => Assert.True(Program.ValidateFlags("unschedule-clean", new[] { "unschedule-clean", "--help" }, Log));

    [Fact]
    public void ValidateFlags_IsCaseInsensitive()
        => Assert.True(Program.ValidateFlags("scan-junk", new[] { "scan-junk", "--JSON" }, Log));

    [Fact]
    public void AppVersion_HasNoBuildMetadataSuffix()
    {
        var v = Program.AppVersion;
        Assert.False(string.IsNullOrWhiteSpace(v));
        Assert.DoesNotContain("+", v); // +commit-hash abgeschnitten
    }
}
