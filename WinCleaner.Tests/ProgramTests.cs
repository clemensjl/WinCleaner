using WinCleaner;
using WinCleaner.Commands;
using WinCleaner.Util;
using Logger = WinCleaner.Core.Logger;

namespace WinCleaner.Tests;

public class ProgramTests
{
    private static readonly Logger Log = new();
    private static ICommand Cmd(string name) => CommandRegistry.Find(name)!;

    [Fact]
    public void ValidateFlags_KnownFlags_Allowed()
        => Assert.True(Program.ValidateFlags(Cmd("clean-junk"), new[] { "clean-junk", "--no-dry-run", "--yes" }, Log));

    [Fact]
    public void ValidateFlags_TypoFlag_Rejected()
        => Assert.False(Program.ValidateFlags(Cmd("clean-junk"), new[] { "clean-junk", "--no-dryrun" }, Log));

    [Fact]
    public void ValidateFlags_FlagFromOtherCommand_Rejected()
        => Assert.False(Program.ValidateFlags(Cmd("scan-junk"), new[] { "scan-junk", "--delete" }, Log));

    [Fact]
    public void ValidateFlags_GlobalRelaunchFlag_Allowed()
        => Assert.True(Program.ValidateFlags(Cmd("startup-disable"), new[] { "startup-disable", "Foo", "--relaunched" }, Log));

    [Fact]
    public void ValidateFlags_HelpFlag_AlwaysAllowed()
        => Assert.True(Program.ValidateFlags(Cmd("unschedule-clean"), new[] { "unschedule-clean", "--help" }, Log));

    [Fact]
    public void ValidateFlags_IsCaseInsensitive()
        => Assert.True(Program.ValidateFlags(Cmd("scan-junk"), new[] { "scan-junk", "--JSON" }, Log));

    [Fact]
    public void AppVersion_HasNoBuildMetadataSuffix()
    {
        var v = AppInfo.Version;
        Assert.False(string.IsNullOrWhiteSpace(v));
        Assert.DoesNotContain("+", v); // +commit-hash abgeschnitten
    }
}
