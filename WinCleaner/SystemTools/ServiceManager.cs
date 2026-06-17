using Microsoft.Win32;
using WinCleaner.Core;

namespace WinCleaner.SystemTools;

/// <summary>Start-Typ eines Windows-Dienstes (entspricht dem Registry-Wert "Start").</summary>
public enum ServiceStartType
{
    /// <summary>Boot-Treiber (0) – wird sehr früh geladen.</summary>
    Boot = 0,
    /// <summary>System-Treiber (1).</summary>
    System = 1,
    /// <summary>Automatisch (2) – startet beim Hochfahren.</summary>
    Automatic = 2,
    /// <summary>Manuell (3) – startet nur bei Bedarf.</summary>
    Manual = 3,
    /// <summary>Deaktiviert (4) – startet nicht.</summary>
    Disabled = 4,
    /// <summary>Unbekannt / nicht lesbar.</summary>
    Unknown = -1
}

/// <summary>Liest die wichtigsten Eckdaten eines Windows-Dienstes.</summary>
/// <param name="Name">Interner Dienstname (Schlüsselname unter ...\Services).</param>
/// <param name="DisplayName">Anzeigename (oder Fallback auf den internen Namen).</param>
/// <param name="StartType">Aktueller Start-Typ.</param>
public sealed record ServiceInfo(string Name, string DisplayName, ServiceStartType StartType);

/// <summary>
/// Liest und ändert den Start-Typ von Windows-Diensten direkt über die Registry
/// (<c>HKLM\SYSTEM\CurrentControlSet\Services\&lt;Name&gt;</c>, Wert <c>Start</c>).
/// Das Ändern erfolgt bewusst REVERSIBEL über die <see cref="TweakEngine"/>: vor
/// jeder Änderung wird der vorherige Wert als JSON gesichert, sodass
/// <see cref="UndoStartType"/> den Ausgangszustand exakt wiederherstellt.
/// Schreibzugriffe auf HKLM erfordern Adminrechte (Aufrufer kann via
/// <see cref="Elevation"/> eleviert neu starten).
/// </summary>
public sealed class ServiceManager
{
    private const string ServicesRoot = @"SYSTEM\CurrentControlSet\Services";

    private readonly Logger _logger;
    private readonly TweakEngine _tweaks;

    public ServiceManager(Logger logger)
    {
        _logger = logger;
        _tweaks = new TweakEngine(logger);
    }

    /// <summary>
    /// Liefert alle echten Dienste (mit gültigem Start-Wert) sortiert nach
    /// Anzeigename. Reine Kernel-Treiber ohne lesbaren Start-Wert werden
    /// übersprungen; unzugängliche Schlüssel still ignoriert.
    /// </summary>
    public List<ServiceInfo> List()
    {
        var result = new List<ServiceInfo>();
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(ServicesRoot);
            if (root is null)
            {
                _logger.Error("Dienste-Registry nicht lesbar (HKLM\\SYSTEM\\CurrentControlSet\\Services).");
                return result;
            }

            foreach (var name in root.GetSubKeyNames())
            {
                var info = ReadInfo(name);
                if (info is not null && info.StartType is not ServiceStartType.Unknown)
                    result.Add(info);
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Dienste konnten nicht aufgelistet werden: {ex.Message}");
        }

        return result.OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Liest die Eckdaten eines einzelnen Dienstes, oder null wenn der Dienst
    /// nicht existiert bzw. nicht lesbar ist.
    /// </summary>
    public ServiceInfo? ReadInfo(string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"{ServicesRoot}\{name}");
            if (key is null) return null;

            var raw = key.GetValue("Start");
            ServiceStartType start = raw is null
                ? ServiceStartType.Unknown
                : ParseStart(raw);

            string display = key.GetValue("DisplayName") as string ?? name;
            // Manche DisplayName-Werte sind MUI-Ressourcen (@dll,-123); dann lieber den Namen zeigen.
            if (display.StartsWith("@", StringComparison.Ordinal)) display = name;

            return new ServiceInfo(name, display, start);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Existiert ein Dienst mit diesem Namen?</summary>
    public bool Exists(string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"{ServicesRoot}\{name}");
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Setzt den Start-Typ eines Dienstes REVERSIBEL (mit Sicherung des
    /// Vorzustands). True bei Erfolg. Schreibt nach HKLM -> Adminrechte nötig.
    /// </summary>
    public bool SetStartType(string name, ServiceStartType type)
    {
        if (type is not (ServiceStartType.Automatic or ServiceStartType.Manual or ServiceStartType.Disabled))
        {
            _logger.Error($"Nicht unterstützter Start-Typ für \"{name}\": {type}.");
            return false;
        }

        if (!Exists(name))
        {
            _logger.Error($"Dienst nicht gefunden: \"{name}\".");
            return false;
        }

        return _tweaks.Apply(BuildTweak(name, type));
    }

    /// <summary>
    /// Macht eine zuvor mit WinCleaner gesetzte Start-Typ-Änderung anhand der
    /// gespeicherten Sicherung rückgängig. True bei Erfolg.
    /// </summary>
    public bool UndoStartType(string name)
    {
        if (!_tweaks.HasBackup(TweakId(name)))
        {
            _logger.Error($"Keine Sicherung für Dienst \"{name}\" – nichts rückgängig zu machen.");
            return false;
        }

        return _tweaks.Undo(TweakId(name), RegistryHive.LocalMachine,
            $@"{ServicesRoot}\{name}", "Start");
    }

    /// <summary>True, wenn für diesen Dienst eine WinCleaner-Sicherung existiert.</summary>
    public bool HasBackup(string name) => _tweaks.HasBackup(TweakId(name));

    /// <summary>Mensch-lesbare Beschriftung eines Start-Typs (deutsch).</summary>
    public static string Describe(ServiceStartType type) => type switch
    {
        ServiceStartType.Boot      => "Boot",
        ServiceStartType.System    => "System",
        ServiceStartType.Automatic => "Automatisch",
        ServiceStartType.Manual    => "Manuell",
        ServiceStartType.Disabled  => "Deaktiviert",
        _                          => "Unbekannt"
    };

    /// <summary>Wandelt eine Nutzer-Eingabe (manual|disabled|auto) in einen Start-Typ.</summary>
    public static ServiceStartType? ParseRequested(string value) => value.ToLowerInvariant() switch
    {
        "manual" or "manuell"          => ServiceStartType.Manual,
        "disabled" or "deaktiviert"    => ServiceStartType.Disabled,
        "auto" or "automatic" or "automatisch" => ServiceStartType.Automatic,
        _ => null
    };

    // ---- intern ----

    private static ServiceStartType ParseStart(object raw)
    {
        try
        {
            int v = Convert.ToInt32(raw);
            return Enum.IsDefined(typeof(ServiceStartType), v)
                ? (ServiceStartType)v
                : ServiceStartType.Unknown;
        }
        catch
        {
            return ServiceStartType.Unknown;
        }
    }

    private static string TweakId(string name) => $"services.{name.ToLowerInvariant()}.start";

    private static RegistryTweak BuildTweak(string name, ServiceStartType type) => new(
        Id:          TweakId(name),
        Description: $"Start-Typ von Dienst \"{name}\" auf {Describe(type)} gesetzt",
        Hive:        RegistryHive.LocalMachine,
        SubKey:      $@"{ServicesRoot}\{name}",
        ValueName:   "Start",
        Kind:        RegistryValueKind.DWord,
        EnabledValue: (int)type);
}
