using System.Text;
using WinCleaner.Core;

namespace WinCleaner.Tests;

/// <summary>
/// Tests für das USN-Record-Parsing als pure Funktion über synthetische
/// byte[]-Puffer im Format von FSCTL_ENUM_USN_DATA (8 Byte Fortsetzungs-FRN,
/// danach USN_RECORD_V2-Einträge). Läuft ohne Adminrechte.
/// </summary>
public class UsnRecordParserTests
{
    private const uint AttrDir     = 0x10;  // FILE_ATTRIBUTE_DIRECTORY
    private const uint AttrReparse = 0x400; // FILE_ATTRIBUTE_REPARSE_POINT
    private const uint AttrNormal  = 0x80;  // FILE_ATTRIBUTE_NORMAL

    // ---- Puffer-Baukasten (USN_RECORD_V2-Layout) ----

    private static byte[] BuildBuffer(ulong nextFrn,
        params (ulong Frn, ulong Parent, uint Attrs, string Name)[] records)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(nextFrn); // erste 8 Bytes: nächste Start-FRN
        foreach (var r in records)
            WriteRecord(w, r.Frn, r.Parent, r.Attrs, r.Name);
        return ms.ToArray();
    }

    private static void WriteRecord(BinaryWriter w, ulong frn, ulong parent, uint attrs, string name)
    {
        var nameBytes = Encoding.Unicode.GetBytes(name);
        int rawLen = 60 + nameBytes.Length;
        int padded = (rawLen + 7) & ~7; // Records sind 8-Byte-aligned

        w.Write((uint)padded);          // RecordLength
        w.Write((ushort)2);             // MajorVersion
        w.Write((ushort)0);             // MinorVersion
        w.Write(frn);                   // FileReferenceNumber
        w.Write(parent);                // ParentFileReferenceNumber
        w.Write(0L);                    // Usn
        w.Write(0L);                    // TimeStamp
        w.Write(0u);                    // Reason
        w.Write(0u);                    // SourceInfo
        w.Write(0u);                    // SecurityId
        w.Write(attrs);                 // FileAttributes
        w.Write((ushort)nameBytes.Length); // FileNameLength (Bytes)
        w.Write((ushort)60);            // FileNameOffset
        w.Write(nameBytes);
        for (int i = rawLen; i < padded; i++) w.Write((byte)0);
    }

    // ---- ParseEnumBuffer ----

    [Fact]
    public void ParseEnumBuffer_ReadsRecordsAndNextFrn()
    {
        var buf = BuildBuffer(0xABCD,
            (Frn: 100, Parent: 5, Attrs: AttrDir, Name: "Users"),
            (Frn: 101, Parent: 100, Attrs: AttrNormal, Name: "notes.txt"));

        var entries = new List<UsnEntry>();
        var next = UsnRecordParser.ParseEnumBuffer(buf, buf.Length, entries);

        Assert.Equal(0xABCDul, next);
        Assert.Equal(2, entries.Count);

        Assert.Equal(100ul, entries[0].Frn);
        Assert.Equal(5ul, entries[0].ParentFrn);
        Assert.Equal("Users", entries[0].Name);
        Assert.True(entries[0].IsDirectory);
        Assert.False(entries[0].IsReparsePoint);

        Assert.Equal("notes.txt", entries[1].Name);
        Assert.False(entries[1].IsDirectory);
    }

    [Fact]
    public void ParseEnumBuffer_ReparseAttribute_IsFlagged()
    {
        var buf = BuildBuffer(1, (Frn: 7, Parent: 5, Attrs: AttrDir | AttrReparse, Name: "Junction"));

        var entries = new List<UsnEntry>();
        UsnRecordParser.ParseEnumBuffer(buf, buf.Length, entries);

        Assert.Single(entries);
        Assert.True(entries[0].IsDirectory);
        Assert.True(entries[0].IsReparsePoint);
    }

    [Fact]
    public void ParseEnumBuffer_TruncatedSecondRecord_ParsesOnlyFirst()
    {
        var buf = BuildBuffer(9,
            (Frn: 1, Parent: 5, Attrs: AttrDir, Name: "ok"),
            (Frn: 2, Parent: 5, Attrs: AttrDir, Name: "cut"));

        // validLength mitten in den zweiten Record legen.
        int validLength = buf.Length - 10;
        var entries = new List<UsnEntry>();
        var next = UsnRecordParser.ParseEnumBuffer(buf, validLength, entries);

        Assert.Equal(9ul, next);
        Assert.Single(entries);
        Assert.Equal("ok", entries[0].Name);
    }

    [Fact]
    public void ParseEnumBuffer_TooShortBuffer_ReturnsNoEntries()
    {
        var entries = new List<UsnEntry>();
        var next = UsnRecordParser.ParseEnumBuffer(new byte[4], 4, entries);
        Assert.Equal(0ul, next);
        Assert.Empty(entries);
    }

    [Fact]
    public void ParseEnumBuffer_ZeroRecordLength_StopsWithoutLooping()
    {
        // 8 Byte Next-FRN + 64 Byte Nullen (RecordLength=0 -> muss abbrechen).
        var buf = new byte[8 + 64];
        buf[0] = 0x2A; // next FRN = 42

        var entries = new List<UsnEntry>();
        var next = UsnRecordParser.ParseEnumBuffer(buf, buf.Length, entries);

        Assert.Equal(42ul, next);
        Assert.Empty(entries);
    }

    [Fact]
    public void ParseEnumBuffer_UnknownMajorVersion_IsSkipped()
    {
        var buf = BuildBuffer(1,
            (Frn: 1, Parent: 5, Attrs: AttrDir, Name: "v2a"),
            (Frn: 2, Parent: 5, Attrs: AttrDir, Name: "v3x"),
            (Frn: 3, Parent: 5, Attrs: AttrDir, Name: "v2b"));

        // MajorVersion des zweiten Records auf 3 patchen. Offset: 8 (Kopf) +
        // RecordLength(1. Record) + 4.
        int rec1Len = BitConverter.ToInt32(buf, 8);
        buf[8 + rec1Len + 4] = 3;

        var entries = new List<UsnEntry>();
        UsnRecordParser.ParseEnumBuffer(buf, buf.Length, entries);

        Assert.Equal(new[] { "v2a", "v2b" }, entries.Select(e => e.Name).ToArray());
    }

    // ---- BuildDirectoryPaths ----

    private static Dictionary<ulong, UsnEntry> Dirs(params UsnEntry[] entries)
        => entries.ToDictionary(e => e.Frn & UsnRecordParser.FrnIndexMask);

    [Fact]
    public void BuildDirectoryPaths_ResolvesNestedChains()
    {
        var dirs = Dirs(
            new UsnEntry(100, 5, AttrDir, "Users"),
            new UsnEntry(200, 100, AttrDir, "Clemens"),
            new UsnEntry(300, 200, AttrDir, "Documents"));

        var paths = UsnRecordParser.BuildDirectoryPaths(dirs, rootFrn: 5, rootPath: @"C:\");

        Assert.Equal(@"C:", paths[5]);
        Assert.Equal(@"C:\Users", paths[100]);
        Assert.Equal(@"C:\Users\Clemens", paths[200]);
        Assert.Equal(@"C:\Users\Clemens\Documents", paths[300]);
    }

    [Fact]
    public void BuildDirectoryPaths_MasksFrnSequenceNumbers()
    {
        // Parent-Referenz trägt eine andere Sequence-Nummer (obere 16 Bit) als
        // der Eintrag selbst -> muss trotzdem aufgelöst werden.
        ulong seq = 0x000A_0000_0000_0000;
        var dirs = Dirs(new UsnEntry(seq | 100, 0x0003_0000_0000_0005, AttrDir, "sub"));

        var paths = UsnRecordParser.BuildDirectoryPaths(dirs, rootFrn: seq | 5, rootPath: @"D:\data");

        Assert.Equal(@"D:\data\sub", paths[100]);
    }

    [Fact]
    public void BuildDirectoryPaths_SubtreeRoot_DropsOutsideEntries()
    {
        var dirs = Dirs(
            new UsnEntry(100, 5, AttrDir, "Users"),      // außerhalb des Teilbaums
            new UsnEntry(200, 100, AttrDir, "Clemens"),  // außerhalb
            new UsnEntry(300, 200, AttrDir, "Documents"),
            new UsnEntry(400, 300, AttrDir, "Projekte"));

        // Root = FRN 300 ("Documents"): nur 300 + 400 gehören dazu.
        var paths = UsnRecordParser.BuildDirectoryPaths(dirs, 300, @"C:\Users\Clemens\Documents");

        Assert.Equal(2, paths.Count);
        Assert.Equal(@"C:\Users\Clemens\Documents", paths[300]);
        Assert.Equal(@"C:\Users\Clemens\Documents\Projekte", paths[400]);
    }

    [Fact]
    public void BuildDirectoryPaths_OrphanParent_IsDropped()
    {
        var dirs = Dirs(new UsnEntry(100, 999, AttrDir, "verwaist"));
        var paths = UsnRecordParser.BuildDirectoryPaths(dirs, 5, @"C:\");
        Assert.Single(paths); // nur die Wurzel selbst
        Assert.False(paths.ContainsKey(100));
    }

    [Fact]
    public void BuildDirectoryPaths_ParentCycle_TerminatesAndDrops()
    {
        var dirs = Dirs(
            new UsnEntry(100, 200, AttrDir, "a"),
            new UsnEntry(200, 100, AttrDir, "b"));

        var paths = UsnRecordParser.BuildDirectoryPaths(dirs, 5, @"C:\"); // darf nicht hängen

        Assert.Single(paths);
        Assert.False(paths.ContainsKey(100));
        Assert.False(paths.ContainsKey(200));
    }

    [Fact]
    public void BuildDirectoryPaths_VeryDeepChain_DoesNotOverflowStack()
    {
        // 50.000 verschachtelte Verzeichnisse: rekursive Auflösung würde den
        // Stack sprengen (nicht abfangbar) — die Implementierung muss iterativ sein.
        const int levels = 50_000;
        var entries = new UsnEntry[levels];
        for (ulong i = 0; i < levels; i++)
            entries[i] = new UsnEntry(100 + i, i == 0 ? 5 : 99 + i, AttrDir, "d");
        var dirs = Dirs(entries);

        var paths = UsnRecordParser.BuildDirectoryPaths(dirs, 5, @"C:\");

        Assert.Equal(levels + 1, paths.Count);
        Assert.Equal(@"C:\d\d", paths[101]);
    }

    [Fact]
    public void BuildDirectoryPaths_TrimsTrailingSeparatorFromRoot()
    {
        var dirs = Dirs(new UsnEntry(100, 5, AttrDir, "sub"));
        var paths = UsnRecordParser.BuildDirectoryPaths(dirs, 5, @"C:\Temp\");
        Assert.Equal(@"C:\Temp\sub", paths[100]);
    }
}
