using WinCleaner.Util;

namespace WinCleaner.Core;

/// <summary>
/// Die löschbaren Kategorien je Browser-Profil. <see cref="Cache"/> ist die
/// harmlose Standard-Kategorie; <see cref="Cookies"/>, <see cref="History"/>
/// und <see cref="Sessions"/> sind heikler (Logins/Verlauf/offene Tabs gehen
/// verloren) und werden daher nur per Opt-in einbezogen.
/// </summary>
[Flags]
public enum BrowserCategory
{
    None     = 0,
    Cache    = 1 << 0,
    Cookies  = 1 << 1,
    History  = 1 << 2,
    Sessions = 1 << 3
}

/// <summary>
/// Ein konkret löschbares Ziel: eine Kategorie eines Profils, aufgelöst auf
/// einen oder mehrere Pfade (Datei oder Ordner) mit ermittelter Größe.
/// </summary>
public sealed record BrowserTarget(
    string Browser,
    string Profile,
    BrowserCategory Category,
    IReadOnlyList<string> Paths,
    long TotalBytes,
    int FileCount);

/// <summary>
/// Maschinenlesbare Ergebniszeile (für <c>--json</c>): aggregiert pro
/// Browser/Profil/Kategorie.
/// </summary>
public sealed record BrowserCleanResult(
    string Browser,
    string Profile,
    string Category,
    long Bytes,
    int Files,
    bool Deleted);

/// <summary>
/// Ermittelt installierte Browser-Profile (Chrome, Edge, Brave als Chromium,
/// dazu Firefox) und die Größe der je Profil löschbaren Kategorien (Cache,
/// Cookies, Verlauf, Sessions). Löscht – wenn freigegeben – umkehrbar in den
/// Papierkorb. Gesperrte Dateien (laufender Browser) werden still übersprungen.
/// Eigenständige Logik; verändert <see cref="JunkScanner"/> NICHT.
/// </summary>
public sealed class BrowserCleaner
{
    private static readonly EnumerationOptions DeepOpts = new()
    {
        RecurseSubdirectories    = true,
        IgnoreInaccessible       = true,
        AttributesToSkip         = FileAttributes.ReparsePoint, // keine Junctions/Links folgen
        ReturnSpecialDirectories = false
    };

    private readonly Logger _logger;
    public BrowserCleaner(Logger logger) => _logger = logger;

    /// <summary>Kanonische, unterstützte Browser-Schlüssel (Kleinschreibung).</summary>
    public static readonly string[] AllBrowsers = { "chrome", "edge", "brave", "firefox" };

    /// <summary>
    /// Sammelt alle löschbaren Ziele für die gewünschten Browser und Kategorien.
    /// Es werden nur tatsächlich vorhandene Profile/Pfade mit Inhalt geliefert.
    /// </summary>
    /// <param name="browsers">Browser-Schlüssel (chrome/edge/brave/firefox).</param>
    /// <param name="categories">Einzubeziehende Kategorien (mind. Cache).</param>
    public List<BrowserTarget> Collect(IEnumerable<string> browsers, BrowserCategory categories)
    {
        var targets = new List<BrowserTarget>();
        string localApp   = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roamingApp = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        foreach (var raw in browsers)
        {
            var b = raw.Trim().ToLowerInvariant();
            switch (b)
            {
                case "chrome":
                    CollectChromium(targets, "Chrome",
                        Path.Combine(localApp, "Google", "Chrome", "User Data"), categories);
                    break;
                case "edge":
                    CollectChromium(targets, "Edge",
                        Path.Combine(localApp, "Microsoft", "Edge", "User Data"), categories);
                    break;
                case "brave":
                    CollectChromium(targets, "Brave",
                        Path.Combine(localApp, "BraveSoftware", "Brave-Browser", "User Data"), categories);
                    break;
                case "firefox":
                    CollectFirefox(targets,
                        Path.Combine(roamingApp, "Mozilla", "Firefox", "Profiles"),
                        Path.Combine(localApp,   "Mozilla", "Firefox", "Profiles"),
                        categories);
                    break;
                default:
                    _logger.Error($"Unbekannter Browser '{raw}' wird übersprungen " +
                                  "(erlaubt: chrome, edge, brave, firefox).");
                    break;
            }
        }

        return targets;
    }

    /// <summary>
    /// Löscht die übergebenen Ziele umkehrbar in den Papierkorb. Gesperrte oder
    /// fehlende Pfade (z. B. weil der Browser läuft) werden still übersprungen.
    /// Liefert die Anzahl tatsächlich gelöschter Pfade.
    /// </summary>
    public int Delete(IEnumerable<BrowserTarget> targets)
    {
        int deleted = 0;
        foreach (var t in targets)
        {
            foreach (var path in t.Paths)
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        RecycleBinHelper.DeleteDirectory(path);
                        deleted++;
                    }
                    else if (File.Exists(path))
                    {
                        RecycleBinHelper.DeleteFile(path);
                        deleted++;
                    }
                }
                catch (Exception ex)
                {
                    // Häufigster Fall: Browser läuft -> Datei gesperrt. Still überspringen.
                    _logger.Error($"Übersprungen (evtl. Browser geöffnet): {path} – {ex.Message}");
                }
            }
        }
        return deleted;
    }

    // ---- Chromium (Chrome / Edge / Brave) ----

    private void CollectChromium(List<BrowserTarget> targets, string browserName,
                                 string userDataPath, BrowserCategory categories)
    {
        if (!Directory.Exists(userDataPath)) return;

        foreach (var profileDir in EnumerateChromiumProfiles(userDataPath))
        {
            string profileName = Path.GetFileName(profileDir);

            if (categories.HasFlag(BrowserCategory.Cache))
            {
                // Übliche Cache-Verzeichnisse eines Chromium-Profils.
                string[] cacheDirs =
                {
                    "Cache",
                    Path.Combine("Code Cache", "js"),
                    Path.Combine("Code Cache", "wasm"),
                    "GPUCache",
                    "Media Cache",
                    "DawnCache",
                    "DawnGraphiteCache",
                    "DawnWebGPUCache"
                };
                AddTarget(targets, browserName, profileName, BrowserCategory.Cache,
                    cacheDirs.Select(d => Path.Combine(profileDir, d)));
            }

            if (categories.HasFlag(BrowserCategory.Cookies))
            {
                AddTarget(targets, browserName, profileName, BrowserCategory.Cookies, new[]
                {
                    Path.Combine(profileDir, "Network", "Cookies"),
                    Path.Combine(profileDir, "Network", "Cookies-journal"),
                    Path.Combine(profileDir, "Cookies"),
                    Path.Combine(profileDir, "Cookies-journal")
                });
            }

            if (categories.HasFlag(BrowserCategory.History))
            {
                AddTarget(targets, browserName, profileName, BrowserCategory.History, new[]
                {
                    Path.Combine(profileDir, "History"),
                    Path.Combine(profileDir, "History-journal"),
                    Path.Combine(profileDir, "Visited Links"),
                    Path.Combine(profileDir, "Top Sites"),
                    Path.Combine(profileDir, "Top Sites-journal")
                });
            }

            if (categories.HasFlag(BrowserCategory.Sessions))
            {
                AddTarget(targets, browserName, profileName, BrowserCategory.Sessions, new[]
                {
                    Path.Combine(profileDir, "Sessions"),
                    Path.Combine(profileDir, "Session Storage"),
                    Path.Combine(profileDir, "Current Session"),
                    Path.Combine(profileDir, "Current Tabs"),
                    Path.Combine(profileDir, "Last Session"),
                    Path.Combine(profileDir, "Last Tabs")
                });
            }
        }
    }

    /// <summary>
    /// Liefert die echten Profil-Ordner unterhalb von <c>User Data</c>
    /// ("Default", "Profile 1", "Guest Profile" …) und filtert Service-Ordner
    /// wie "System Profile", "Crashpad" oder Komponenten-Caches heraus.
    /// </summary>
    private static IEnumerable<string> EnumerateChromiumProfiles(string userDataPath)
    {
        IEnumerable<string> dirs;
        try { dirs = Directory.EnumerateDirectories(userDataPath, "*", SearchOption.TopDirectoryOnly); }
        catch { yield break; }

        foreach (var dir in dirs)
        {
            string name = Path.GetFileName(dir);
            bool isProfile = name.Equals("Default", StringComparison.OrdinalIgnoreCase)
                          || name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase)
                          || name.Equals("Guest Profile", StringComparison.OrdinalIgnoreCase);
            if (isProfile) yield return dir;
        }
    }

    // ---- Firefox ----

    private void CollectFirefox(List<BrowserTarget> targets, string roamingProfilesRoot,
                                string localProfilesRoot, BrowserCategory categories)
    {
        // Roaming-Profile sind maßgeblich (cookies.sqlite, places.sqlite, sessionstore*).
        // Der Cache liegt unter LOCALAPPDATA im gleichnamigen Profilordner.
        if (!Directory.Exists(roamingProfilesRoot) && !Directory.Exists(localProfilesRoot)) return;

        IEnumerable<string> roamingProfiles;
        try
        {
            roamingProfiles = Directory.Exists(roamingProfilesRoot)
                ? Directory.EnumerateDirectories(roamingProfilesRoot, "*", SearchOption.TopDirectoryOnly)
                : Enumerable.Empty<string>();
        }
        catch { roamingProfiles = Enumerable.Empty<string>(); }

        foreach (var profileDir in roamingProfiles)
        {
            string profileName = Path.GetFileName(profileDir);
            string localProfileDir = Path.Combine(localProfilesRoot, profileName);

            if (categories.HasFlag(BrowserCategory.Cache))
            {
                // cache2 liegt unter LOCALAPPDATA; ältere Builds auch im Roaming-Profil.
                AddTarget(targets, "Firefox", profileName, BrowserCategory.Cache, new[]
                {
                    Path.Combine(localProfileDir, "cache2"),
                    Path.Combine(localProfileDir, "startupCache"),
                    Path.Combine(profileDir, "cache2")
                });
            }

            if (categories.HasFlag(BrowserCategory.Cookies))
            {
                AddTarget(targets, "Firefox", profileName, BrowserCategory.Cookies, new[]
                {
                    Path.Combine(profileDir, "cookies.sqlite"),
                    Path.Combine(profileDir, "cookies.sqlite-wal"),
                    Path.Combine(profileDir, "cookies.sqlite-shm")
                });
            }

            if (categories.HasFlag(BrowserCategory.History))
            {
                AddTarget(targets, "Firefox", profileName, BrowserCategory.History, new[]
                {
                    Path.Combine(profileDir, "places.sqlite"),
                    Path.Combine(profileDir, "places.sqlite-wal"),
                    Path.Combine(profileDir, "places.sqlite-shm")
                });
            }

            if (categories.HasFlag(BrowserCategory.Sessions))
            {
                AddTarget(targets, "Firefox", profileName, BrowserCategory.Sessions, new[]
                {
                    Path.Combine(profileDir, "sessionstore.jsonlz4"),
                    Path.Combine(profileDir, "sessionstore-backups"),
                    Path.Combine(profileDir, "sessionCheckpoints.json")
                });
            }
        }
    }

    // ---- gemeinsame Helfer ----

    /// <summary>
    /// Misst die angegebenen Pfade (Datei oder Ordner), behält nur die mit Inhalt
    /// und fügt – sofern insgesamt etwas zusammenkommt – ein <see cref="BrowserTarget"/>
    /// hinzu.
    /// </summary>
    private static void AddTarget(List<BrowserTarget> targets, string browser, string profile,
                                  BrowserCategory category, IEnumerable<string> candidatePaths)
    {
        long totalBytes = 0;
        int totalFiles = 0;
        var existing = new List<string>();

        foreach (var path in candidatePaths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    var (bytes, files) = MeasureFolder(path);
                    if (files > 0)
                    {
                        totalBytes += bytes;
                        totalFiles += files;
                        existing.Add(path);
                    }
                }
                else if (File.Exists(path))
                {
                    long len = new FileInfo(path).Length;
                    totalBytes += len;
                    totalFiles += 1;
                    existing.Add(path);
                }
            }
            catch
            {
                // gesperrt/unzugänglich -> still überspringen
            }
        }

        if (existing.Count > 0 && totalFiles > 0)
            targets.Add(new BrowserTarget(browser, profile, category, existing, totalBytes, totalFiles));
    }

    /// <summary>
    /// Summiert Größe und Dateizahl rekursiv unterhalb <paramref name="folder"/>
    /// (ohne Reparse Points). Gesperrte Dateien werden übersprungen.
    /// </summary>
    private static (long bytes, int files) MeasureFolder(string folder)
    {
        long bytes = 0;
        int files = 0;
        if (!Directory.Exists(folder)) return (0, 0);

        foreach (var file in Directory.EnumerateFiles(folder, "*", DeepOpts))
        {
            try { bytes += new FileInfo(file).Length; files++; }
            catch { /* gesperrt/unzugänglich */ }
        }
        return (bytes, files);
    }
}
