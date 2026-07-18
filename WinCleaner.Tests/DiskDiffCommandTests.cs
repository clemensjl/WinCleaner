using WinCleaner.Commands;
using WinCleaner.Core;

namespace WinCleaner.Tests;

/// <summary>
/// Tests für disk-diff: Warnung bei Snapshots mit unterschiedlichem Scan-Modus
/// (Standard vs. NTFS-Schnellscan behandeln Junction-Einträge unterschiedlich).
/// </summary>
public class DiskDiffCommandTests
{
    private static string WriteSnapshot(TempDir dir, string name, string scanMode)
    {
        var file = System.IO.Path.Combine(dir.Path, name);
        new DiskSnapshot
        {
            Root       = @"C:\Daten",
            CreatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TotalBytes = 10,
            ScanMode   = scanMode,
            Entries    = { new SnapshotEntry(@"C:\Daten\a", 10, 1, true) }
        }.Save(file);
        return file;
    }

    private static (int Rc, List<string> Log) Run(params string[] args)
    {
        var log = new List<string>();
        var ctx = new CommandContext
        {
            Args = args,
            FullArgs = new[] { "disk-diff" }.Concat(args).ToArray(),
            Logger = new Logger((_, msg) => log.Add(msg)),
            Json = false
        };
        return (new DiskDiffCommand().Execute(ctx), log);
    }

    [Fact]
    public void Execute_DifferentScanModes_WarnsAboutJunctionEntries()
    {
        using var dir = new TempDir();
        var oldSnap = WriteSnapshot(dir, "alt.json", "standard");
        var newSnap = WriteSnapshot(dir, "neu.json", "ntfs-fast");

        var (rc, log) = Run(oldSnap, newSnap);

        Assert.Equal(0, rc);
        Assert.Contains(log, m => m.Contains("Scan-Modus") && m.Contains("Junction"));
    }

    [Fact]
    public void Execute_SameScanMode_NoScanModeWarning()
    {
        using var dir = new TempDir();
        var oldSnap = WriteSnapshot(dir, "alt.json", "standard");
        var newSnap = WriteSnapshot(dir, "neu.json", "standard");

        var (rc, log) = Run(oldSnap, newSnap);

        Assert.Equal(0, rc);
        Assert.DoesNotContain(log, m => m.Contains("Scan-Modus"));
    }
}
