using System.Text.Json;
using Microsoft.Win32;

namespace WinCleaner.SystemTools;

/// <summary>Ein reversibler Registry-Tweak (z. B. ein Telemetrie-Schalter).</summary>
/// <param name="Id">Stabiler, eindeutiger Schlüssel (für Sicherung/Undo), z. B. "telemetry.allow-telemetry".</param>
/// <param name="Description">Menschlich lesbare Beschreibung für Ausgabe/Hilfe.</param>
/// <param name="EnabledValue">Wert, der den Tweak "aktiv" macht (z. B. 0 = Telemetrie aus).</param>
public sealed record RegistryTweak(
    string Id,
    string Description,
    RegistryHive Hive,
    string SubKey,
    string ValueName,
    RegistryValueKind Kind,
    object EnabledValue);

public enum TweakStatus { Applied, NotApplied, Unknown }

/// <summary>
/// Wendet reversible Registry-Tweaks an und macht sie rückgängig. Vor der ersten
/// Änderung wird der vorherige Zustand (Wert + Art, oder "nicht vorhanden") als
/// JSON unter <c>%LOCALAPPDATA%\WinCleaner\tweaks</c> gesichert, damit
/// <see cref="Undo(RegistryTweak)"/> exakt den Ausgangszustand wiederherstellt.
/// HKLM-Tweaks erfordern Adminrechte (Aufrufer kann via <see cref="Elevation"/>
/// neu starten). Dies ist das gemeinsame Fundament für Privacy-, Debloat- und
/// Dienste-Tweaks (kuratiert + reversibel + skriptbar).
/// </summary>
public sealed class TweakEngine
{
    private readonly Core.Logger _logger;
    private readonly string _backupDir;

    public TweakEngine(Core.Logger logger)
    {
        _logger = logger;
        _backupDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinCleaner", "tweaks");
        Directory.CreateDirectory(_backupDir);
    }

    private static RegistryKey BaseKey(RegistryHive hive) => hive switch
    {
        RegistryHive.CurrentUser   => Registry.CurrentUser,
        RegistryHive.LocalMachine  => Registry.LocalMachine,
        RegistryHive.ClassesRoot   => Registry.ClassesRoot,
        RegistryHive.Users         => Registry.Users,
        RegistryHive.CurrentConfig => Registry.CurrentConfig,
        _ => throw new ArgumentOutOfRangeException(nameof(hive), hive, "Nicht unterstützte Registry-Hive.")
    };

    /// <summary>Ist der Tweak aktuell angewendet (aktueller Wert == EnabledValue)?</summary>
    public TweakStatus Status(RegistryTweak t)
    {
        try
        {
            using var key = BaseKey(t.Hive).OpenSubKey(t.SubKey);
            var cur = key?.GetValue(t.ValueName);
            if (cur is null) return TweakStatus.NotApplied;
            return string.Equals(cur.ToString(), t.EnabledValue.ToString(), StringComparison.Ordinal)
                ? TweakStatus.Applied
                : TweakStatus.NotApplied;
        }
        catch
        {
            return TweakStatus.Unknown;
        }
    }

    /// <summary>Wendet den Tweak an (mit Sicherung des Vorzustands). True bei Erfolg.</summary>
    public bool Apply(RegistryTweak t)
    {
        try
        {
            BackupCurrent(t);
            using var key = BaseKey(t.Hive).CreateSubKey(t.SubKey, writable: true)
                ?? throw new InvalidOperationException($"Schlüssel nicht beschreibbar: {t.SubKey}");
            key.SetValue(t.ValueName, t.EnabledValue, t.Kind);
            _logger.Info($"Tweak angewendet: {t.Id} – {t.Description}");
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            _logger.Error($"Adminrechte nötig für Tweak {t.Id} ({t.Hive}).");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error($"Tweak {t.Id} fehlgeschlagen: {ex.Message}");
            return false;
        }
    }

    /// <summary>Macht den Tweak anhand der gespeicherten Sicherung rückgängig.</summary>
    public bool Undo(RegistryTweak t) => Undo(t.Id, t.Hive, t.SubKey, t.ValueName);

    public bool Undo(string id, RegistryHive hive, string subKey, string valueName)
    {
        var file = BackupFile(id);
        if (!File.Exists(file))
        {
            _logger.Error($"Keine Sicherung für Tweak {id} – Undo nicht möglich.");
            return false;
        }

        try
        {
            var b = JsonSerializer.Deserialize<Backup>(File.ReadAllText(file))
                    ?? throw new InvalidOperationException("Sicherung leer/ungültig.");
            using var key = BaseKey(hive).CreateSubKey(subKey, writable: true)!;

            if (!b.Existed)
            {
                key.DeleteValue(valueName, throwOnMissingValue: false);
            }
            else
            {
                var kind = Enum.Parse<RegistryValueKind>(b.Kind!);
                object value = kind switch
                {
                    RegistryValueKind.DWord  => int.Parse(b.Value!),
                    RegistryValueKind.QWord  => long.Parse(b.Value!),
                    RegistryValueKind.Binary => Convert.FromBase64String(b.Value!),
                    _ => b.Value!
                };
                key.SetValue(valueName, value, kind);
            }

            File.Delete(file);
            _logger.Info($"Tweak rückgängig gemacht: {id}");
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            _logger.Error($"Adminrechte nötig zum Rückgängigmachen von {id} ({hive}).");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error($"Undo {id} fehlgeschlagen: {ex.Message}");
            return false;
        }
    }

    /// <summary>True, wenn für die Id eine Sicherung existiert (Tweak wurde von WinCleaner gesetzt).</summary>
    public bool HasBackup(string id) => File.Exists(BackupFile(id));

    private void BackupCurrent(RegistryTweak t)
    {
        var file = BackupFile(t.Id);
        if (File.Exists(file)) return; // bereits gesichert -> Ausgangszustand bewahren

        using var key = BaseKey(t.Hive).OpenSubKey(t.SubKey);
        var cur = key?.GetValue(t.ValueName);

        Backup b;
        if (cur is null)
        {
            b = new Backup(t.Id, false, null, null, DateTime.UtcNow);
        }
        else
        {
            var kind = key!.GetValueKind(t.ValueName);
            string val = kind == RegistryValueKind.Binary
                ? Convert.ToBase64String((byte[])cur)
                : cur.ToString() ?? "";
            b = new Backup(t.Id, true, kind.ToString(), val, DateTime.UtcNow);
        }

        File.WriteAllText(file, JsonSerializer.Serialize(b));
    }

    private string BackupFile(string id)
    {
        var safe = string.Concat(id.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        return Path.Combine(_backupDir, safe + ".json");
    }

    private sealed record Backup(string Id, bool Existed, string? Kind, string? Value, DateTime When);
}
