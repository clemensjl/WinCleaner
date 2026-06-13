using WinCleaner.SystemTools;

namespace WinCleaner.Tests;

public class StartupManagerTests
{
    [Fact]
    public void BuildDisabledBlob_Is12Bytes_StartingWith03()
    {
        var blob = StartupManager.BuildDisabledBlob();

        Assert.Equal(12, blob.Length);
        Assert.Equal(0x03, blob[0]);
    }

    [Fact]
    public void BuildDisabledBlob_IsRecognizedAsDisabled()
    {
        Assert.True(StartupManager.IsDisabledBlob(StartupManager.BuildDisabledBlob()));
    }

    [Fact]
    public void BuildDisabledBlob_EmbedsFileTime()
    {
        var before = DateTime.UtcNow.ToFileTimeUtc();
        var blob = StartupManager.BuildDisabledBlob();
        var after = DateTime.UtcNow.ToFileTimeUtc();

        long stamped = BitConverter.ToInt64(blob, 4);
        Assert.InRange(stamped, before, after);
    }

    [Theory]
    [InlineData(0x02, false)] // aktiv (Task-Manager Standard)
    [InlineData(0x06, false)] // aktiv, anderes gerades erstes Byte
    [InlineData(0x03, true)]  // deaktiviert
    [InlineData(0x01, true)]  // ungerade => deaktiviert
    public void IsDisabledBlob_UsesLowBitOfFirstByte(byte firstByte, bool expectedDisabled)
    {
        var blob = new byte[12];
        blob[0] = firstByte;
        Assert.Equal(expectedDisabled, StartupManager.IsDisabledBlob(blob));
    }
}
