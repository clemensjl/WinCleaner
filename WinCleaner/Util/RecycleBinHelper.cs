using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WinCleaner.Util;

/// <summary>
/// Zentrale, umkehrbare Löschung in den Papierkorb – konsistent über alle
/// Befehle (JunkCleaner, DuplicateFinder, BrowserCleaner …).
/// <para>
/// Nutzt <c>SHFileOperation</c> mit vollständig unterdrückter Oberfläche
/// (kein Bestätigungs-, Fehler- oder „Sie benötigen Berechtigung"-Dialog).
/// Nicht löschbare Dateien (gesperrt, geschützt) führen zu einer
/// <see cref="IOException"/>, die der Aufrufer still überspringt – der Nutzer
/// wird für unkritische Bereinigung NIE nach Berechtigungen gefragt.
/// </para>
/// Für Tests bzw. bewusst permanentes Löschen kann <c>toRecycleBin = false</c>
/// gesetzt werden (dann klassisches <see cref="File.Delete"/>).
/// </summary>
public static class RecycleBinHelper
{
    public static void DeleteFile(string path, bool toRecycleBin = true)
    {
        if (toRecycleBin) RecycleSilently(path);
        else File.Delete(path);
    }

    public static void DeleteDirectory(string path, bool toRecycleBin = true)
    {
        if (toRecycleBin) RecycleSilently(path);
        else Directory.Delete(path, recursive: true);
    }

    // ---- Stiller Papierkorb-Löschvorgang (SHFileOperation) ----

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT           = 0x0004; // kein Fortschrittsdialog
    private const ushort FOF_NOCONFIRMATION   = 0x0010; // kein „Wirklich löschen?"
    private const ushort FOF_ALLOWUNDO        = 0x0040; // in den Papierkorb (umkehrbar)
    private const ushort FOF_NOCONFIRMMKDIR   = 0x0200;
    private const ushort FOF_NOERRORUI        = 0x0400; // kein Fehler-/Berechtigungsdialog

    private const ushort FlagsSilentRecycle =
        FOF_ALLOWUNDO | FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_NOCONFIRMMKDIR;

    private static void RecycleSilently(string path)
    {
        // pFrom muss doppelt null-terminiert sein; die angehängte \0 liefert
        // zusammen mit dem Marshalling-Terminator das nötige Doppel-Null.
        var op = new SHFILEOPSTRUCT
        {
            wFunc  = FO_DELETE,
            pFrom  = path + "\0",
            fFlags = FlagsSilentRecycle
        };

        int result = SHFileOperation(ref op);
        if (result != 0 || op.fAnyOperationsAborted != 0)
            throw new IOException($"Löschen in den Papierkorb fehlgeschlagen (Code {result}): {path}");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
}
