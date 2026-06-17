using System.Diagnostics;

namespace WinCleaner.SystemTools;

/// <summary>
/// Legt über <c>schtasks.exe</c> eine geplante Aufgabe an, die WinCleaner
/// regelmäßig im Echtbetrieb ausführt (<c>clean-junk --no-dry-run</c>).
/// </summary>
public class TaskSchedulerHelper
{
    private const string TaskName = "WinCleaner Auto-Clean";

    private readonly Core.Logger _logger;
    public TaskSchedulerHelper(Core.Logger logger) => _logger = logger;

    public bool CreateScheduledClean(string interval)
    {
        string schedule = ScheduleSwitch(interval);
        if (schedule.Length == 0)
        {
            _logger.Error($"Unbekanntes Intervall \"{interval}\". Erlaubt: daily | weekly.");
            return false;
        }

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            _logger.Error("Pfad zur WinCleaner-Programmdatei nicht ermittelbar.");
            return false;
        }

        return RunSchtasks(BuildCreateArgs(schedule, exePath),
            $"Geplante Bereinigung ({interval}) um 03:00 Uhr eingerichtet.");
    }

    /// <summary>schtasks-Schalter für das Intervall, oder "" bei unbekanntem Wert.</summary>
    internal static string ScheduleSwitch(string interval) => interval.ToLowerInvariant() switch
    {
        "daily"  or "täglich"     => "/SC DAILY",
        "weekly" or "wöchentlich" => "/SC WEEKLY /D SUN",
        _ => ""
    };

    /// <summary>
    /// Baut die vollständigen schtasks-/Create-Argumente. --yes ist Pflicht, weil der
    /// geplante Lauf keine interaktive Konsole hat; ohne --yes würde die
    /// Bestätigungsabfrage in clean-junk den Lauf still abbrechen. Innere
    /// Anführungszeichen werden escaped, da /TR selbst in Anführungszeichen steht.
    /// </summary>
    internal static string BuildCreateArgs(string scheduleSwitch, string exePath)
    {
        string action = $"\\\"{exePath}\\\" clean-junk --no-dry-run --yes";
        return $"/Create /TN \"{TaskName}\" /TR \"{action}\" {scheduleSwitch} /ST 03:00 /F";
    }

    public bool RemoveScheduledClean()
        => RunSchtasks($"/Delete /TN \"{TaskName}\" /F", "Geplante Bereinigung entfernt.");

    private bool RunSchtasks(string args, string successMessage)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = "schtasks.exe",
                Arguments              = args,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                _logger.Error("schtasks.exe konnte nicht gestartet werden.");
                return false;
            }

            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            if (proc.ExitCode == 0)
            {
                _logger.Info(successMessage);
                return true;
            }

            var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            _logger.Error($"schtasks fehlgeschlagen (Code {proc.ExitCode}): {detail.Trim()}");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error($"Fehler beim Aufruf von schtasks: {ex.Message}");
            return false;
        }
    }
}
