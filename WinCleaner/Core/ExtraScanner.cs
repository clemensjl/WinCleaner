using System.Runtime.InteropServices;

namespace WinCleaner.Core;

/// <summary>Art eines "Extra"-Fundes, den <see cref="ExtraScanner"/> meldet.</summary>
public enum ExtraKind
{
    /// <summary>Leerer Ordner (keine Dateien und keine Unterordner).</summary>
    EmptyFolder,

    /// <summary>Datei mit 0 Byte Inhalt.</summary>
    EmptyFile,

    /// <summary>Kaputte Verknüpfung (.lnk), deren Ziel nicht (mehr) existiert.</summary>
    BrokenShortcut,

    /// <summary>Kaputter Symlink/Reparse-Point, dessen Ziel nicht (mehr) existiert.</summary>
    BrokenSymlink
}

/// <summary>Ein einzelner Fund: Art und vollständiger Pfad.</summary>
public record ExtraEntry(ExtraKind Kind, string Path);

/// <summary>
/// Ergebnis eines <see cref="ExtraScanner"/>-Laufs: alle Funde plus
/// Summen je Kategorie für die Anzeige bzw. JSON-Ausgabe.
/// </summary>
public sealed class ExtraScanResult
{
    public List<ExtraEntry> Entries { get; } = new();

    public int EmptyFolders   => Entries.Count(e => e.Kind == ExtraKind.EmptyFolder);
    public int EmptyFiles     => Entries.Count(e => e.Kind == ExtraKind.EmptyFile);
    public int BrokenShortcuts => Entries.Count(e => e.Kind == ExtraKind.BrokenShortcut);
    public int BrokenSymlinks  => Entries.Count(e => e.Kind == ExtraKind.BrokenSymlink);
    public int Total          => Entries.Count;
}

/// <summary>
/// Durchsucht ein Verzeichnis rekursiv nach "Ballast": leeren Ordnern,
/// 0-Byte-Dateien sowie kaputten Verknüpfungen (.lnk mit fehlendem Ziel) und
/// kaputten Symlinks/Reparse-Points (Ziel existiert nicht mehr).
///
/// Reparse-Points werden bewusst NICHT bei der normalen Aufzählung verfolgt
/// (sonst Endlosschleifen-Gefahr), aber gezielt einzeln geprüft, um kaputte
/// Symlinks zu erkennen.
/// </summary>
public sealed class ExtraScanner
{
    // Aufzählung: rekursiv, unzugängliche Pfade ignorieren, Reparse-Points NICHT
    // verfolgen (die prüfen wir gezielt selbst, um kaputte Symlinks zu finden).
    private static readonly EnumerationOptions WalkOpts = new()
    {
        RecurseSubdirectories    = true,
        IgnoreInaccessible       = true,
        AttributesToSkip         = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false
    };

    // Aufzählung der direkten Kinder (Top-Level) – inkl. Reparse-Points, damit
    // ein leerer Ordner auch dann als leer gilt, wenn er nur einen Reparse-Point
    // enthält? Nein: ein Reparse-Point zählt als Inhalt. Wir nutzen Standard.
    private static readonly EnumerationOptions FlatOpts = new()
    {
        RecurseSubdirectories    = false,
        IgnoreInaccessible       = true,
        ReturnSpecialDirectories = false
    };

    private readonly Logger _logger;
    public ExtraScanner(Logger logger) => _logger = logger;

    /// <summary>
    /// Führt den Scan unter <paramref name="rootPath"/> durch und liefert alle Funde.
    /// </summary>
    public ExtraScanResult Scan(string rootPath)
    {
        var result = new ExtraScanResult();
        if (!Directory.Exists(rootPath))
        {
            _logger.Error($"Pfad nicht gefunden: {rootPath}");
            return result;
        }

        ScanReparsePoints(rootPath, result);
        ScanFiles(rootPath, result);
        ScanEmptyFolders(rootPath, result);

        _logger.Info($"Scan fertig: {result.EmptyFolders} leere Ordner, {result.EmptyFiles} 0-Byte-Dateien, " +
                     $"{result.BrokenShortcuts} kaputte Verknüpfungen, {result.BrokenSymlinks} kaputte Symlinks.");
        return result;
    }

    // ---- (b) 0-Byte-Dateien + (c) kaputte .lnk-Verknüpfungen ----
    private void ScanFiles(string root, ExtraScanResult result)
    {
        foreach (var file in SafeDeepFiles(root))
        {
            try
            {
                var info = new FileInfo(file);

                // Kaputte .lnk-Verknüpfung: Ziel existiert nicht mehr.
                if (info.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    if (IsBrokenShortcut(file))
                        result.Entries.Add(new ExtraEntry(ExtraKind.BrokenShortcut, file));
                    continue;
                }

                // 0-Byte-Datei (echte Datei, kein Reparse-Point – die werden
                // durch AttributesToSkip schon übersprungen).
                if (info.Length == 0)
                    result.Entries.Add(new ExtraEntry(ExtraKind.EmptyFile, file));
            }
            catch { /* gesperrt/weg – still überspringen */ }
        }
    }

    // ---- (a) leere Ordner ----
    private void ScanEmptyFolders(string root, ExtraScanResult result)
    {
        foreach (var dir in SafeDeepDirs(root))
        {
            try
            {
                // Leer = kein einziger Eintrag (weder Datei noch Unterordner).
                bool hasAny = Directory.EnumerateFileSystemEntries(dir, "*", FlatOpts).Any();
                if (!hasAny)
                    result.Entries.Add(new ExtraEntry(ExtraKind.EmptyFolder, dir));
            }
            catch { /* gesperrt/weg – still überspringen */ }
        }
    }

    // ---- (c) kaputte Symlinks/Reparse-Points (Datei oder Ordner) ----
    private void ScanReparsePoints(string root, ExtraScanResult result)
    {
        // Reparse-Points selbst zählen wir manuell auf, da der rekursive Walk sie
        // (per AttributesToSkip) überspringt. Wir gehen die Ordnerstruktur ab und
        // prüfen je Ebene Datei- und Ordner-Einträge auf das ReparsePoint-Attribut.
        foreach (var dir in EnumerateAllDirsIncludingRoot(root))
        {
            foreach (var entry in SafeFlatEntries(dir))
            {
                try
                {
                    var attr = File.GetAttributes(entry);
                    if ((attr & FileAttributes.ReparsePoint) == 0) continue;

                    // .lnk werden separat als BrokenShortcut behandelt; ein .lnk ist
                    // aber kein Reparse-Point, daher hier keine Sonderbehandlung nötig.
                    if (IsBrokenReparseTarget(entry))
                        result.Entries.Add(new ExtraEntry(ExtraKind.BrokenSymlink, entry));
                }
                catch { /* gesperrt/weg – still überspringen */ }
            }
        }
    }

    // ---- Symlink-Zielprüfung ----

    /// <summary>
    /// True, wenn der Reparse-Point (Symlink/Junction) auf ein nicht mehr
    /// existierendes Ziel zeigt. Nutzt das .NET-Symlink-API
    /// (<see cref="FileSystemInfo.LinkTarget"/>) und folgt der Kette final.
    /// </summary>
    private static bool IsBrokenReparseTarget(string path)
    {
        try
        {
            // Als Datei und als Ordner betrachten – eines der beiden trifft zu.
            FileSystemInfo info = Directory.Exists(path) || File.Exists(path)
                ? (Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path))
                : new FileInfo(path);

            // ResolveLinkTarget folgt der Verkettung bis zum Endziel; null = kein Link.
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            if (target is null)
            {
                // Kein auflösbares .NET-Linkziel: prüfe rohes LinkTarget.
                var raw = info.LinkTarget;
                if (string.IsNullOrEmpty(raw)) return false; // kein Symlink -> nicht kaputt
                return !PathExists(raw);
            }

            // target.Exists ist nach Resolve aktualisiert: existiert das Endziel?
            return !target.Exists;
        }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
        catch (IOException)
        {
            // Zyklischer/ungültiger Link o. Ä. – als kaputt werten.
            return true;
        }
        catch { return false; }
    }

    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

    // ---- .lnk-Auflösung über IShellLink (COM) ----

    /// <summary>
    /// True, wenn eine .lnk-Verknüpfung ein Dateisystem-Ziel hat, das nicht (mehr)
    /// existiert. Verknüpfungen ohne Pfadziel (z. B. auf virtuelle Shell-Objekte)
    /// werden NICHT als kaputt gewertet, um Fehlalarme zu vermeiden.
    /// </summary>
    private static bool IsBrokenShortcut(string lnkPath)
    {
        try
        {
            var target = ResolveShortcutTarget(lnkPath);
            if (string.IsNullOrWhiteSpace(target)) return false; // kein Pfadziel -> nicht beurteilbar
            return !PathExists(target);
        }
        catch { return false; }
    }

    /// <summary>
    /// Liest das Zielpfad-Feld einer .lnk-Datei über die Shell-COM-Schnittstelle
    /// IShellLink aus. Liefert null/leer, wenn kein Dateisystem-Ziel hinterlegt ist.
    /// </summary>
    private static string? ResolveShortcutTarget(string lnkPath)
    {
        ShellLink? link = null;
        try
        {
            link = new ShellLink();
            var persist = (IPersistFile)link;
            // SLR_NO_UI | SLR_NOUPDATE: keine Dialoge, kein Selbst-Reparieren der Verknüpfung.
            persist.Load(lnkPath, 0);

            var shellLink = (IShellLinkW)link;
            // Auflösen ohne UI/Reparatur, damit wir das gespeicherte Ziel sehen.
            try { shellLink.Resolve(IntPtr.Zero, SLR_NO_UI | SLR_NOUPDATE); }
            catch { /* Auflösen darf scheitern – wir lesen trotzdem das gespeicherte Ziel */ }

            var sb = new System.Text.StringBuilder(260);
            shellLink.GetPath(sb, sb.Capacity, IntPtr.Zero, SLGP_RAWPATH);
            return sb.ToString();
        }
        finally
        {
            if (link is not null) Marshal.FinalReleaseComObject(link);
        }
    }

    // ---- sichere Aufzählungs-Helfer ----

    private static IEnumerable<string> SafeDeepFiles(string root)
    {
        try { return Directory.EnumerateFiles(root, "*", WalkOpts); }
        catch { return Enumerable.Empty<string>(); }
    }

    private static IEnumerable<string> SafeDeepDirs(string root)
    {
        try { return Directory.EnumerateDirectories(root, "*", WalkOpts); }
        catch { return Enumerable.Empty<string>(); }
    }

    private static IEnumerable<string> SafeFlatEntries(string dir)
    {
        try { return Directory.EnumerateFileSystemEntries(dir, "*", FlatOpts); }
        catch { return Enumerable.Empty<string>(); }
    }

    /// <summary>
    /// Liefert das Wurzelverzeichnis und alle Unterordner (rekursiv, ohne Reparse-
    /// Points zu verfolgen). Wird zum gezielten Aufspüren von Reparse-Point-Kindern
    /// je Ebene gebraucht.
    /// </summary>
    private static IEnumerable<string> EnumerateAllDirsIncludingRoot(string root)
    {
        yield return root;
        foreach (var d in SafeDeepDirs(root))
            yield return d;
    }

    // ---- COM-Interop für IShellLink (.lnk lesen) ----

    private const uint SLR_NO_UI   = 0x0001;
    private const uint SLR_NOUPDATE = 0x0008;
    private const uint SLGP_RAWPATH = 0x0004;

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile,
                     int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath,
                             int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName,
                  [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}
