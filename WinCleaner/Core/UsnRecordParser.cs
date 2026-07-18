using System.Buffers.Binary;
using System.Text;

namespace WinCleaner.Core;

/// <summary>
/// Ein Eintrag aus dem MFT-Inventar (USN_RECORD_V2): Datei-Referenznummer (FRN),
/// Eltern-FRN, Dateiattribute und Name. Die oberen 16 Bit einer FRN sind eine
/// Sequence-Nummer; für Baum-Verknüpfungen zählt nur der 48-Bit-Index
/// (<see cref="UsnRecordParser.FrnIndexMask"/>).
/// </summary>
public sealed record UsnEntry(ulong Frn, ulong ParentFrn, uint Attributes, string Name)
{
    public const uint FileAttributeDirectory    = 0x10;
    public const uint FileAttributeReparsePoint = 0x400;

    public bool IsDirectory    => (Attributes & FileAttributeDirectory) != 0;
    public bool IsReparsePoint => (Attributes & FileAttributeReparsePoint) != 0;
}

/// <summary>
/// Pure Parsing-Logik für die Ausgabepuffer von FSCTL_ENUM_USN_DATA sowie der
/// Pfadaufbau aus FRN-Ketten. Bewusst frei von P/Invoke und Dateisystem-Zugriffen,
/// damit sie mit synthetischen byte[]-Puffern ohne Adminrechte testbar ist
/// (siehe UsnRecordParserTests). Die I/O-Seite liegt in <see cref="NtfsFastScanner"/>.
/// </summary>
public static class UsnRecordParser
{
    /// <summary>Maskiert die Sequence-Nummer (obere 16 Bit) einer FRN weg.</summary>
    public const ulong FrnIndexMask = 0x0000_FFFF_FFFF_FFFF;

    /// <summary>Fixer Kopfteil eines USN_RECORD_V2 vor dem Dateinamen.</summary>
    private const int RecordHeaderSize = 60;

    /// <summary>
    /// Parst einen FSCTL_ENUM_USN_DATA-Ausgabepuffer: die ersten 8 Bytes sind die
    /// nächste Start-FRN (Rückgabewert), danach folgen USN_RECORD_V2-Einträge.
    /// Defekte, abgeschnittene oder unbekannte Records werden übersprungen bzw.
    /// beenden das Parsen — es wird nie über <paramref name="validLength"/> gelesen.
    /// </summary>
    /// <param name="buffer">Roher Ausgabepuffer des DeviceIoControl-Aufrufs.</param>
    /// <param name="validLength">Anzahl gültiger Bytes (lpBytesReturned).</param>
    /// <param name="into">Zielliste für geparste Einträge.</param>
    /// <returns>Nächste Start-FRN für den Folgeaufruf (0 bei leerem Puffer).</returns>
    public static ulong ParseEnumBuffer(ReadOnlySpan<byte> buffer, int validLength, List<UsnEntry> into)
    {
        if (validLength > buffer.Length) validLength = buffer.Length;
        if (validLength < sizeof(ulong)) return 0;

        ulong nextFrn = BinaryPrimitives.ReadUInt64LittleEndian(buffer);
        int offset = sizeof(ulong);

        while (offset + RecordHeaderSize <= validLength)
        {
            var rec = buffer[offset..validLength];
            int recordLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(rec);

            // RecordLength 0 oder zu klein => defekter Puffer; über das Ende
            // hinausragende Records sind abgeschnitten. In beiden Fällen stoppen.
            if (recordLength < RecordHeaderSize || recordLength > rec.Length)
                break;

            ushort major = BinaryPrimitives.ReadUInt16LittleEndian(rec[4..]);
            if (major == 2)
            {
                ulong frn      = BinaryPrimitives.ReadUInt64LittleEndian(rec[8..]);
                ulong parent   = BinaryPrimitives.ReadUInt64LittleEndian(rec[16..]);
                uint attrs     = BinaryPrimitives.ReadUInt32LittleEndian(rec[52..]);
                ushort nameLen = BinaryPrimitives.ReadUInt16LittleEndian(rec[56..]);
                ushort nameOff = BinaryPrimitives.ReadUInt16LittleEndian(rec[58..]);

                if (nameOff >= RecordHeaderSize && nameOff + nameLen <= recordLength)
                {
                    var name = Encoding.Unicode.GetString(rec.Slice(nameOff, nameLen));
                    into.Add(new UsnEntry(frn, parent, attrs, name));
                }
            }
            // Andere Versionen (ENUM_USN_DATA liefert auf NTFS nur V2) überspringen.

            offset += recordLength;
        }

        return nextFrn;
    }

    /// <summary>
    /// Baut aus dem FRN-Eltern-Graphen die vollständigen Pfade aller Verzeichnisse,
    /// die unterhalb von <paramref name="rootFrn"/> liegen (inklusive der Wurzel
    /// selbst). Einträge außerhalb des Teilbaums, Waisen (Eltern nicht im Inventar)
    /// und Zyklen werden verworfen. Sequence-Nummern werden maskiert.
    /// </summary>
    /// <param name="dirsByFrn">Verzeichnis-Inventar, Schlüssel = FRN (maskiert oder roh).</param>
    /// <param name="rootFrn">FRN des Wurzelverzeichnisses des Scans.</param>
    /// <param name="rootPath">Pfad der Wurzel; nachgestellte Trenner werden entfernt.</param>
    public static Dictionary<ulong, string> BuildDirectoryPaths(
        IReadOnlyDictionary<ulong, UsnEntry> dirsByFrn, ulong rootFrn, string rootPath)
    {
        // Schlüssel defensiv auf den 48-Bit-Index normalisieren.
        var byIndex = new Dictionary<ulong, UsnEntry>(dirsByFrn.Count);
        foreach (var kv in dirsByFrn)
            byIndex[kv.Key & FrnIndexMask] = kv.Value;

        rootFrn &= FrnIndexMask;
        var result = new Dictionary<ulong, string>
        {
            [rootFrn] = rootPath.TrimEnd('\\', '/')
        };
        var dead = new HashSet<ulong>();   // bekannt unerreichbar (Waisen, Zyklen, außerhalb)
        var onPath = new HashSet<ulong>(); // Zyklus-Erkennung während einer Auflösung
        var chain = new List<ulong>();     // wiederverwendeter Ketten-Puffer

        // Iterativ statt rekursiv: beliebig tiefe Verzeichnisketten dürfen den
        // Stack nicht sprengen (StackOverflowException wäre nicht abfangbar).
        foreach (var start in byIndex.Keys)
        {
            chain.Clear();
            var frn = start;
            string? basePath = null;

            // Aufwärts laufen, bis ein bekannter Pfad, eine Waise oder ein Zyklus kommt.
            while (true)
            {
                if (result.TryGetValue(frn, out var known)) { basePath = known; break; }
                if (dead.Contains(frn) || !byIndex.TryGetValue(frn, out var entry) || !onPath.Add(frn))
                    break;
                chain.Add(frn);
                frn = entry.ParentFrn & FrnIndexMask;
            }

            if (basePath is null)
            {
                // Kette hängt an einer Waise oder einem Zyklus -> komplett verwerfen.
                foreach (var f in chain) { dead.Add(f); onPath.Remove(f); }
                continue;
            }

            // Abwärts (wurzelnah zuerst) die Pfade zuweisen.
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                basePath = basePath + '\\' + byIndex[chain[i]].Name;
                result[chain[i]] = basePath;
                onPath.Remove(chain[i]);
            }
        }

        return result;
    }
}
