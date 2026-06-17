using System.Management;
using System.Security.Cryptography;

namespace WinCleaner.SystemTools;

/// <summary>
/// Erkannter Datenträgertyp für ein Laufwerk. Wird genutzt, um vor wirkungslosem
/// (und die Lebensdauer verkürzendem) Überschreiben auf SSDs zu warnen.
/// </summary>
public enum DriveMediaType
{
    /// <summary>Magnetische Festplatte (HDD) – Überschreiben ist hier sinnvoll.</summary>
    Hdd,
    /// <summary>Solid-State-Datenträger (SSD/NVMe) – Überschreiben ist wirkungslos (Wear-Leveling/TRIM).</summary>
    Ssd,
    /// <summary>Typ konnte nicht ermittelt werden.</summary>
    Unknown
}

/// <summary>
/// Gemeinsame Logik für das sichere, NICHT umkehrbare Löschen: mehrfaches
/// Überschreiben von Dateiinhalten vor dem endgültigen Löschen sowie die
/// Erkennung des Datenträgertyps (HDD/SSD). Auf SSDs ist Überschreiben durch
/// Wear-Leveling und TRIM wirkungslos – Aufrufer warnen den Nutzer entsprechend.
/// </summary>
public sealed class SecureDelete
{
    /// <summary>Standardanzahl der Überschreib-Pässe.</summary>
    public const int DefaultPasses = 3;

    private const int BufferSize = 1024 * 1024; // 1 MB Schreibpuffer

    private readonly Core.Logger _logger;

    public SecureDelete(Core.Logger logger) => _logger = logger;

    /// <summary>
    /// Überschreibt den Inhalt einer einzelnen Datei <paramref name="passes"/>-mal
    /// (Zufallsdaten je Pass, letzter Pass mit Nullen) und löscht sie anschließend
    /// endgültig. IRREVERSIBEL. Liefert true bei Erfolg.
    /// </summary>
    public bool OverwriteFile(string path, int passes)
    {
        if (passes < 1) passes = 1;

        try
        {
            // Schreibgeschützte/versteckte Attribute entfernen, sonst schlägt Schreiben/Löschen fehl.
            try { File.SetAttributes(path, FileAttributes.Normal); } catch { /* ignorieren */ }

            long length = new FileInfo(path).Length;

            // Leere Dateien müssen nur gelöscht werden.
            if (length > 0)
            {
                using var rng = RandomNumberGenerator.Create();
                var buffer = new byte[BufferSize];

                using var fs = new FileStream(path, FileMode.Open, FileAccess.Write,
                    FileShare.None, BufferSize, FileOptions.WriteThrough);

                for (int pass = 0; pass < passes; pass++)
                {
                    bool lastPass = pass == passes - 1;
                    if (lastPass) Array.Clear(buffer, 0, buffer.Length); // letzter Pass: Nullen

                    fs.Seek(0, SeekOrigin.Begin);
                    long remaining = length;
                    while (remaining > 0)
                    {
                        int chunk = (int)Math.Min(buffer.Length, remaining);
                        if (!lastPass) rng.GetBytes(buffer, 0, chunk);
                        fs.Write(buffer, 0, chunk);
                        remaining -= chunk;
                    }
                    fs.Flush(flushToDisk: true);
                }
            }

            File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Sicheres Löschen fehlgeschlagen für \"{path}\": {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Überschreibt und löscht alle Dateien unterhalb eines Ordners und entfernt
    /// anschließend die (leeren) Verzeichnisse. Liefert die Anzahl erfolgreich
    /// sicher gelöschter Dateien. Gesperrte/unzugängliche Pfade werden still
    /// übersprungen.
    /// </summary>
    public int OverwriteDirectory(string path, int passes, out int failed)
    {
        int ok = 0;
        failed = 0;

        var opts = new System.IO.EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (var file in EnumerateFilesSafe(path, opts))
        {
            if (OverwriteFile(file, passes)) ok++;
            else failed++;
        }

        // Verzeichnisse von innen nach außen entfernen.
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(path, "*", opts)
                         .OrderByDescending(d => d.Length))
            {
                try { Directory.Delete(dir, recursive: false); } catch { /* nicht leer/gesperrt */ }
            }
            try { Directory.Delete(path, recursive: true); } catch { /* Rest gesperrt */ }
        }
        catch { /* ignorieren */ }

        return ok;
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root, System.IO.EnumerationOptions opts)
    {
        try { return Directory.EnumerateFiles(root, "*", opts); }
        catch { return Enumerable.Empty<string>(); }
    }

    /// <summary>
    /// Ermittelt den Datenträgertyp (HDD/SSD/Unbekannt) für den Pfad bzw. das
    /// Laufwerk. Nutzt WMI (<c>MSFT_PhysicalDisk.MediaType</c> im Namespace
    /// <c>root\Microsoft\Windows\Storage</c>); bei Fehlern wird
    /// <see cref="DriveMediaType.Unknown"/> geliefert (der Aufrufer warnt dann generisch).
    /// </summary>
    public DriveMediaType DetectMediaType(string pathOrDrive)
    {
        try
        {
            // MSFT_PhysicalDisk.MediaType: 3 = HDD, 4 = SSD, 5 = SCM, 0 = unbekannt.
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            scope.Connect();

            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT MediaType FROM MSFT_PhysicalDisk"));

            bool anyHdd = false, anySsd = false, anyKnown = false;
            foreach (ManagementBaseObject disk in searcher.Get())
            {
                using (disk)
                {
                    var raw = disk["MediaType"];
                    if (raw is null) continue;
                    ushort mt = Convert.ToUInt16(raw);
                    switch (mt)
                    {
                        case 3: anyHdd = true; anyKnown = true; break;
                        case 4:
                        case 5: anySsd = true; anyKnown = true; break;
                    }
                }
            }

            // Konservativ: liegt überhaupt eine SSD vor, gilt die SSD-Warnung.
            if (anySsd) return DriveMediaType.Ssd;
            if (anyHdd) return DriveMediaType.Hdd;
            if (anyKnown) return DriveMediaType.Unknown;
            return DriveMediaType.Unknown;
        }
        catch (Exception ex)
        {
            _logger.Debug($"Datenträgertyp-Erkennung fehlgeschlagen: {ex.Message}");
            return DriveMediaType.Unknown;
        }
    }
}
