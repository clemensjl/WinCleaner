using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace WinCleaner.SystemTools;

/// <summary>
/// Hilfen zur Rechteerhöhung. Windows kann einen laufenden Prozess nicht
/// nachträglich elevieren; stattdessen startet sich das Programm über
/// ShellExecute mit dem Verb <c>runas</c> selbst neu und löst so den UAC-Dialog
/// aus. Der elevierte Prozess läuft in einem eigenen Fenster.
/// </summary>
public static class Elevation
{
    public const string RelaunchFlag = "--relaunched";

    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    /// <summary>
    /// Startet die aktuelle .exe mit denselben Argumenten elevated neu.
    /// Gibt true zurück, wenn der elevierte Prozess gestartet wurde.
    /// </summary>
    public static bool RelaunchAsAdmin(string[] args, Core.Logger logger)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            logger.Error("Programmpfad für Neustart nicht ermittelbar.");
            return false;
        }

        var psi = new ProcessStartInfo
        {
            FileName        = exe,
            UseShellExecute = true,   // Pflicht für Verb "runas"
            Verb            = "runas"
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add(RelaunchFlag); // markiert Kind-Prozess -> Pause am Ende

        try
        {
            Process.Start(psi);
            return true;
        }
        catch (Win32Exception)
        {
            // Häufigster Fall: User klickt UAC-Dialog weg (Code 1223).
            logger.Error("Rechteerhöhung abgelehnt (UAC abgebrochen).");
            return false;
        }
        catch (Exception ex)
        {
            logger.Error($"Neustart mit Adminrechten fehlgeschlagen: {ex.Message}");
            return false;
        }
    }
}
