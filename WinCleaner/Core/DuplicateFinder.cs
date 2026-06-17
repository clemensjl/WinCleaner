using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using VB = Microsoft.VisualBasic.FileIO; // Recycle-Bin-Löschung wie im JunkCleaner

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
/// Ergebnis einer Lösch-/Hardlink-Aktion über mehrere Gruppen, für JSON-Ausgabe
/// und Zusammenfassung. Rein informativ – im Dry-Run beschreibt es die geplante
/// Aktion.
/// </summary>
public sealed record DuplicateActionResult(
    int GroupsProcessed,
    int GroupsSkipped,
    int FilesAffected,
    long BytesAffected,
    bool DryRun,
    bool HardLink,
    bool SentToRecycleBin);

/// <summary>
/// Findet inhaltsgleiche Dateien. Statt jede Datei voll zu hashen, filtert ein
/// dreistufiges Verfahren Kandidaten heraus: erst nach Dateigröße gruppieren,
/// dann ein günstiger Partial-Hash (erste 4 KB), und nur bei verbleibenden
/// Kollisionen der volle SHA-256-Hash. Das spart bei großen Bäumen massiv I/O.
/// </summary>
public class DuplicateFinder
{
    private const int PartialBytes = 4096;

    private static readonly EnumerationOptions DeepOpts = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible    = true,
        AttributesToSkip      = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false
    };

    private readonly Logger _logger;
    public DuplicateFinder(Logger logger) => _logger = logger;

    public List<DuplicateGroup> Find(string rootPath)
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
            // Eigene Hardlink-Sicherungen NICHT erfassen, sonst tauchen sie in
            // Folgeläufen als neue Duplikate auf.
            if (file.EndsWith(".wcbak", StringComparison.OrdinalIgnoreCase)) continue;

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

                // Stufe 3: voller Hash nur für echte Kandidaten.
                foreach (var fullGroup in GroupByHash(partialGroup, f => Hash(f, -1)))
                {
                    fullHashes += fullGroup.Count;
                    if (fullGroup.Count < 2) continue;

                    string hash = Hash(fullGroup[0], -1);
                    result.Add(new DuplicateGroup(hash, fullGroup, size * fullGroup.Count));
                }
            }
        }

        result.Sort((a, b) => b.TotalBytes.CompareTo(a.TotalBytes));
        _logger.Info($"Duplikatsuche fertig: {result.Count} Gruppen, {fullHashes} volle Hashes berechnet.");
        return result;
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
                    if (sendToRecycleBin)
                        VB.FileSystem.DeleteFile(f, VB.UIOption.OnlyErrorDialogs, VB.RecycleOption.SendToRecycleBin);
                    else
                        File.Delete(f);
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

        int groupsProcessed = 0, groupsSkipped = 0, filesAffected = 0;
        long bytesAffected = 0;

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
                if (hardLink && !SameVolume(keepFile, f))
                {
                    _logger.Info($"Hardlink nicht möglich (anderes Volume), übersprungen: {f}");
                    continue;
                }

                if (dryRun)
                {
                    _logger.Info(hardLink
                        ? $"[Probelauf] Würde durch Hardlink auf '{keepFile}' ersetzen: {f}"
                        : $"[Probelauf] Würde {(sendToRecycleBin ? "in den Papierkorb verschieben" : "permanent löschen")}: {f}");
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
                    filesAffected++;
                    bytesAffected += perFileBytes;
                    anyAffected = true;
                }
            }

            if (anyAffected) groupsProcessed++; else groupsSkipped++;
        }

        var verb = dryRun ? "geplant" : (hardLink ? "durch Hardlinks ersetzt"
            : sendToRecycleBin ? "in den Papierkorb verschoben" : "gelöscht");
        _logger.Info($"Duplikat-Aktion fertig: {filesAffected} Dateien {verb}, " +
                     $"{DiskAnalyzer.FormatSize(bytesAffected)}, {groupsProcessed} Gruppen bearbeitet, " +
                     $"{groupsSkipped} übersprungen.");

        return new DuplicateActionResult(
            GroupsProcessed: groupsProcessed,
            GroupsSkipped: groupsSkipped,
            FilesAffected: filesAffected,
            BytesAffected: bytesAffected,
            DryRun: dryRun,
            HardLink: hardLink,
            SentToRecycleBin: sendToRecycleBin);
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
            if (sendToRecycleBin)
                VB.FileSystem.DeleteFile(path, VB.UIOption.OnlyErrorDialogs, VB.RecycleOption.SendToRecycleBin);
            else
                File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Debug($"Löschen fehlgeschlagen {path}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Ersetzt <paramref name="duplicate"/> durch einen Hardlink auf
    /// <paramref name="keepFile"/>. Erst wird das Duplikat (umkehrbar) entfernt,
    /// dann der Hardlink angelegt; schlägt der Link fehl, wird versucht,
    /// die Original-Datei aus dem Quell-Inhalt wiederherzustellen.
    /// </summary>
    private bool ReplaceWithHardLink(string duplicate, string keepFile, bool sendToRecycleBin)
    {
        // Vor dem Entfernen eine permanente Sicherung anlegen, falls das
        // Hardlink-Anlegen scheitert (Papierkorb lässt sich nicht zuverlässig
        // automatisch zurückholen).
        string backup = duplicate + ".wcbak";
        try
        {
            File.Copy(duplicate, backup, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.Error($"Hardlink: Sicherung fehlgeschlagen, übersprungen {duplicate}: {ex.Message}");
            return false;
        }

        try
        {
            File.Delete(duplicate); // Platz für den neuen Linknamen schaffen

            if (!CreateHardLink(duplicate, keepFile, IntPtr.Zero))
            {
                int err = Marshal.GetLastWin32Error();
                throw new Win32Exception(err);
            }

            // Erfolg: Sicherung verwerfen.
            TryDelete(backup);
            _logger.Debug($"Hardlink angelegt: {duplicate} -> {keepFile}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Hardlink fehlgeschlagen für {duplicate}: {ex.Message} – stelle Original wieder her.");
            // Wiederherstellung aus der Sicherung versuchen.
            try
            {
                if (!File.Exists(duplicate))
                    File.Move(backup, duplicate);
                else
                    TryDelete(backup);
            }
            catch (Exception rex)
            {
                _logger.Error($"Wiederherstellung fehlgeschlagen für {duplicate}: {rex.Message}. " +
                              $"Sicherung liegt unter: {backup}");
            }
            return false;
        }
    }

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
    {
        var map = new Dictionary<string, List<string>>();
        foreach (var f in files)
        {
            string h;
            try { h = hasher(f); }
            catch { continue; } // nicht lesbare Datei kann kein verifiziertes Duplikat sein
            (map.TryGetValue(h, out var list) ? list : map[h] = new()).Add(f);
        }
        return map.Values.ToList();
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
