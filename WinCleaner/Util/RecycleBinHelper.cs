using VB = Microsoft.VisualBasic.FileIO;

namespace WinCleaner.Util;

/// <summary>
/// Zentrale, umkehrbare Löschung in den Papierkorb – konsistent über alle
/// Befehle (JunkCleaner, DuplicateFinder, BrowserCleaner …). Für Tests bzw.
/// bewusst permanentes Löschen kann <paramref name="toRecycleBin"/> = false
/// gesetzt werden.
/// </summary>
public static class RecycleBinHelper
{
    public static void DeleteFile(string path, bool toRecycleBin = true)
    {
        if (toRecycleBin)
            VB.FileSystem.DeleteFile(path, VB.UIOption.OnlyErrorDialogs, VB.RecycleOption.SendToRecycleBin);
        else
            File.Delete(path);
    }

    public static void DeleteDirectory(string path, bool toRecycleBin = true)
    {
        if (toRecycleBin)
            VB.FileSystem.DeleteDirectory(path, VB.UIOption.OnlyErrorDialogs, VB.RecycleOption.SendToRecycleBin);
        else
            Directory.Delete(path, recursive: true);
    }
}
