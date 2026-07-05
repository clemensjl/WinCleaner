using WinCleaner.SystemTools;
using Logger = WinCleaner.Core.Logger;

namespace WinCleaner.Tests;

public class PrivacySchedulerTests
{
    [Theory]
    [InlineData("monthly")]
    [InlineData("")]
    [InlineData("garbage")]
    public void Schedule_InvalidInterval_ReturnsFalse_NoSideEffect(string interval)
    {
        // Ungültiges Intervall wird vor jedem schtasks-Aufruf abgelehnt.
        var ok = new PrivacyScheduler(new Logger()).Schedule(interval, PrivacyProfile.Standard);
        Assert.False(ok);
    }

    [Fact]
    public void BuildCreateArgs_IncludesNoDryRunAndYes_SoScheduledRunActuallyApplies()
    {
        // Ohne --no-dry-run bliebe der geplante Lauf ein Trockenlauf, ohne --yes
        // würde er an der Bestätigungsabfrage hängen.
        var args = PrivacyScheduler.BuildCreateArgs("/SC WEEKLY /D SUN",
            @"C:\Tools\WinCleaner.exe", PrivacyProfile.Standard);

        Assert.Contains("privacy --apply standard --no-dry-run --yes", args);
        Assert.Contains("/SC WEEKLY /D SUN", args);
        Assert.Contains("/ST 05:00", args);
        Assert.Contains("WinCleaner Privacy-Reapply", args);      // /TN
        Assert.Contains(@"\""C:\Tools\WinCleaner.exe\""", args);  // escaptes inneres Quoting
    }

    [Fact]
    public void BuildCreateArgs_AdvancedProfile_UsesAdvanced()
    {
        var args = PrivacyScheduler.BuildCreateArgs("/SC DAILY",
            @"C:\Tools\WinCleaner.exe", PrivacyProfile.Advanced);
        Assert.Contains("--apply advanced", args);
    }
}
