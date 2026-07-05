using System.Text.Json;

namespace WinCleaner.Core;

/// <summary>
/// Persistenter Cache für volle SHA-256-Hashes zwischen find-duplicates-Läufen
/// (Opt-in über <c>--cache</c>). Ein Eintrag gilt nur als Treffer, wenn Größe
/// UND letzte Schreibzeit der Datei unverändert sind – sonst wird neu gehasht.
/// Der Cache beschleunigt Wiederholungsläufe über große Bäume massiv, ändert
/// aber nie das Ergebnis: bei jedem Zweifel (Datei geändert, Datei unlesbar,
/// Cache defekt) wird regulär gerechnet.
/// </summary>
public sealed class HashCache
{
    private sealed record Entry(long Size, long MTimeUtcTicks, string Hash);

    private readonly Dictionary<string, Entry> _map;
    private readonly string _path;
    private readonly Logger _logger;
    private bool _dirty;

    /// <summary>Anzahl Cache-Treffer in diesem Lauf (für die Log-Zusammenfassung).</summary>
    public int Hits { get; private set; }

    /// <summary>Standard-Speicherort: %LOCALAPPDATA%\WinCleaner\hash-cache.json.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WinCleaner", "hash-cache.json");

    private HashCache(string path, Dictionary<string, Entry> map, Logger logger)
    {
        _path = path;
        _map = map;
        _logger = logger;
    }

    /// <summary>
    /// Lädt den Cache; eine fehlende oder defekte Datei ergibt einen leeren
    /// Cache (niemals ein Fehler – der Cache ist reine Optimierung).
    /// </summary>
    public static HashCache Load(string path, Logger logger)
    {
        try
        {
            if (File.Exists(path))
            {
                var map = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(path));
                if (map is not null)
                    // Comparer geht beim Deserialisieren verloren -> case-insensitiv neu aufbauen.
                    return new HashCache(path,
                        new Dictionary<string, Entry>(map, StringComparer.OrdinalIgnoreCase), logger);
            }
        }
        catch (Exception ex)
        {
            logger.Debug($"Hash-Cache nicht lesbar ({path}): {ex.Message} – starte leer.");
        }
        return new HashCache(path, new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase), logger);
    }

    /// <summary>
    /// Liefert den gecachten Hash, falls Größe und Schreibzeit exakt passen.
    /// </summary>
    public bool TryGet(string file, long size, DateTime mtimeUtc, out string hash)
    {
        if (_map.TryGetValue(Normalize(file), out var e) &&
            e.Size == size && e.MTimeUtcTicks == mtimeUtc.Ticks)
        {
            hash = e.Hash;
            Hits++;
            return true;
        }
        hash = "";
        return false;
    }

    public void Set(string file, long size, DateTime mtimeUtc, string hash)
    {
        _map[Normalize(file)] = new Entry(size, mtimeUtc.Ticks, hash);
        _dirty = true;
    }

    /// <summary>
    /// Schreibt den Cache zurück (nur wenn geändert). Fehler werden nur
    /// protokolliert – ein nicht speicherbarer Cache darf den Lauf nie scheitern lassen.
    /// </summary>
    public void Save()
    {
        if (!_dirty) return;
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, JsonSerializer.Serialize(_map));
            _dirty = false;
            _logger.Debug($"Hash-Cache gespeichert: {_path} ({_map.Count} Einträge).");
        }
        catch (Exception ex)
        {
            _logger.Debug($"Hash-Cache konnte nicht gespeichert werden ({_path}): {ex.Message}");
        }
    }

    private static string Normalize(string file)
    {
        try { return Path.GetFullPath(file); }
        catch { return file; }
    }
}
