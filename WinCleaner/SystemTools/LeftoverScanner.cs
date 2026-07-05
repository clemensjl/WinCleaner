using System.Diagnostics;
using Microsoft.Win32;
using WinCleaner.Core;

namespace WinCleaner.SystemTools;

/// <summary>Ein Windows-Dienst, dessen Programmdatei im Installationsordner eines (de)installierten Programms liegt.</summary>
public sealed record ServiceLeftover(string Name, string? DisplayName, string ImagePath);

/// <summary>Eine geplante Aufgabe, deren Aktion auf den Installationsordner eines (de)installierten Programms zeigt.</summary>
public sealed record TaskLeftover(string TaskName);

/// <summary>
/// Findet Reste jenseits von Dateien/Ordnern: Windows-Dienste und geplante
/// Aufgaben, die noch auf den Installationsordner eines deinstallierten
/// Programms verweisen. REINE SUCHE – es wird nichts entfernt; die Ausgabe
/// nennt nur die passenden Bordmittel-Befehle (<c>sc delete</c>,
/// <c>schtasks /Delete</c>), denn das Entfernen von Diensten/Tasks ist nicht
/// umkehrbar und bleibt bewusst eine manuelle Entscheidung.
/// </summary>
public static class LeftoverScanner
{
    /// <summary>
    /// Durchsucht die Dienste-Registrierung (HKLM\SYSTEM\CurrentControlSet\Services)
    /// nach Diensten, deren ImagePath unterhalb von <paramref name="installLocation"/> liegt.
    /// </summary>
    public static List<ServiceLeftover> FindServiceLeftovers(string? installLocation, Logger logger)
    {
        var found = new List<ServiceLeftover>();
        if (string.IsNullOrWhiteSpace(installLocation)) return found;

        try
        {
            using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (services is null) return found;

            foreach (var name in services.GetSubKeyNames())
            {
                try
                {
                    using var svc = services.OpenSubKey(name);
                    var imagePath = svc?.GetValue("ImagePath") as string;
                    if (!PathReferences(imagePath, installLocation)) continue;

                    var display = svc?.GetValue("DisplayName") as string;
                    found.Add(new ServiceLeftover(name, display, imagePath!));
                }
                catch { /* einzelner Dienst nicht lesbar -> überspringen */ }
            }
        }
        catch (Exception ex)
        {
            logger.Debug($"Dienste-Scan fehlgeschlagen: {ex.Message}");
        }
        return found;
    }

    /// <summary>
    /// Durchsucht die geplanten Aufgaben (via <c>schtasks /Query /V /FO CSV</c>)
    /// nach Aufgaben, deren Zeilen den Installationsordner referenzieren.
    /// </summary>
    public static List<TaskLeftover> FindTaskLeftovers(string? installLocation, Logger logger)
    {
        if (string.IsNullOrWhiteSpace(installLocation)) return new();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = "schtasks.exe",
                Arguments              = "/Query /V /FO CSV",
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return new();

            string stdout = proc.StandardOutput.ReadToEnd();
            proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode != 0) return new();

            return ParseTaskCsv(stdout, installLocation);
        }
        catch (Exception ex)
        {
            logger.Debug($"Task-Scan fehlgeschlagen: {ex.Message}");
            return new();
        }
    }

    /// <summary>
    /// Filtert die CSV-Ausgabe von <c>schtasks /Query /V /FO CSV</c> nach Zeilen,
    /// die <paramref name="installLocation"/> referenzieren. Bewusst
    /// locale-unabhängig: statt auf (übersetzte) Spaltenüberschriften zu bauen,
    /// wird der Aufgabenname positional gelesen (Spalte 2, beginnt mit "\") und
    /// der Pfad in ALLEN Feldern gesucht.
    /// </summary>
    internal static List<TaskLeftover> ParseTaskCsv(string csv, string installLocation)
    {
        var found = new List<TaskLeftover>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in csv.Split('\n'))
        {
            var fields = SplitCsvLine(line.TrimEnd('\r'));
            if (fields.Count < 2) continue;

            // Aufgabennamen beginnen mit "\" – das filtert Kopfzeilen
            // (jede Sprache) und Leerzeilen zuverlässig heraus.
            string taskName = fields[1];
            if (!taskName.StartsWith('\\')) continue;

            if (!fields.Any(f => PathReferences(f, installLocation))) continue;
            if (seen.Add(taskName))
                found.Add(new TaskLeftover(taskName));
        }
        return found;
    }

    /// <summary>
    /// Prüft, ob eine Befehlszeile/ein Feld den Installationsordner referenziert
    /// (Pfad selbst oder etwas darunter; Anführungszeichen tolerant, case-insensitive).
    /// </summary>
    internal static bool PathReferences(string? text, string installLocation)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(installLocation))
            return false;

        string root = installLocation.Trim().Trim('"');
        root = Path.TrimEndingDirectorySeparator(root);
        if (root.Length < 4) return false; // "C:\" o. ä. wäre ein Alles-Treffer

        int idx = text.IndexOf(root, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return false;

        // Nach dem Treffer muss der Pfad enden oder mit einem Trenner weitergehen,
        // sonst matcht "C:\Tools\App" fälschlich auch "C:\Tools\App2".
        int end = idx + root.Length;
        if (end >= text.Length) return true;
        char next = text[end];
        return next is '\\' or '/' or '"' or ' ';
    }

    /// <summary>
    /// Zerlegt eine CSV-Zeile im schtasks-Format (alle Felder in
    /// Anführungszeichen, Komma-getrennt, "" als escaptes Anführungszeichen).
    /// </summary>
    internal static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        if (string.IsNullOrEmpty(line)) return fields;

        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else inQuotes = false;
                }
                else current.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { fields.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        fields.Add(current.ToString());
        return fields;
    }
}
