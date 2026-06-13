using System.Management;

namespace WinCleaner.SystemTools;

/// <summary>
/// Erstellt einen System-Wiederherstellungspunkt über die WMI-Klasse
/// <c>SystemRestore</c> (Namespace <c>root\default</c>). Erfordert Adminrechte
/// und aktivierten Systemschutz; Windows drosselt die Erstellung standardmäßig
/// auf einen Punkt pro 24 Stunden.
/// </summary>
public class RestorePoint
{
    // RestorePointType / EventType laut SystemRestore-API
    private const int MODIFY_SETTINGS     = 12;
    private const int BEGIN_SYSTEM_CHANGE = 100;

    private readonly Core.Logger _logger;
    public RestorePoint(Core.Logger logger) => _logger = logger;

    public bool Create(string name)
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\default");
            scope.Connect();

            using var systemRestore = new ManagementClass(scope,
                new ManagementPath("SystemRestore"), new ObjectGetOptions());

            using var inParams = systemRestore.GetMethodParameters("CreateRestorePoint");
            inParams["Description"]      = name;
            inParams["RestorePointType"] = MODIFY_SETTINGS;
            inParams["EventType"]        = BEGIN_SYSTEM_CHANGE;

            using var outParams = systemRestore.InvokeMethod("CreateRestorePoint", inParams, null);
            uint rc = Convert.ToUInt32(outParams?["ReturnValue"] ?? 1u);

            if (rc == 0)
            {
                _logger.Info($"Wiederherstellungspunkt erstellt: \"{name}\".");
                return true;
            }

            _logger.Error($"CreateRestorePoint Fehlercode {rc} – {DescribeError(rc)}");
            return false;
        }
        catch (ManagementException ex)
        {
            _logger.Error($"WMI-Fehler bei Wiederherstellungspunkt: {ex.Message}");
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            _logger.Error("Adminrechte erforderlich, um einen Wiederherstellungspunkt zu erstellen.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error($"Unerwarteter Fehler: {ex.Message}");
            _logger.Debug(ex.ToString());
            return false;
        }
    }

    private static string DescribeError(uint rc) => rc switch
    {
        1058 => "Dienst deaktiviert / Systemschutz aus.",
        1359 => "Interner Fehler.",
        // Häufig bei Drosselung: ein zweiter Punkt innerhalb der Sperrfrist (24 h).
        _    => "Systemschutz aktiv? Adminrechte? Bereits ein Punkt in den letzten 24 h?"
    };
}
