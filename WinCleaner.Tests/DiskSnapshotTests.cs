using WinCleaner.Core;

namespace WinCleaner.Tests;

public class DiskSnapshotTests
{
    private static DiskSnapshot Snap(string root, params (string Path, long Bytes)[] entries) => new()
    {
        Root       = root,
        CreatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        TotalBytes = entries.Sum(e => e.Bytes),
        Entries    = entries.Select(e => new SnapshotEntry(e.Path, e.Bytes, 1, true)).ToList()
    };

    [Fact]
    public void Diff_DetectsGrownShrunkNewAndRemoved()
    {
        var old = Snap(@"C:\Daten",
            (@"C:\Daten\gewachsen", 100),
            (@"C:\Daten\geschrumpft", 500),
            (@"C:\Daten\entfernt", 300),
            (@"C:\Daten\gleich", 42));
        var neu = Snap(@"C:\Daten",
            (@"C:\Daten\gewachsen", 900),
            (@"C:\Daten\geschrumpft", 200),
            (@"C:\Daten\neu", 50),
            (@"C:\Daten\gleich", 42));

        var diff = DiskSnapshot.Diff(old, neu);

        // Unveränderte Einträge tauchen nicht auf.
        Assert.DoesNotContain(diff.Entries, e => e.Path.EndsWith("gleich"));
        Assert.Equal(4, diff.Entries.Count);

        var grown = Assert.Single(diff.Entries, e => e.Path.EndsWith("gewachsen"));
        Assert.Equal(100, grown.OldBytes);
        Assert.Equal(900, grown.NewBytes);
        Assert.Equal(800, grown.DeltaBytes);

        var removed = Assert.Single(diff.Entries, e => e.Path.EndsWith("entfernt"));
        Assert.Null(removed.NewBytes);
        Assert.Equal(-300, removed.DeltaBytes);

        var added = Assert.Single(diff.Entries, e => e.Path.EndsWith("neu"));
        Assert.Null(added.OldBytes);
        Assert.Equal(50, added.DeltaBytes);

        // Sortierung: größte |Δ| zuerst.
        Assert.Equal(800, Math.Abs(diff.Entries[0].DeltaBytes));
        Assert.True(Math.Abs(diff.Entries[1].DeltaBytes) >= Math.Abs(diff.Entries[2].DeltaBytes));

        Assert.Equal(neu.TotalBytes - old.TotalBytes, diff.DeltaTotalBytes);
    }

    [Fact]
    public void Diff_MatchesPathsCaseInsensitive()
    {
        var old = Snap(@"C:\Daten", (@"C:\Daten\Ordner", 100));
        var neu = Snap(@"C:\Daten", (@"c:\daten\ORDNER", 100));

        // Gleicher Pfad in anderer Schreibweise + gleiche Größe -> kein Unterschied.
        Assert.Empty(DiskSnapshot.Diff(old, neu).Entries);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        using var tmp = new TempDir();
        var file = System.IO.Path.Combine(tmp.Path, "snap.json");

        var snap = Snap(@"C:\Daten", (@"C:\Daten\a", 123), (@"C:\Daten\b", 456));
        snap.Save(file);

        var loaded = DiskSnapshot.Load(file);
        Assert.Equal(snap.Root, loaded.Root);
        Assert.Equal(snap.TotalBytes, loaded.TotalBytes);
        Assert.Equal(2, loaded.Entries.Count);
        Assert.Equal(123, loaded.Entries[0].Bytes);
    }

    [Fact]
    public void Load_RejectsForeignJson()
    {
        using var tmp = new TempDir();
        var file = tmp.Write("fremd.json", "{ \"irgendwas\": 1 }");

        // Fremde JSON-Dateien dürfen nicht als leerer Snapshot durchgehen.
        Assert.Throws<InvalidDataException>(() => DiskSnapshot.Load(file));
    }
}
