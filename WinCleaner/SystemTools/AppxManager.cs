using System.Diagnostics;

namespace WinCleaner.SystemTools;

/// <summary>
/// Eine installierte Store-/Appx-App (vorinstalliert oder nachträglich aus dem
/// Store geladen). Identifiziert wird sie über den <see cref="PackageFullName"/>,
/// der für die Entfernung via <c>Remove-AppxPackage</c> nötig ist.
/// </summary>
/// <param name="Name">Kurzer Paketname, z. B. "Microsoft.BingNews".</param>
/// <param name="PackageFullName">Vollständiger Paketname inkl. Version/Architektur.</param>
public sealed record AppxPackage(string Name, string PackageFullName);

/// <summary>
/// Listet installierte Appx-/Store-Apps des aktuellen Benutzers über
/// <c>powershell.exe Get-AppxPackage</c> und entfernt einzelne Pakete per
/// <c>Remove-AppxPackage</c>. Beides läuft per-User; Adminrechte sind für die
/// Auflistung nicht nötig. Die Entfernung ist umkehrbar, da sich entfernte
/// Store-Apps jederzeit erneut aus dem Microsoft Store installieren lassen.
/// </summary>
public sealed class AppxManager
{
    private readonly Core.Logger _logger;

    public AppxManager(Core.Logger logger) => _logger = logger;

    /// <summary>
    /// Liefert alle installierten Appx-Pakete des aktuellen Benutzers
    /// (Name + PackageFullName), alphabetisch nach Name sortiert. Bei Fehlern
    /// wird eine leere Liste zurückgegeben (Diagnose nach stderr).
    /// </summary>
    public List<AppxPackage> ListInstalled()
    {
        var result = new List<AppxPackage>();

        // ConvertTo-Csv liefert robust parsebare, getrennte Felder ohne Tabellen-Layout.
        const string script =
            "Get-AppxPackage | " +
            "Select-Object Name, PackageFullName | " +
            "ConvertTo-Csv -NoTypeInformation";

        var output = RunPowerShell(script, out bool ok);
        if (!ok)
            return result;

        // Erste Zeile ist die CSV-Kopfzeile -> überspringen.
        bool header = true;
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
                continue;
            if (header)
            {
                header = false;
                continue;
            }

            var fields = ParseCsvLine(line);
            if (fields.Count < 2)
                continue;

            var name = fields[0].Trim();
            var full = fields[1].Trim();
            if (name.Length == 0 || full.Length == 0)
                continue;

            result.Add(new AppxPackage(name, full));
        }

        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    /// <summary>
    /// Entfernt ein einzelnes Appx-Paket des aktuellen Benutzers anhand seines
    /// PackageFullName. True bei Erfolg, sonst false (Diagnose nach stderr).
    /// </summary>
    public bool Remove(string packageFullName)
    {
        if (string.IsNullOrWhiteSpace(packageFullName))
        {
            _logger.Error("Leerer PackageFullName – Entfernung übersprungen.");
            return false;
        }

        // Einfachen Quote im Namen für das PowerShell-Single-Quote-Literal escapen.
        var safe = packageFullName.Replace("'", "''");
        string script = $"Remove-AppxPackage -Package '{safe}' -ErrorAction Stop";

        RunPowerShell(script, out bool ok);
        if (ok)
            _logger.Info($"Appx entfernt: {packageFullName}");
        else
            _logger.Error($"Entfernen fehlgeschlagen: {packageFullName}");
        return ok;
    }

    /// <summary>
    /// Führt ein PowerShell-Skript aus und liefert stdout. <paramref name="ok"/>
    /// ist true bei ExitCode 0. Fehlerdetails gehen nach stderr.
    /// </summary>
    private string RunPowerShell(string script, out bool ok)
    {
        ok = false;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = "powershell.exe",
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(script);

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                _logger.Error("powershell.exe konnte nicht gestartet werden.");
                return "";
            }

            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            if (proc.ExitCode == 0)
            {
                ok = true;
                return stdout;
            }

            var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            _logger.Error($"PowerShell fehlgeschlagen (Code {proc.ExitCode}): {detail.Trim()}");
            return stdout;
        }
        catch (Exception ex)
        {
            _logger.Error($"Fehler beim Aufruf von PowerShell: {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// Minimaler CSV-Zeilen-Parser für die von <c>ConvertTo-Csv</c> erzeugten
    /// Felder (doppelte Anführungszeichen als Quoting/Escaping, Komma als Trenner).
    /// </summary>
    internal static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    // Verdoppeltes Anführungszeichen -> ein literales Anführungszeichen.
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        cur.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    cur.Append(c);
                }
            }
            else
            {
                if (c == '"')
                    inQuotes = true;
                else if (c == ',')
                {
                    fields.Add(cur.ToString());
                    cur.Clear();
                }
                else
                    cur.Append(c);
            }
        }

        fields.Add(cur.ToString());
        return fields;
    }
}
