using System.Diagnostics;

namespace WinCleaner.SystemTools;

/// <summary>
/// Legt eine geplante Aufgabe an, die die Privacy-Tweaks regelmäßig neu
/// anwendet (<c>privacy --apply &lt;Profil&gt; --no-dry-run --yes</c>).
/// Hintergrund: Windows-Feature-Updates setzen einzelne Telemetrie-/KI-Schalter
/// gern wieder zurück; der geplante Reapply hält den gewünschten Zustand
/// dauerhaft aufrecht. Alle Tweaks bleiben über <c>privacy --undo</c> umkehrbar.
/// </summary>
public sealed class PrivacyScheduler
{
    private const string TaskName = "WinCleaner Privacy-Reapply";

    private readonly Core.Logger _logger;
    public PrivacyScheduler(Core.Logger logger) => _logger = logger;

    public bool Schedule(string interval, PrivacyProfile profile)
    {
        string schedule = TaskSchedulerHelper.ScheduleSwitch(interval);
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

        return RunSchtasks(BuildCreateArgs(schedule, exePath, profile),
            $"Geplanter Privacy-Reapply ({interval}, Profil {profile}) um 05:00 Uhr eingerichtet.");
    }

    public bool Unschedule()
        => RunSchtasks($"/Delete /TN \"{TaskName}\" /F", "Geplanter Privacy-Reapply entfernt.");

    /// <summary>
    /// Baut die vollständigen schtasks-/Create-Argumente. --yes und --no-dry-run
    /// sind Pflicht, weil der geplante Lauf keine interaktive Konsole hat; ohne
    /// sie würde der Lauf im Trockenlauf enden bzw. an der Bestätigung hängen.
    /// Innere Anführungszeichen werden escaped, da /TR selbst in Anführungszeichen steht.
    /// </summary>
    internal static string BuildCreateArgs(string scheduleSwitch, string exePath, PrivacyProfile profile)
    {
        string profileArg = profile == PrivacyProfile.Advanced ? "advanced" : "standard";
        string action = $"\\\"{exePath}\\\" privacy --apply {profileArg} --no-dry-run --yes";
        return $"/Create /TN \"{TaskName}\" /TR \"{action}\" {scheduleSwitch} /ST 05:00 /F";
    }

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
