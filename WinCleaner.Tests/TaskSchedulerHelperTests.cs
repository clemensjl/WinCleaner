using WinCleaner.SystemTools;
using Logger = WinCleaner.Core.Logger;

namespace WinCleaner.Tests;

public class TaskSchedulerHelperTests
{
    [Theory]
    [InlineData("monthly")]
    [InlineData("")]
    [InlineData("garbage")]
    public void CreateScheduledClean_InvalidInterval_ReturnsFalse_NoSideEffect(string interval)
    {
        // Ungültiges Intervall wird vor jedem schtasks-Aufruf abgelehnt.
        var ok = new TaskSchedulerHelper(new Logger()).CreateScheduledClean(interval);
        Assert.False(ok);
    }

    [Theory]
    [InlineData("daily",  "/SC DAILY")]
    [InlineData("weekly", "/SC WEEKLY /D SUN")]
    public void ScheduleSwitch_MapsKnownIntervals(string interval, string expected)
        => Assert.Equal(expected, TaskSchedulerHelper.ScheduleSwitch(interval));

    [Theory]
    [InlineData("monthly")]
    [InlineData("")]
    public void ScheduleSwitch_UnknownInterval_ReturnsEmpty(string interval)
        => Assert.Equal("", TaskSchedulerHelper.ScheduleSwitch(interval));

    [Fact]
    public void BuildCreateArgs_IncludesYes_SoScheduledRunDoesNotAbort()
    {
        // Regressionsschutz für v1-Fix (1): der geplante Lauf braucht --yes,
        // sonst bricht clean-junk an der Bestätigungsabfrage still ab.
        var args = TaskSchedulerHelper.BuildCreateArgs("/SC DAILY", @"C:\Tools\WinCleaner.exe");

        Assert.Contains("clean-junk --no-dry-run --yes", args);
        Assert.Contains("/SC DAILY", args);
        Assert.Contains("/ST 03:00", args);
        Assert.Contains("WinCleaner Auto-Clean", args);     // /TN
        Assert.Contains(@"\""C:\Tools\WinCleaner.exe\""", args); // escaptes inneres Quoting
    }
}
