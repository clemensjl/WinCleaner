using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using WinCleaner.SystemTools;

namespace WinCleaner.Core;

/// <summary>Eine per Schnellscan gefundene Datei (voller Pfad, Größe, letzte Änderung).</summary>
public readonly record struct FastFile(string Path, long Bytes, DateTime LastWriteTime);

/// <summary>
/// WizTree-artiger NTFS-Schnellscan (Roadmap M11) als Alternative zur
/// Directory.EnumerateFiles-Enumeration in <see cref="DiskAnalyzer"/>.
///
/// Technische Entscheidung (zwei Stufen):
/// 1. Verzeichnisstruktur über FSCTL_ENUM_USN_DATA (DeviceIoControl auf dem
///    Raw-Volume): liefert das komplette MFT-Inventar (Name, Eltern-FRN,
///    Attribute) in Sekunden über eine dokumentierte API. Braucht Adminrechte
///    und NTFS.
/// 2. Dateigrößen NICHT aus rohen $MFT-Records gelesen: Das erforderte das
///    Parsen von FILE-Records inkl. Update-Sequence-Fixups, Attribute-Lists und
///    non-residenten $DATA-Läufen — undokumentiert und fehleranfällig.
///    Stattdessen werden die per USN gefundenen Verzeichnisse FLACH und PARALLEL
///    mit FindFirstFileExW (FindExInfoBasic + FIND_FIRST_EX_LARGE_FETCH)
///    gelistet: exakte Größen und Zeitstempel aus einer dokumentierten API,
///    ohne rekursive Traversierungs-Abhängigkeit — der Verzeichnisbaum steht ja
///    schon fest. Das ist der im Task skizzierte "Fallback"-Weg für Größen,
///    bewusst als Hauptweg gewählt (robust > maximal schnell).
///
/// Reparse Points (Junctions/Symlinks) werden nie verfolgt: Verzeichnisse mit
/// Reparse-Attribut fliegen aus dem USN-Inventar, Datei-Einträge mit
/// Reparse-Attribut aus der Größenerfassung — identisch zur
/// AttributesToSkip-Semantik des DiskAnalyzer. Bewusste Abweichung: Der
/// Standard-Scan listet eine Junction direkt unter der Wurzel noch als eigenen
/// Eintrag (und misst dabei ihr Ziel); der Schnellscan lässt sie ganz weg —
/// das entspricht der Junction-Regel des Projekts strenger. Ist die Scan-Wurzel
/// selbst ein Reparse Point, greift der Fallback (siehe IsSupported).
///
/// Fallback: Ohne Adminrechte, auf Nicht-NTFS-Volumes oder bei Fehlern liefern
/// die Try*-Methoden null (Meldung auf stderr); der Aufrufer nutzt dann den
/// normalen DiskAnalyzer-Pfad. Es wird bewusst KEIN UAC-Relaunch ausgelöst —
/// ein Scan soll nie einen Elevation-Prompt erzwingen.
/// </summary>
public sealed class NtfsFastScanner
{
    private readonly Logger _logger;

    public NtfsFastScanner(Logger logger) => _logger = logger;

    /// <summary>
    /// Prüft, ob der Schnellscan für diesen Pfad möglich ist (Windows, lokales
    /// NTFS-Laufwerk, Adminrechte). <paramref name="reason"/> nennt sonst den Grund.
    /// </summary>
    public static bool IsSupported(string rootPath, out string reason)
    {
        reason = "";
        if (!OperatingSystem.IsWindows())
        {
            reason = "kein Windows";
            return false;
        }

        string full;
        try { full = Path.GetFullPath(rootPath); }
        catch { reason = $"ungültiger Pfad: {rootPath}"; return false; }

        if (!Directory.Exists(full))
        {
            reason = $"Pfad nicht gefunden: {rootPath}";
            return false;
        }

        // Ist die Scan-Wurzel selbst eine Junction/ein Symlink, könnte das Ziel auf
        // einem anderen Volume liegen — die FRN wäre dann im falschen MFT-Inventar
        // verankert (stille Falschmessung). Solche Wurzeln dem Standard-Scan überlassen.
        try
        {
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            {
                reason = "Scan-Wurzel ist ein Reparse Point (Junction/Symlink)";
                return false;
            }
        }
        catch
        {
            reason = $"Attribute nicht lesbar: {rootPath}";
            return false;
        }

        var driveRoot = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(driveRoot) || driveRoot.StartsWith(@"\\", StringComparison.Ordinal))
        {
            reason = "kein lokales Laufwerk (UNC-Pfade werden nicht unterstützt)";
            return false;
        }

        string format;
        try { format = new DriveInfo(driveRoot).DriveFormat; }
        catch { reason = $"Laufwerk {driveRoot} nicht lesbar"; return false; }

        if (!string.Equals(format, "NTFS", StringComparison.OrdinalIgnoreCase))
        {
            reason = $"Dateisystem {format} statt NTFS";
            return false;
        }

        if (!Elevation.IsAdministrator())
        {
            reason = "keine Adminrechte für Raw-Volume-Zugriff";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Top-Level-Analyse wie <see cref="DiskAnalyzer.Analyze"/>, aber über den
    /// Schnellscan. Null, wenn der Schnellscan nicht möglich ist (dann Fallback).
    /// </summary>
    public DiskAnalysis? TryAnalyze(string rootPath, int topN = 25, DiskFilter? filter = null, int depth = 1)
    {
        var files = TryScan(rootPath, out var fullRoot);
        if (files is null) return null;

        var analysis = Aggregate(files, fullRoot!, topN, filter, depth);
        RebaseEntryPaths(analysis, fullRoot!, rootPath);
        _logger.Info($"NTFS-Schnellscan fertig: {files.Count} Dateien, " +
                     $"gesamt {DiskAnalyzer.FormatSize(analysis.TotalBytes)}.");
        return analysis;
    }

    /// <summary>
    /// Endungs-Gruppierung wie <see cref="DiskAnalyzer.AnalyzeByExtension"/>, aber
    /// über den Schnellscan. Null, wenn der Schnellscan nicht möglich ist.
    /// </summary>
    public ExtensionAnalysis? TryAnalyzeByExtension(string rootPath, int topN = 25, DiskFilter? filter = null)
    {
        var files = TryScan(rootPath, out _);
        if (files is null) return null;

        var analysis = AggregateByExtension(files, topN, filter);
        _logger.Info($"NTFS-Schnellscan (nach Endung) fertig: {files.Count} Dateien, " +
                     $"gesamt {DiskAnalyzer.FormatSize(analysis.TotalBytes)}.");
        return analysis;
    }

    // ---- Aggregation (pure, testbar ohne Adminrechte) ----

    /// <summary>
    /// Schreibt die absoluten Scan-Pfade auf die Nutzereingabe zurück, damit die
    /// Ausgabe auch bei relativen Pfaden identisch zum Standard-Scan bleibt
    /// (DiskAnalyzer gibt Pfade in der Schreibweise der Eingabe aus).
    /// </summary>
    internal static void RebaseEntryPaths(DiskAnalysis analysis, string fullRoot, string userRoot)
    {
        var source = fullRoot.TrimEnd('\\', '/');
        var target = userRoot.TrimEnd('\\', '/');
        if (string.Equals(source, target, StringComparison.Ordinal)) return;

        for (int i = 0; i < analysis.Entries.Count; i++)
        {
            var e = analysis.Entries[i];
            analysis.Entries[i] = e with { Path = target + e.Path[source.Length..] };
        }
    }

    /// <summary>
    /// Aggregiert eine flache Dateiliste zur selben Struktur wie
    /// <see cref="DiskAnalyzer.Analyze"/>: Verzeichnis-Einträge der Ebenen
    /// 1..depth (rekursive Summen), Dateien direkt in der Wurzel, absteigend
    /// sortiert, Top-N; TotalBytes = überlappungsfreie Gesamtsumme.
    /// </summary>
    internal static DiskAnalysis Aggregate(IReadOnlyList<FastFile> files, string rootPath,
                                           int topN, DiskFilter? filter, int depth)
    {
        if (depth < 1) depth = 1;
        var root = rootPath.TrimEnd('\\', '/');
        var prefix = root + '\\';

        var analysis = new DiskAnalysis();
        var dirTotals = new Dictionary<string, (long Bytes, int Files)>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<DiskEntry>();
        long total = 0;

        foreach (var f in files)
        {
            if (!f.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (filter is not null && !filter.Matches(f.Path, f.Bytes, f.LastWriteTime)) continue;

            total += f.Bytes;

            int sep = f.Path.IndexOf('\\', prefix.Length);
            if (sep < 0)
            {
                // Datei direkt in der Wurzel -> eigener Eintrag.
                entries.Add(new DiskEntry(IsDir: false, f.Path, f.Bytes, 1));
                continue;
            }

            // Datei zählt zu jedem Vorfahr-Verzeichnis bis zur Aggregationstiefe.
            // Bewusst ohne Split(): pro Datei nur <= depth Teil-Strings allokieren
            // (Hot-Loop bei Millionen Dateien).
            int level = 0;
            while (sep >= 0 && level < depth)
            {
                var key = f.Path[prefix.Length..sep];
                var cur = dirTotals.TryGetValue(key, out var v) ? v : (0L, 0);
                dirTotals[key] = (cur.Item1 + f.Bytes, cur.Item2 + 1);
                level++;
                sep = f.Path.IndexOf('\\', sep + 1);
            }
        }

        foreach (var kv in dirTotals)
            entries.Add(new DiskEntry(IsDir: true, prefix + kv.Key, kv.Value.Bytes, kv.Value.Files));

        analysis.TotalBytes = total;
        analysis.Entries.AddRange(entries.OrderByDescending(e => e.Bytes).Take(topN));
        return analysis;
    }

    /// <summary>
    /// Gruppiert eine flache Dateiliste nach Endung — selbe Struktur wie
    /// <see cref="DiskAnalyzer.AnalyzeByExtension"/>.
    /// </summary>
    internal static ExtensionAnalysis AggregateByExtension(IReadOnlyList<FastFile> files,
                                                           int topN, DiskFilter? filter)
    {
        var analysis = new ExtensionAnalysis();
        var byExt = new Dictionary<string, (long Bytes, int Files)>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in files)
        {
            if (filter is not null && !filter.Matches(f.Path, f.Bytes, f.LastWriteTime)) continue;

            var ext = Path.GetExtension(f.Path).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext)) ext = DiskAnalyzer.NoExtensionLabel;

            var cur = byExt.TryGetValue(ext, out var v) ? v : (0L, 0);
            byExt[ext] = (cur.Item1 + f.Bytes, cur.Item2 + 1);
        }

        analysis.TotalBytes = byExt.Values.Sum(v => v.Bytes);
        analysis.Entries.AddRange(
            byExt.Select(kv => new ExtensionEntry(kv.Key, kv.Value.Bytes, kv.Value.Files))
                 .OrderByDescending(e => e.Bytes)
                 .Take(topN));
        return analysis;
    }

    // ---- Scan (I/O-Seite, braucht Adminrechte) ----

    /// <summary>
    /// Führt den Schnellscan aus. Null bei fehlender Unterstützung oder Fehlern
    /// (Meldung auf stderr) — der Aufrufer fällt dann auf DiskAnalyzer zurück.
    /// </summary>
    private List<FastFile>? TryScan(string rootPath, out string? fullRoot)
    {
        fullRoot = null;

        if (!IsSupported(rootPath, out var reason))
        {
            _logger.Info($"NTFS-Schnellscan nicht möglich ({reason}) – nutze Standard-Scan.");
            return null;
        }

        try
        {
            fullRoot = Path.GetFullPath(rootPath).TrimEnd('\\', '/');
            if (fullRoot.Length == 2 && fullRoot[1] == ':')
            {
                // Volume-Wurzel ("C:") braucht den Trenner für CreateFile/DriveInfo,
                // der Pfadaufbau trimmt selbst.
                fullRoot += '\\';
            }
            return ScanCore(fullRoot);
        }
        catch (Exception ex)
        {
            _logger.Error($"NTFS-Schnellscan fehlgeschlagen: {ex.Message} – nutze Standard-Scan.");
            return null;
        }
    }

    private List<FastFile> ScanCore(string root)
    {
        var driveRoot = Path.GetPathRoot(root)!; // z.B. "C:\"

        using var volume = NativeMethods.OpenVolume(driveRoot);
        var rootFrn = NativeMethods.GetFileReferenceNumber(root);

        // 1. Komplettes Verzeichnis-Inventar des Volumes aus der MFT.
        var dirs = EnumerateDirectories(volume);

        // 2. Pfade der Verzeichnisse unterhalb der Scan-Wurzel auflösen.
        var paths = UsnRecordParser.BuildDirectoryPaths(dirs, rootFrn, root);
        _logger.Debug($"USN-Inventar: {dirs.Count} Verzeichnisse auf {driveRoot}, " +
                      $"{paths.Count} unterhalb von {root}.");

        // 3. Größen: jedes Verzeichnis flach und parallel listen.
        var result = new List<FastFile>(1 << 16);
        var gate = new object();
        Parallel.ForEach(
            paths.Values,
            () => new List<FastFile>(256),
            (dir, _, local) => { NativeMethods.ListFiles(dir, local); return local; },
            local => { lock (gate) result.AddRange(local); });

        return result;
    }

    /// <summary>Liest das MFT-Inventar per FSCTL_ENUM_USN_DATA (nur Verzeichnisse, ohne Reparse Points).</summary>
    private static Dictionary<ulong, UsnEntry> EnumerateDirectories(SafeFileHandle volume)
    {
        var dirs = new Dictionary<ulong, UsnEntry>(1 << 16);
        var buffer = new byte[1024 * 1024];
        var entries = new List<UsnEntry>(4096);

        var med = new NativeMethods.MFT_ENUM_DATA_V1
        {
            StartFileReferenceNumber = 0,
            LowUsn = 0,
            HighUsn = long.MaxValue,
            MinMajorVersion = 2, // V1-Struct nötig ab Win8, Records als USN_RECORD_V2
            MaxMajorVersion = 2
        };

        while (true)
        {
            if (!NativeMethods.DeviceIoControl(
                    volume, NativeMethods.FSCTL_ENUM_USN_DATA,
                    ref med, Marshal.SizeOf<NativeMethods.MFT_ENUM_DATA_V1>(),
                    buffer, buffer.Length, out int returned, IntPtr.Zero))
            {
                int err = Marshal.GetLastWin32Error();
                if (err == NativeMethods.ERROR_HANDLE_EOF) break; // MFT komplett gelesen
                throw new Win32Exception(err, $"FSCTL_ENUM_USN_DATA fehlgeschlagen (Win32-Fehler {err})");
            }

            if (returned < 8) break; // nicht einmal die Fortsetzungs-FRN geliefert

            entries.Clear();
            var next = UsnRecordParser.ParseEnumBuffer(buffer, returned, entries);

            foreach (var e in entries)
            {
                if (e.IsDirectory && !e.IsReparsePoint)
                    dirs[e.Frn & UsnRecordParser.FrnIndexMask] = e;
            }

            // Auch ein leerer Batch (nur Fortsetzungs-FRN) wird fortgesetzt, solange
            // die FRN vorankommt — das Ende signalisiert nur ERROR_HANDLE_EOF.
            // Ohne Fortschritt abbrechen (Schutz vor Endlosschleife).
            if (next <= med.StartFileReferenceNumber) break;
            med.StartFileReferenceNumber = next;
        }

        return dirs;
    }

    // ---- P/Invoke ----

    private static class NativeMethods
    {
        internal const uint FSCTL_ENUM_USN_DATA = 0x000900B3;
        internal const int ERROR_HANDLE_EOF = 38;

        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_SHARE_ALL = 0x7; // READ | WRITE | DELETE
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

        private const uint FILE_ATTRIBUTE_DIRECTORY = UsnEntry.FileAttributeDirectory;
        private const uint FILE_ATTRIBUTE_REPARSE_POINT = UsnEntry.FileAttributeReparsePoint;

        private const int FIND_FIRST_EX_LARGE_FETCH = 2;
        private const int FindExInfoBasic = 1;      // FINDEX_INFO_LEVELS
        private const int FindExSearchNameMatch = 0; // FINDEX_SEARCH_OPS
        private static readonly IntPtr InvalidHandleValue = new(-1);

        [StructLayout(LayoutKind.Sequential)]
        internal struct MFT_ENUM_DATA_V1
        {
            public ulong StartFileReferenceNumber;
            public long LowUsn;
            public long HighUsn;
            public ushort MinMajorVersion;
            public ushort MaxMajorVersion;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BY_HANDLE_FILE_INFORMATION
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WIN32_FIND_DATAW
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
            uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool DeviceIoControl(
            SafeFileHandle hDevice, uint dwIoControlCode,
            ref MFT_ENUM_DATA_V1 lpInBuffer, int nInBufferSize,
            byte[] lpOutBuffer, int nOutBufferSize,
            out int lpBytesReturned, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(
            SafeFileHandle hFile, out BY_HANDLE_FILE_INFORMATION lpFileInformation);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindFirstFileExW(
            string lpFileName, int fInfoLevelId, out WIN32_FIND_DATAW lpFindFileData,
            int fSearchOp, IntPtr lpSearchFilter, int dwAdditionalFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool FindNextFileW(IntPtr hFindFile, out WIN32_FIND_DATAW lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FindClose(IntPtr hFindFile);

        /// <summary>Öffnet das Raw-Volume (z.B. \\.\C:) lesend — braucht Adminrechte.</summary>
        internal static SafeFileHandle OpenVolume(string driveRoot)
        {
            var volumePath = @"\\.\" + driveRoot.TrimEnd('\\');
            var handle = CreateFileW(volumePath, GENERIC_READ, FILE_SHARE_ALL, IntPtr.Zero,
                                     OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    $"Volume {volumePath} konnte nicht geöffnet werden");
            return handle;
        }

        /// <summary>Ermittelt die FRN eines Verzeichnisses (FileIndexHigh/Low).</summary>
        internal static ulong GetFileReferenceNumber(string directoryPath)
        {
            var path = directoryPath.TrimEnd('\\');
            if (path.Length == 2 && path[1] == ':')
                path += '\\'; // "\\?\C:" wäre das Volume-Device, nicht das Wurzelverzeichnis

            using var handle = CreateFileW(@"\\?\" + path, 0, FILE_SHARE_ALL,
                                           IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
            if (handle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    $"Verzeichnis {directoryPath} konnte nicht geöffnet werden");
            if (!GetFileInformationByHandle(handle, out var info))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    $"GetFileInformationByHandle fehlgeschlagen für {directoryPath}");
            return ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow;
        }

        /// <summary>
        /// Listet die Dateien EINES Verzeichnisses (nicht rekursiv) mit
        /// FIND_FIRST_EX_LARGE_FETCH. Unterverzeichnisse und Reparse Points werden
        /// übersprungen; unlesbare Verzeichnisse still ignoriert (wie
        /// IgnoreInaccessible im DiskAnalyzer).
        /// </summary>
        internal static void ListFiles(string directory, List<FastFile> into)
        {
            // \\?\-Präfix: Pfade aus dem USN-Baum können MAX_PATH überschreiten.
            var handle = FindFirstFileExW(@"\\?\" + directory + @"\*",
                FindExInfoBasic, out var data, FindExSearchNameMatch,
                IntPtr.Zero, FIND_FIRST_EX_LARGE_FETCH);
            if (handle == InvalidHandleValue) return;

            try
            {
                do
                {
                    if ((data.dwFileAttributes &
                         (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) != 0)
                        continue; // Verzeichnisse (inkl. "."/"..") kommen aus dem USN-Baum

                    long size = ((long)data.nFileSizeHigh << 32) | data.nFileSizeLow;
                    long fileTime = ((long)data.ftLastWriteTime.dwHighDateTime << 32) |
                                    (uint)data.ftLastWriteTime.dwLowDateTime;

                    DateTime lastWrite;
                    try { lastWrite = DateTime.FromFileTime(fileTime); }
                    catch (ArgumentOutOfRangeException) { lastWrite = DateTime.MinValue; }

                    into.Add(new FastFile(directory + '\\' + data.cFileName, size, lastWrite));
                }
                while (FindNextFileW(handle, out data));
            }
            finally
            {
                FindClose(handle);
            }
        }
    }
}
