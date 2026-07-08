using WinCleaner.Util;

namespace WinCleaner.Tests;

/// <summary>
/// Prüft den stillen Papierkorb-Löschvorgang (SHFileOperation). Sichert vor
/// allem das P/Invoke-Marshalling ab: doppelt null-terminierter Pfad, korrektes
/// Struct-Layout, keine geworfene Ausnahme im Erfolgsfall.
/// </summary>
public class RecycleBinHelperTests
{
    [Fact]
    public void DeleteFile_ToRecycleBin_RemovesFileWithoutError()
    {
        using var tmp = new TempDir();
        var file = tmp.Write("weg.txt", "inhalt");
        Assert.True(File.Exists(file));

        RecycleBinHelper.DeleteFile(file, toRecycleBin: true);

        Assert.False(File.Exists(file)); // in den Papierkorb verschoben
    }

    [Fact]
    public void DeleteDirectory_ToRecycleBin_RemovesFolderWithoutError()
    {
        using var tmp = new TempDir();
        tmp.Write("ordner/datei.txt", "x");
        var dir = System.IO.Path.Combine(tmp.Path, "ordner");
        Assert.True(Directory.Exists(dir));

        RecycleBinHelper.DeleteDirectory(dir, toRecycleBin: true);

        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void DeleteFile_Missing_ThrowsIOException_NoDialog()
    {
        // Nicht existierende Datei -> SHFileOperation meldet Fehler als IOException
        // (kein Dialog), damit Aufrufer sie still überspringen können.
        var missing = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "wc_missing_" + System.Guid.NewGuid().ToString("N") + ".txt");
        Assert.Throws<IOException>(() => RecycleBinHelper.DeleteFile(missing, toRecycleBin: true));
    }
}
