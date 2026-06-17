using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WinCleaner.Core;

public enum Safety
{
    Safe,      // ohne hohes Risiko (Temp, Prefetch, Browser-Caches)
    Caution,   // kann Nebenwirkungen haben (z. B. Windows Update Cache)
    Dangerous  // reserviert
}

public record JunkItem(string Category, string Path, long TotalBytes, int FileCount, Safety Safety = Safety.Safe);

public class JunkReport
{
    public List<JunkItem> Items { get; } = new();
    public long TotalBytes => Items.Sum(i => i.TotalBytes);
    public int TotalFiles  => Items.Sum(i => i.FileCount);

    public IEnumerable<string[]> ToTableRows() =>
        Items.Select(i => new[] {
            i.Category, i.Path, i.FileCount.ToString(),
            (i.TotalBytes/(1024*1024.0)).ToString("N1")
        });
}

public class JunkScanner
{
    private static readonly EnumerationOptions DeepOpts = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint, // keine Junctions/Links folgen
        ReturnSpecialDirectories = false,
        MatchType = MatchType.Simple
    };

    private readonly Logger _logger;
    public JunkScanner(Logger logger) => _logger = logger;

    public JunkReport Scan()
    {
        var report = new JunkReport();

        string windir      = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string localApp    = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roamingApp  = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        // 1) User- und Windows-Temp
        TryAddFolder(report, "Temp (User)",    Path.GetTempPath(), Safety.Safe);
        TryAddFolder(report, "Temp (Windows)", Path.Combine(windir, "Temp"), Safety.Safe);

        // 2) Prefetch
        TryAddFolder(report, "Prefetch", Path.Combine(windir, "Prefetch"), Safety.Safe);

        // 3) Chromium-Browser: Chrome / Edge / Brave
        AddChromiumCaches(report, "Chrome", Path.Combine(localApp, "Google",        "Chrome",        "User Data"));
        AddChromiumCaches(report, "Edge",   Path.Combine(localApp, "Microsoft",     "Edge",          "User Data"));
        AddChromiumCaches(report, "Brave",  Path.Combine(localApp, "BraveSoftware", "Brave-Browser", "User Data"));

        // 4) Firefox (Roaming + Local)
        AddFirefoxCaches(report, Path.Combine(roamingApp, "Mozilla", "Firefox", "Profiles"));
        AddFirefoxCaches(report, Path.Combine(localApp,   "Mozilla", "Firefox", "Profiles"));

        // 5) Windows Update Cache – nur Hinweis (Caution)
        TryAddFolder(report, "Windows Update Cache (SoftwareDistribution\\Download)",
            Path.Combine(windir, "SoftwareDistribution", "Download"),
            Safety.Caution);

        // 6) Windows Error Reporting
        TryAddFolder(report, "Windows Error Reporting (ReportQueue)",
            Path.Combine(programData, "Microsoft", "Windows", "WER", "ReportQueue"), Safety.Safe);
        TryAddFolder(report, "Windows Error Reporting (ReportArchive)",
            Path.Combine(programData, "Microsoft", "Windows", "WER", "ReportArchive"), Safety.Safe);

        _logger.Info("Junk-Scan abgeschlossen.");
        return report;
    }

    // ---- Helpers ----

    private void AddChromiumCaches(JunkReport report, string browserName, string userDataPath)
    {
        if (!Directory.Exists(userDataPath)) return;

        IEnumerable<string> profiles;
        try { profiles = Directory.EnumerateDirectories(userDataPath, "*", SearchOption.TopDirectoryOnly); }
        catch { return; }

        // Übliche Cache-Verzeichnisse in Chromium
        string[] cacheDirs =
        {
            "Cache",
            Path.Combine("Code Cache","js"),
            "GPUCache",
            "Media Cache",
            "DawnCache"
        };

        foreach (var profile in profiles)
        {
            foreach (var relative in cacheDirs)
            {
                var cachePath = Path.Combine(profile, relative);
                TryAddFolder(report, $"Browser Cache – {browserName}", cachePath, Safety.Safe);
            }
        }
    }

    private void AddFirefoxCaches(JunkReport report, string profilesRoot)
    {
        if (!Directory.Exists(profilesRoot)) return;

        IEnumerable<string> profiles;
        try { profiles = Directory.EnumerateDirectories(profilesRoot, "*", SearchOption.TopDirectoryOnly); }
        catch { return; }

        foreach (var profile in profiles)
        {
            var cache2   = Path.Combine(profile, "cache2");
            var entries  = Path.Combine(cache2, "entries");
            TryAddFolder(report, "Browser Cache – Firefox", entries, Safety.Safe);
            TryAddFolder(report, "Browser Cache – Firefox (cache2)", cache2, Safety.Safe);
        }
    }

    private void TryAddFolder(JunkReport report, string category, string folder, Safety safety)
    {
        try
        {
            var (bytes, files) = MeasureFolder(folder);
            if (files > 0 && bytes > 0)
                report.Items.Add(new JunkItem(category, folder, bytes, files, safety));
        }
        catch
        {
            // z. B. Zugriffsfehler – still ignorieren
        }
    }

    /// <summary>
    /// Summiert Größe und Anzahl aller Dateien unterhalb <paramref name="folder"/>
    /// (rekursiv, ohne Reparse Points). Fehlender Pfad oder gesperrte Dateien
    /// liefern (0, 0) bzw. werden übersprungen.
    /// </summary>
    internal static (long bytes, int files) MeasureFolder(string folder)
    {
        long bytes = 0;
        int  files = 0;
        if (!Directory.Exists(folder)) return (0, 0);

        // schnell, robust gegen gesperrte Pfade + keine Reparse Points
        foreach (var file in Directory.EnumerateFiles(folder, "*", DeepOpts))
        {
            try { bytes += new FileInfo(file).Length; files++; }
            catch { /* ignore locked/inaccessible */ }
        }
        return (bytes, files);
    }
}
