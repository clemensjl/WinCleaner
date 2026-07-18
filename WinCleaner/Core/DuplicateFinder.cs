using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using WinCleaner.Util; // stiller Papierkorb-Löschvorgang (RecycleBinHelper)

namespace WinCleaner.Core;

public record DuplicateGroup(string Hash, List<string> Files, long TotalBytes);

/// <summary>
/// Strategie, welche Datei je Duplikatgruppe BEHALTEN wird. Die behaltene
/// Datei wird niemals gelöscht oder durch einen Hardlink ersetzt.
/// </summary>
public enum KeepStrategy
{
    /// <summary>Erste Datei der Gruppe (bisheriges Standardverhalten).</summary>
    First,
    /// <summary>Älteste Datei (kleinste Schreibzeit).</summary>
    Oldest,
    /// <summary>Neueste Datei (größte Schreibzeit).</summary>
    Newest,
    /// <summary>Datei mit dem kürzesten vollständigen Pfad.</summary>
    ShortestPath,
    /// <summary>Datei mit dem längsten vollständigen Pfad.</summary>
    LongestPath
}

/// <summary>
/// Eine einzelne (geplante oder ausgeführte) Aktion an einer Datei – Bestandteil
/// der <c>--json</c>-Ausgabe: Gruppen-Hash, behaltene Datei, betroffene Datei,
/// Aktions-Code (siehe <c>Act*</c>-Konstanten in <see cref="DuplicateFinder"/>)
/// und die dadurch eingesparten Bytes (0 bei Skip/Fehler).
/// </summary>
public sealed record DuplicateFileAction(string Hash, string Keep, string File, string Action, long Bytes);

/// <summary>
/// Ergebnis einer Lösch-/Hardlink-Aktion über mehrere Gruppen, für JSON-Ausgabe
/// und Zusammenfassung. Rein informativ – im Dry-Run beschreibt es die geplante
/// Aktion.
/// </summary>
public sealed record DuplicateActionResult(
    int GroupsProcessed,
    int GroupsSkipped,
    int FilesAffected,
    int FilesSkipped,
    long BytesAffected,
    bool DryRun,
    bool HardLink,
    bool SentToRecycleBin,
    IReadOnlyList<DuplicateFileAction> Actions);

/// <summary>
/// Findet inhaltsgleiche Dateien. Statt jede Datei voll zu hashen, filtert ein
/// dreistufiges Verfahren Kandidaten heraus: erst nach Dateigröße gruppieren,
/// dann ein günstiger Partial-Hash (erste 4 KB), und nur bei verbleibenden
/// Kollisionen der volle SHA-256-Hash. Das spart bei großen Bäumen massiv I/O.
/// </summary>
public class DuplicateFinder
{
    private const int PartialBytes = 4096;

    /// <summary>NTFS erlaubt maximal 1024 Hardlinks pro Datei.</summary>
    public const int MaxHardLinksPerFile = 1024;

    // Aktions-Codes für DuplicateFileAction.Action (stabil, für --json).
    public const string ActHardLink          = "hardlink";
    public const string ActDelete            = "delete";
    public const string ActPlanHardLink      = "plan-hardlink";
    public const string ActPlanDelete        = "plan-delete";
    public const string ActFailed            = "failed";
    public const string ActSkipOtherVolume   = "skip-other-volume";
    public const string ActSkipNotNtfs       = "skip-not-ntfs";
    public const string ActSkipReparsePoint  = "skip-reparse-point";
    public const string ActSkipAlreadyLinked = "skip-already-linked";
    public const string ActSkipLinkLimit     = "skip-link-limit";

    private static readonly EnumerationOptions DeepOpts = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible    = true,
        AttributesToSkip      = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false
    };

    private readonly Logger _logger;
    public DuplicateFinder(Logger logger) => _logger = logger;

    /// <param name="rootPath">Wurzelverzeichnis der Suche.</param>
    /// <param name="cache">
    /// Optionaler persistenter Hash-Cache: volle SHA-256-Hashes werden
    /// wiederverwendet, solange Größe und Schreibzeit der Datei unverändert
    /// sind. null = jedes Mal frisch hashen (bisheriges Verhalten).
    /// </param>
    public List<DuplicateGroup> Find(string rootPath, HashCache? cache = null)
    {
        if (!Directory.Exists(rootPath))
        {
            _logger.Error($"Pfad nicht gefunden: {rootPath}");
            return new();
        }

        // Stufe 1: nach Größe gruppieren (leere Dateien überspringen).
        var bySize = new Dictionary<long, List<string>>();
        foreach (var file in Directory.EnumerateFiles(rootPath, "*", DeepOpts))
        {
            // Eigene Hardlink-Sicherungen/Temp-Links NICHT erfassen, sonst
            // tauchen sie in Folgeläufen als neue Duplikate auf.
            if (file.EndsWith(".wcbak", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".wclnk", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                long size = new FileInfo(file).Length;
                if (size == 0) continue;
                (bySize.TryGetValue(size, out var list) ? list : bySize[size] = new()).Add(file);
            }
            catch { /* gesperrt/weg */ }
        }

        var result = new List<DuplicateGroup>();
        int fullHashes = 0;

        foreach (var (size, sameSize) in bySize)
        {
            if (sameSize.Count < 2) continue; // eindeutige Größe -> kein Duplikat

            // Stufe 2: Partial-Hash (erste 4 KB).
            foreach (var partialGroup in GroupByHash(sameSize, f => Hash(f, PartialBytes)))
            {
                if (partialGroup.Count < 2) continue;

                // Stufe 3: voller Hash nur für echte Kandidaten (ggf. aus dem Cache).
                foreach (var (hash, fullGroup) in GroupByHashKeyed(partialGroup, f => FullHash(f, cache)))
                {
                    fullHashes += fullGroup.Count;
                    if (fullGroup.Count < 2) continue;

                    result.Add(new DuplicateGroup(hash, fullGroup, size * fullGroup.Count));
                }
            }
        }

        result.Sort((a, b) => b.TotalBytes.CompareTo(a.TotalBytes));
        string cacheNote = cache is not null ? $", davon {cache.Hits} aus dem Hash-Cache" : "";
        _logger.Info($"Duplikatsuche fertig: {result.Count} Gruppen, {fullHashes} volle Hashes{cacheNote}.");
        return result;
    }

    /// <summary>
    /// Voller SHA-256-Hash einer Datei, bei aktivem Cache mit Wiederverwendung:
    /// Treffer nur, wenn Größe und Schreibzeit unverändert sind.
    /// </summary>
    private static string FullHash(string path, HashCache? cache)
    {
        if (cache is null) return Hash(path, -1);

        var info = new FileInfo(path);
        if (cache.TryGet(path, info.Length, info.LastWriteTimeUtc, out var cached))
            return cached;

        var h = Hash(path, -1);
        cache.Set(path, info.Length, info.LastWriteTimeUtc, h);
        return h;
    }

    /// <summary>
    /// Löscht je Gruppe alle Dateien außer der ersten. Standardmäßig in den
    /// Papierkorb (umkehrbar) – konsistent mit <see cref="JunkCleaner"/>; nur für
    /// Tests kann permanent gelöscht werden.
    /// </summary>
    /// <remarks>
    /// Diese Überladung bleibt aus Kompatibilitätsgründen (Tests) erhalten und
    /// verhält sich exakt wie bisher: behält die ERSTE Datei jeder Gruppe, ohne
    /// Schutzpfade und ohne Hardlinks.
    /// </remarks>
    public void DeleteDuplicates(List<DuplicateGroup> groups, bool sendToRecycleBin = true)
    {
        int deleted = 0;
        foreach (var g in groups)
        {
            // Erste Datei behalten, Rest löschen.
            foreach (var f in g.Files.Skip(1))
            {
                try
                {
                    RecycleBinHelper.DeleteFile(f, sendToRecycleBin);
                    deleted++;
                }
                catch (Exception ex) { _logger.Debug($"Löschen fehlgeschlagen {f}: {ex.Message}"); }
            }
        }
        _logger.Info(sendToRecycleBin
            ? $"{deleted} Duplikate in den Papierkorb verschoben (je Gruppe eine Datei behalten)."
            : $"{deleted} Duplikate gelöscht (je Gruppe eine Datei behalten).");
    }

    /// <summary>
    /// Erweiterte Duplikat-Bereinigung mit wählbarer Behalte-Strategie,
    /// Schutzpfaden und optionalem Hardlink statt Löschung.
    /// </summary>
    /// <param name="groups">Gefundene Duplikatgruppen.</param>
    /// <param name="keep">Welche Datei je Gruppe behalten wird.</param>
    /// <param name="protectedPaths">
    /// Wurzelpfade; Dateien darunter werden NIE gelöscht/ersetzt. Sind ALLE
    /// Dateien einer Gruppe geschützt, wird die Gruppe komplett übersprungen.
    /// </param>
    /// <param name="hardLink">
    /// true => Duplikate werden durch einen NTFS-Hardlink auf die behaltene Datei
    /// ersetzt (spart Platz, kein Datenverlust). Nur auf demselben Volume möglich,
    /// sonst wird die Datei übersprungen. false => normales Löschen.
    /// </param>
    /// <param name="sendToRecycleBin">Beim Löschen Papierkorb (true) statt permanent.</param>
    /// <param name="dryRun">
    /// true (Default) => es wird NICHTS verändert, nur die geplante Aktion ermittelt
    /// und protokolliert.
    /// </param>
    public DuplicateActionResult ProcessDuplicates(
        List<DuplicateGroup> groups,
        KeepStrategy keep = KeepStrategy.First,
        IReadOnlyCollection<string>? protectedPaths = null,
        bool hardLink = false,
        bool sendToRecycleBin = true,
        bool dryRun = true)
    {
        var normProtected = NormalizeProtected(protectedPaths);

        int groupsProcessed = 0, groupsSkipped = 0, filesAffected = 0, filesSkipped = 0;
        long bytesAffected = 0;
        var actions = new List<DuplicateFileAction>();

        foreach (var g in groups)
        {
            if (g.Files.Count < 2) { groupsSkipped++; continue; }

            // Sind alle Dateien geschützt -> Gruppe überspringen.
            if (g.Files.All(f => IsProtected(f, normProtected)))
            {
                _logger.Debug($"Gruppe übersprungen (alle Dateien geschützt): {g.Hash}");
                groupsSkipped++;
                continue;
            }

            string keepFile = SelectKeepFile(g.Files, keep, normProtected);
            long perFileBytes = g.Files.Count > 0 ? g.TotalBytes / g.Files.Count : 0;

            // Kandidaten = alle außer der behaltenen und außer geschützten Dateien.
            var candidates = g.Files
                .Where(f => !PathEquals(f, keepFile) && !IsProtected(f, normProtected))
                .ToList();

            if (candidates.Count == 0) { groupsSkipped++; continue; }

            bool anyAffected = false;
            foreach (var f in candidates)
            {
                // Guards gelten auch im Probelauf, damit die gemeldete Ersparnis
                // der echten Ausführung entspricht (z. B. keine Schein-Ersparnis
                // für bereits verlinkte Paare).
                if (hardLink && HardLinkBlocker(keepFile, f) is { } blocked)
                {
                    _logger.Info($"Hardlink übersprungen ({BlockerText(blocked)}): {f}");
                    actions.Add(new DuplicateFileAction(g.Hash, keepFile, f, blocked, 0));
                    filesSkipped++;
                    continue;
                }

                if (dryRun)
                {
                    _logger.Info(hardLink
                        ? $"[Probelauf] Würde durch Hardlink auf '{keepFile}' ersetzen: {f}"
                        : $"[Probelauf] Würde {(sendToRecycleBin ? "in den Papierkorb verschieben" : "permanent löschen")}: {f}");
                    actions.Add(new DuplicateFileAction(g.Hash, keepFile, f,
                        hardLink ? ActPlanHardLink : ActPlanDelete, perFileBytes));
                    filesAffected++;
                    bytesAffected += perFileBytes;
                    anyAffected = true;
                    continue;
                }

                bool ok = hardLink
                    ? ReplaceWithHardLink(f, keepFile, sendToRecycleBin)
                    : DeleteOne(f, sendToRecycleBin);

                if (ok)
                {
                    actions.Add(new DuplicateFileAction(g.Hash, keepFile, f,
                        hardLink ? ActHardLink : ActDelete, perFileBytes));
                    filesAffected++;
                    bytesAffected += perFileBytes;
                    anyAffected = true;
                }
                else
                {
                    actions.Add(new DuplicateFileAction(g.Hash, keepFile, f, ActFailed, 0));
                    filesSkipped++;
                }
            }

            if (anyAffected) groupsProcessed++; else groupsSkipped++;
        }

        var verb = dryRun ? "geplant" : (hardLink ? "durch Hardlinks ersetzt"
            : sendToRecycleBin ? "in den Papierkorb verschoben" : "gelöscht");
        _logger.Info($"Duplikat-Aktion fertig: {filesAffected} Dateien {verb}, " +
                     $"{DiskAnalyzer.FormatSize(bytesAffected)}, {groupsProcessed} Gruppen bearbeitet, " +
                     $"{groupsSkipped} Gruppen und {filesSkipped} Dateien übersprungen.");

        return new DuplicateActionResult(
            GroupsProcessed: groupsProcessed,
            GroupsSkipped: groupsSkipped,
            FilesAffected: filesAffected,
            FilesSkipped: filesSkipped,
            BytesAffected: bytesAffected,
            DryRun: dryRun,
            HardLink: hardLink,
            SentToRecycleBin: sendToRecycleBin,
            Actions: actions);
    }

    /// <summary>Parst einen Strategie-Namen (z. B. "oldest") in <see cref="KeepStrategy"/>.</summary>
    public static KeepStrategy ParseKeepStrategy(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "first"   => KeepStrategy.First,
        "oldest"                => KeepStrategy.Oldest,
        "newest"                => KeepStrategy.Newest,
        "shortest-path"         => KeepStrategy.ShortestPath,
        "longest-path"          => KeepStrategy.LongestPath,
        _ => throw new ArgumentException(
            $"Unbekannte --keep-Strategie '{value}'. Erlaubt: oldest, newest, shortest-path, longest-path.")
    };

    // ---- Behalte-Auswahl ----

    /// <summary>
    /// Ermittelt die zu behaltende Datei einer Gruppe. Geschützte Dateien werden
    /// bei der Auswahl bevorzugt (sie dürfen ohnehin nicht gelöscht werden); gibt
    /// es geschützte Dateien, wird unter diesen die Strategie angewandt.
    /// </summary>
    private static string SelectKeepFile(List<string> files, KeepStrategy keep, List<string> normProtected)
    {
        // Bevorzugt eine geschützte Datei behalten, damit Kandidaten gefahrlos
        // entfernt werden können.
        var pool = files.Where(f => IsProtected(f, normProtected)).ToList();
        if (pool.Count == 0) pool = files;

        return keep switch
        {
            KeepStrategy.First        => pool[0],
            KeepStrategy.Oldest       => pool.OrderBy(p => SafeWriteTime(p, DateTime.MaxValue)).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).First(),
            KeepStrategy.Newest       => pool.OrderByDescending(p => SafeWriteTime(p, DateTime.MinValue)).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).First(),
            KeepStrategy.ShortestPath => pool.OrderBy(p => p.Length).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).First(),
            KeepStrategy.LongestPath  => pool.OrderByDescending(p => p.Length).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).First(),
            _                         => pool[0]
        };
    }

    /// <summary>
    /// Liest die Schreibzeit; bei unlesbarer Zeit wird <paramref name="fallback"/>
    /// geliefert. Der Aufrufer wählt den Fallback strategie-abhängig so, dass eine
    /// unlesbare/gesperrte Datei NIE gewinnt: bei Oldest (aufsteigend) MaxValue,
    /// bei Newest (absteigend) MinValue – beide landen am Ende der Sortierung.
    /// </summary>
    private static DateTime SafeWriteTime(string path, DateTime fallback)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return fallback; }
    }

    // ---- Schutzpfade ----

    private static List<string> NormalizeProtected(IReadOnlyCollection<string>? protectedPaths)
    {
        var list = new List<string>();
        if (protectedPaths is null) return list;
        foreach (var p in protectedPaths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            try { list.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(p.Trim()))); }
            catch { /* ungültiger Pfad -> ignorieren */ }
        }
        return list;
    }

    private static bool IsProtected(string file, List<string> normProtected)
    {
        if (normProtected.Count == 0) return false;
        string full;
        try { full = Path.GetFullPath(file); }
        catch { return false; }

        foreach (var root in normProtected)
        {
            // Exakte Datei oder unterhalb des Schutz-Wurzelpfads.
            if (full.Equals(root, StringComparison.OrdinalIgnoreCase)) return true;
            if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    // ---- Lösch-/Hardlink-Operationen ----

    private bool DeleteOne(string path, bool sendToRecycleBin)
    {
        try
        {
            RecycleBinHelper.DeleteFile(path, sendToRecycleBin);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Debug($"Löschen fehlgeschlagen {path}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Ersetzt <paramref name="duplicate"/> atomar durch einen Hardlink auf
    /// <paramref name="keepFile"/>: Link unter temporärem Namen im selben
    /// Verzeichnis anlegen, per <see cref="File.Replace(string,string,string,bool)"/>
    /// eintauschen (ReplaceFile-Swap), Original als Rückversicherung in den
    /// Papierkorb. Schlägt ein Schritt fehl, bleibt das Original unverändert
    /// (Rollback: nur der Temp-Link wird entfernt).
    /// </summary>
    private bool ReplaceWithHardLink(string duplicate, string keepFile, bool sendToRecycleBin)
    {
        string tempLink = duplicate + "." + Guid.NewGuid().ToString("N")[..8] + ".wclnk";
        string backup   = duplicate + ".wcbak";

        // 1) Hardlink unter Temp-Namen anlegen – berührt das Original nicht.
        if (!CreateHardLink(tempLink, keepFile, IntPtr.Zero))
        {
            var err = new Win32Exception(Marshal.GetLastWin32Error());
            _logger.Error($"Hardlink fehlgeschlagen für {duplicate}: {err.Message} – Datei bleibt unverändert.");
            return false;
        }

        // 2) Atomarer Tausch: Temp-Link -> Duplikat, Original -> Backup.
        try
        {
            File.Replace(tempLink, duplicate, backup, ignoreMetadataErrors: true);
        }
        catch (Exception ex)
        {
            _logger.Error($"Hardlink-Tausch fehlgeschlagen für {duplicate}: {ex.Message} – Original bleibt unverändert.");
            TryDelete(tempLink); // Rollback: nur den Temp-Link entfernen
            return false;
        }

        // 3) Rückversicherung: Original-Inhalt in den Papierkorb (Tests: permanent).
        try
        {
            RecycleBinHelper.DeleteFile(backup, sendToRecycleBin);
        }
        catch (Exception ex)
        {
            _logger.Info($"Sicherung konnte nicht entsorgt werden ({ex.Message}) – liegt weiter unter: {backup}");
        }

        _logger.Debug($"Hardlink angelegt: {duplicate} -> {keepFile}");
        return true;
    }

    // ---- Hardlink-Guards ----

    /// <summary>
    /// Prüft alle Voraussetzungen für eine Hardlink-Ersetzung. Liefert null,
    /// wenn nichts dagegen spricht, sonst den Aktions-Code des Hindernisses.
    /// </summary>
    private static string? HardLinkBlocker(string keepFile, string duplicate)
    {
        if (!SameVolume(keepFile, duplicate)) return ActSkipOtherVolume;
        if (!IsNtfs(keepFile)) return ActSkipNotNtfs;
        if (IsReparsePoint(keepFile) || IsReparsePoint(duplicate)) return ActSkipReparsePoint;
        if (AreHardLinked(keepFile, duplicate)) return ActSkipAlreadyLinked;
        if (GetHardLinkCount(keepFile) >= MaxHardLinksPerFile) return ActSkipLinkLimit;
        return null;
    }

    private static string BlockerText(string code) => code switch
    {
        ActSkipOtherVolume   => "anderes Volume",
        ActSkipNotNtfs       => "kein NTFS",
        ActSkipReparsePoint  => "Reparse Point",
        ActSkipAlreadyLinked => "schon verlinkt",
        ActSkipLinkLimit     => $"Hardlink-Limit ({MaxHardLinksPerFile} Links/Datei) erreicht",
        _                    => code
    };

    private static readonly Dictionary<string, bool> NtfsCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Prüft (mit Cache je Volume-Root), ob der Pfad auf NTFS liegt.</summary>
    private static bool IsNtfs(string path)
    {
        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(path)) ?? "";
            if (root.Length == 0) return false;
            lock (NtfsCache)
            {
                if (!NtfsCache.TryGetValue(root, out bool ntfs))
                    NtfsCache[root] = ntfs = string.Equals(
                        new DriveInfo(root).DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase);
                return ntfs;
            }
        }
        catch { return false; } // UNC/unbekannt -> konservativ überspringen
    }

    private static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch { return false; } // nicht lesbar -> der eigentliche Schritt meldet den Fehler
    }

    // ---- File-Identity (GetFileInformationByHandle) ----

    /// <summary>
    /// True, wenn beide Pfade auf denselben NTFS-Dateieintrag zeigen (gleiche
    /// Volume-Seriennummer + FileID), also bereits Hardlinks aufeinander sind.
    /// </summary>
    public static bool AreHardLinked(string a, string b)
        => TryGetFileIdentity(a, out uint va, out ulong ia, out _)
        && TryGetFileIdentity(b, out uint vb, out ulong ib, out _)
        && va == vb && ia == ib;

    /// <summary>Anzahl der Hardlinks der Datei; -1, wenn nicht ermittelbar.</summary>
    public static int GetHardLinkCount(string path)
        => TryGetFileIdentity(path, out _, out _, out int links) ? links : -1;

    private static bool TryGetFileIdentity(string path, out uint volume, out ulong fileId, out int links)
    {
        volume = 0; fileId = 0; links = -1;
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (!GetFileInformationByHandle(handle, out var info)) return false;
            volume = info.VolumeSerialNumber;
            fileId = ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow;
            links  = (int)info.NumberOfLinks;
            return true;
        }
        catch { return false; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle hFile, out ByHandleFileInformation lpFileInformation);

    private void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { _logger.Debug($"Aufräumen fehlgeschlagen {path}: {ex.Message}"); }
    }

    /// <summary>Prüft, ob zwei Pfade auf demselben Volume liegen (Hardlink-Voraussetzung).</summary>
    private static bool SameVolume(string a, string b)
    {
        try
        {
            string ra = Path.GetPathRoot(Path.GetFullPath(a)) ?? "";
            string rb = Path.GetPathRoot(Path.GetFullPath(b)) ?? "";
            return ra.Length > 0 && ra.Equals(rb, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool PathEquals(string a, string b)
    {
        try { return Path.GetFullPath(a).Equals(Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    // ---- Hash-Helpers ----

    private static List<List<string>> GroupByHash(List<string> files, Func<string, string> hasher)
        => GroupByHashKeyed(files, hasher).Select(g => g.Files).ToList();

    /// <summary>Wie <see cref="GroupByHash"/>, liefert aber den Hash je Gruppe gleich mit.</summary>
    private static List<(string Hash, List<string> Files)> GroupByHashKeyed(
        List<string> files, Func<string, string> hasher)
    {
        var map = new Dictionary<string, List<string>>();
        foreach (var f in files)
        {
            string h;
            try { h = hasher(f); }
            catch { continue; } // nicht lesbare Datei kann kein verifiziertes Duplikat sein
            (map.TryGetValue(h, out var list) ? list : map[h] = new()).Add(f);
        }
        return map.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    /// <summary>SHA-256 über die ersten <paramref name="maxBytes"/> Bytes; -1 = ganze Datei.</summary>
    private static string Hash(string path, int maxBytes)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);

        if (maxBytes < 0)
            return Convert.ToHexString(sha.ComputeHash(fs));

        var buffer = new byte[maxBytes];
        int read = fs.Read(buffer, 0, maxBytes);
        return Convert.ToHexString(sha.ComputeHash(buffer, 0, read));
    }
}
