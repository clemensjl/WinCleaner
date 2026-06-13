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
        string schedule = interval.ToLowerInvariant() switch
        {
            "daily"  or "täglich"     => "/SC DAILY",
            "weekly" or "wöchentlich" => "/SC WEEKLY /D SUN",
            _ => ""
        };

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

        // Aktion: WinCleaner mit echter Bereinigung. Innere Anführungszeichen
        // escapen, da /TR selbst in Anführungszeichen steht.
        string action = $"\\\"{exePath}\\\" clean-junk --no-dry-run";
        string args =
            $"/Create /TN \"{TaskName}\" /TR \"{action}\" {schedule} /ST 03:00 /F";

        return RunSchtasks(args, $"Geplante Bereinigung ({interval}) um 03:00 Uhr eingerichtet.");
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
