using System.Diagnostics;
using System.IO;
using System.Text;

namespace WinCleaner.Gui.Services;

/// <summary>
/// Führt Admin-pflichtige Aktionen aus, indem die gehärtete Kommandozeilen-EXE
/// (<c>WinCleaner.exe</c>) per UAC (<c>ShellExecute runas</c>) gestartet wird.
/// So bleibt die GUI selbst ohne Adminrechte, und die geprüfte
/// Elevation-/Restore-Logik der CLI wird wiederverwendet.
/// </summary>
public static class ElevatedCli
{
    /// <summary>Ergebnis eines elevierten Laufs.</summary>
    public sealed record Result(bool Started, int ExitCode, string? Error)
    {
        public bool Success => Started && ExitCode == 0;
    }

    /// <summary>
    /// Sucht die WinCleaner.exe: zuerst neben der GUI, dann im Standard-
    /// Installationsordner, sonst Verlass auf den PATH.
    /// </summary>
    public static string ResolveCliPath()
    {
        string beside = Path.Combine(AppContext.BaseDirectory, "WinCleaner.exe");
        if (File.Exists(beside)) return beside;

        string installed = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "WinCleaner", "WinCleaner.exe");
        if (File.Exists(installed)) return installed;

        return "WinCleaner.exe"; // auf dem PATH (Standard-Installation)
    }

    /// <summary>
    /// Startet <c>WinCleaner.exe</c> mit den Argumenten unter UAC und wartet auf
    /// das Ende. Die Argumente werden vom Aufrufer bereits fertig (inkl.
    /// nötiger Quotes) übergeben.
    /// </summary>
    public static Result Run(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName        = ResolveCliPath(),
                Arguments       = arguments,
                UseShellExecute = true,   // nötig für "runas"
                Verb            = "runas" // UAC-Abfrage
            };

            using var proc = Process.Start(psi);
            if (proc is null) return new Result(false, -1, "Prozess konnte nicht gestartet werden.");

            proc.WaitForExit();
            return new Result(true, proc.ExitCode, null);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // 1223 = Nutzer hat die UAC-Abfrage abgelehnt.
            string msg = ex.NativeErrorCode == 1223
                ? "Vom Nutzer abgebrochen (keine Adminrechte erteilt)."
                : ex.Message;
            return new Result(false, -1, msg);
        }
        catch (Exception ex)
        {
            return new Result(false, -1, ex.Message);
        }
    }

    /// <summary>
    /// Setzt einen einzelnen Kommandozeilen-Parameter so in Anführungszeichen, dass
    /// ihn <c>CommandLineToArgvW</c> (das WinCleaner.exe zum Parsen nutzt) exakt als
    /// EIN Argument zurückliefert. Escapt eingebettete Anführungszeichen und
    /// Backslash-Läufe korrekt – nötig, weil ein Pfad wie <c>C:\Alte Daten\</c>
    /// sonst mit <c>\"</c> das schließende Anführungszeichen entwerten würde
    /// (bei irreversiblen Aktionen wie shred gefährlich).
    /// </summary>
    public static string Quote(string value)
    {
        // Keine Sonderzeichen -> unverändert (leere Zeichenkette wird zu "").
        if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
            return value;

        var sb = new StringBuilder();
        sb.Append('"');
        for (int i = 0; i < value.Length; i++)
        {
            int backslashes = 0;
            while (i < value.Length && value[i] == '\\') { backslashes++; i++; }

            if (i == value.Length)
            {
                // Backslashes vor dem schließenden Anführungszeichen verdoppeln.
                sb.Append('\\', backslashes * 2);
                break;
            }
            if (value[i] == '"')
            {
                // Backslashes verdoppeln + Anführungszeichen escapen.
                sb.Append('\\', backslashes * 2 + 1);
                sb.Append('"');
            }
            else
            {
                sb.Append('\\', backslashes);
                sb.Append(value[i]);
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
