using System.Diagnostics;
using System.Text;

namespace WinCleaner.SystemTools;

/// <summary>
/// Ein verfügbares Paket-Update, geparst aus der Tabellenausgabe von
/// <c>winget upgrade --include-unknown</c>.
/// </summary>
public sealed record WingetUpdate(string Name, string Id, string Current, string Available);

/// <summary>
/// Kapselt Aufrufe von <c>winget.exe</c> (Windows Package Manager) über einen
/// externen Prozess sowie die geplante Auto-Update-Aufgabe via
/// <c>schtasks.exe</c>. Bewusst eine eigenständige Klasse, damit die
/// Befehle (update, list-updates, install, schedule-update) ihre Logik teilen
/// und <see cref="TaskSchedulerHelper"/> unverändert bleibt.
/// </summary>
public sealed class WingetWrapper
{
    /// <summary>Eigener Aufgabenname der geplanten Auto-Update-Aufgabe.</summary>
    private const string TaskName = "WinCleaner Auto-Update";

    private readonly Core.Logger _logger;
    public WingetWrapper(Core.Logger logger) => _logger = logger;

    /// <summary>
    /// Prüft, ob <c>winget</c> verfügbar ist. Liefert false und protokolliert eine
    /// klare deutsche Meldung nach stderr, wenn das Programm nicht gefunden wird
    /// (z. B. fehlender App-Installer auf älteren Windows-Versionen).
    /// </summary>
    public bool IsAvailable()
    {
        try
        {
            var psi = NewPsi("--version");
            using var proc = Process.Start(psi);
            if (proc is null) return false;

            proc.StandardOutput.ReadToEnd();
            proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            return proc.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Protokolliert eine einheitliche Fehlermeldung, wenn winget fehlt.
    /// </summary>
    public void ReportUnavailable()
        => _logger.Error("winget (Windows Package Manager) ist nicht verfügbar. " +
                         "Bitte den \"App-Installer\" aus dem Microsoft Store installieren.");

    /// <summary>
    /// Ermittelt verfügbare Updates über <c>winget upgrade --include-unknown</c>
    /// und parst die Tabellenausgabe in strukturierte Einträge.
    /// </summary>
    public List<WingetUpdate> ListUpdates()
    {
        var result = new List<WingetUpdate>();
        try
        {
            var psi = NewPsi("upgrade --include-unknown --accept-source-agreements");
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                _logger.Error("winget konnte nicht gestartet werden.");
                return result;
            }

            string stdout = proc.StandardOutput.ReadToEnd();
            proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            return ParseUpgradeTable(stdout);
        }
        catch (Exception ex)
        {
            _logger.Error($"Fehler beim Ermitteln der Updates: {ex.Message}");
            return result;
        }
    }

    /// <summary>
    /// Aktualisiert alle Pakete still über
    /// <c>winget upgrade --all --silent --accept-package-agreements --accept-source-agreements</c>.
    /// Gibt true bei Exitcode 0 zurück. Die winget-Ausgabe wird live nach stderr
    /// gespiegelt, damit stdout (bei --json) sauber bleibt.
    /// </summary>
    public bool UpgradeAll()
    {
        const string args = "upgrade --all --silent --accept-package-agreements --accept-source-agreements";
        return RunStreamed(args, "Alle Updates eingespielt.");
    }

    /// <summary>
    /// Installiert ein einzelnes Paket über
    /// <c>winget install &lt;q&gt; --silent --accept-package-agreements --accept-source-agreements</c>.
    /// </summary>
    public bool Install(string query)
    {
        // Suchbegriff/ID in Anführungszeichen, falls Leerzeichen enthalten sind.
        string q = query.Contains(' ') ? $"\"{query}\"" : query;
        string args = $"install {q} --silent --accept-package-agreements --accept-source-agreements";
        return RunStreamed(args, $"Paket \"{query}\" installiert.");
    }

    // ---- Geplante Auto-Update-Aufgabe (eigene Logik, schtasks) ----

    /// <summary>
    /// Legt eine geplante Aufgabe an, die WinCleaner regelmäßig im Echtbetrieb
    /// ausführt (<c>update --no-dry-run --yes</c>). Intervall: daily | weekly.
    /// </summary>
    public bool ScheduleUpdate(string interval)
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
            $"Geplantes Auto-Update ({interval}) um 04:00 Uhr eingerichtet.");
    }

    /// <summary>Entfernt die geplante Auto-Update-Aufgabe wieder.</summary>
    public bool UnscheduleUpdate()
        => RunSchtasks($"/Delete /TN \"{TaskName}\" /F", "Geplantes Auto-Update entfernt.");

    /// <summary>schtasks-Schalter für das Intervall, oder "" bei unbekanntem Wert.</summary>
    internal static string ScheduleSwitch(string interval) => interval.ToLowerInvariant() switch
    {
        "daily"  or "täglich"     => "/SC DAILY",
        "weekly" or "wöchentlich" => "/SC WEEKLY /D SUN",
        _ => ""
    };

    /// <summary>
    /// Baut die vollständigen schtasks-/Create-Argumente. --yes ist Pflicht, weil
    /// der geplante Lauf keine interaktive Konsole hat; ohne --yes würde die
    /// Bestätigungsabfrage in update den Lauf still abbrechen. Innere
    /// Anführungszeichen werden escaped, da /TR selbst in Anführungszeichen steht.
    /// </summary>
    internal static string BuildCreateArgs(string scheduleSwitch, string exePath)
    {
        string action = $"\\\"{exePath}\\\" update --no-dry-run --yes";
        return $"/Create /TN \"{TaskName}\" /TR \"{action}\" {scheduleSwitch} /ST 04:00 /F";
    }

    // ---- interne Helfer ----

    /// <summary>Baut ein <see cref="ProcessStartInfo"/> für winget mit umgeleiteten Strömen.</summary>
    private static ProcessStartInfo NewPsi(string arguments) => new()
    {
        FileName               = "winget.exe",
        Arguments              = arguments,
        RedirectStandardOutput = true,
        RedirectStandardError  = true,
        UseShellExecute        = false,
        CreateNoWindow         = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding  = Encoding.UTF8
    };

    /// <summary>
    /// Führt winget aus und spiegelt dessen Ausgabe nach stderr (Diagnose), damit
    /// stdout für JSON/Tabellen reserviert bleibt. Liefert true bei Exitcode 0.
    /// </summary>
    private bool RunStreamed(string arguments, string successMessage)
    {
        try
        {
            var psi = NewPsi(arguments);
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                _logger.Error("winget konnte nicht gestartet werden.");
                return false;
            }

            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            // winget-Ausgabe als Diagnose nach stderr.
            if (!string.IsNullOrWhiteSpace(stdout)) Console.Error.WriteLine(stdout.TrimEnd());
            if (!string.IsNullOrWhiteSpace(stderr)) Console.Error.WriteLine(stderr.TrimEnd());

            if (proc.ExitCode == 0)
            {
                _logger.Info(successMessage);
                return true;
            }

            _logger.Error($"winget fehlgeschlagen (Code {proc.ExitCode}).");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error($"Fehler beim Aufruf von winget: {ex.Message}");
            return false;
        }
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

    /// <summary>
    /// Parst die feste-Spalten-Tabelle von <c>winget upgrade</c>. winget richtet
    /// die Spalten (Name, Id, Version, Available, Source) an festen Positionen
    /// aus; wir bestimmen die Spaltengrenzen anhand der Kopfzeile und schneiden
    /// jede Datenzeile entsprechend. Zeilen ohne verfügbare Version (Spalte
    /// "Available" leer) sowie Fortschritts-/Trennzeilen werden übersprungen.
    /// </summary>
    internal static List<WingetUpdate> ParseUpgradeTable(string output)
    {
        var updates = new List<WingetUpdate>();
        if (string.IsNullOrWhiteSpace(output)) return updates;

        var lines = output.Replace("\r", string.Empty)
                          .Split('\n');

        // Kopfzeile suchen: enthält "Name" und sowohl "Id"/"ID" als auch
        // "Available"/"Verfügbar". Case-insensitiv, da deutsch lokalisiertes
        // winget die Spalte als "ID" (großes D) ausgibt.
        int headerIdx = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            var l = lines[i];
            if (l.Contains("Name", StringComparison.OrdinalIgnoreCase) &&
                l.Contains("Id", StringComparison.OrdinalIgnoreCase) &&
                (l.Contains("Available", StringComparison.OrdinalIgnoreCase) ||
                 l.Contains("Verfügbar", StringComparison.OrdinalIgnoreCase)))
            {
                headerIdx = i;
                break;
            }
        }
        if (headerIdx < 0) return updates;

        string header = lines[headerIdx];

        // Spaltengrenzen aus der Kopfzeile bestimmen. Case-insensitiv und für
        // beide Schreibweisen (Id/ID) bzw. deutsche Spaltentitel.
        int idCol        = IndexOfAny(header, "Id", "ID");
        int versionCol   = IndexOfHeader(header, "Version");
        int availableCol = IndexOfAny(header, "Available", "Verfügbar");
        int sourceCol    = IndexOfAny(header, "Source", "Quelle");

        if (idCol < 0 || versionCol < 0 || availableCol < 0) return updates;

        // Datenzeilen ab Kopfzeile+1; eine evtl. Trennlinie (---) überspringen.
        for (int i = headerIdx + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            string trimmed = line.Trim();
            // Trennlinie (---) bzw. Fortschrittsanimation (nur -, \, |, /) überspringen.
            if (trimmed.Length > 0 && trimmed.All(c => c is '-' or '\\' or '|' or '/'))
                continue;
            // Spinner-/Fortschrittszeilen von winget enthalten '»'.
            if (line.Contains('»')) continue;

            // Zeilen, die nicht breit genug für alle Spalten sind, verwerfen.
            if (line.Length <= availableCol) continue;

            string name      = Slice(line, 0, idCol).Trim();
            string id        = Slice(line, idCol, versionCol).Trim();
            string current   = Slice(line, versionCol, availableCol).Trim();
            int availEnd     = sourceCol > availableCol ? sourceCol : line.Length;
            string available = Slice(line, availableCol, availEnd).Trim();
            // Fehlt die Source-Spalte, reicht der Available-Wert bis Zeilenende und
            // enthält dann den Quellennamen ("2.7.8   winget"). Daher am ersten
            // Whitespace abschneiden, sodass nur die Versionsnummer übrig bleibt.
            if (sourceCol <= availableCol)
            {
                int ws = available.IndexOfAny(new[] { ' ', '\t' });
                if (ws >= 0) available = available.Substring(0, ws);
            }

            // Nur echte Aktualisierungszeilen: Id und verfügbare Version vorhanden.
            if (id.Length == 0 || available.Length == 0) continue;
            // Schlusszeile von winget ("X upgrades available.") ausfiltern.
            if (name.Length == 0) continue;

            updates.Add(new WingetUpdate(name, id, current, available));
        }

        return updates;
    }

    /// <summary>Spaltenstart eines Kopf-Titels (case-insensitiv), oder -1.</summary>
    private static int IndexOfHeader(string header, string title)
        => header.IndexOf(title, StringComparison.OrdinalIgnoreCase);

    private static int IndexOfAny(string header, string a, string b)
    {
        int i = header.IndexOf(a, StringComparison.OrdinalIgnoreCase);
        return i >= 0 ? i : header.IndexOf(b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Sicherer Teilstring zwischen zwei Spaltenpositionen.</summary>
    private static string Slice(string s, int start, int end)
    {
        if (start < 0) start = 0;
        if (start >= s.Length) return string.Empty;
        if (end > s.Length) end = s.Length;
        if (end <= start) return string.Empty;
        return s.Substring(start, end - start);
    }
}
