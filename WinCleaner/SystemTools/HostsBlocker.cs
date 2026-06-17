using System.Text;

namespace WinCleaner.SystemTools;

/// <summary>
/// Verwaltet einen markierten Telemetrie-Block-Abschnitt in der Windows-hosts-Datei
/// (<c>C:\Windows\System32\drivers\etc\hosts</c>). Der Abschnitt steht zwischen den
/// Markierungen <see cref="MarkerStart"/> und <see cref="MarkerEnd"/> und leitet eine
/// kuratierte, konservative Liste bekannter Microsoft-Telemetrie-Hosts auf
/// <c>0.0.0.0</c> um (Null-Route).
///
/// Vollständig REVERSIBEL: <see cref="Undo"/> entfernt ausschließlich den markierten
/// Abschnitt und lässt alle übrigen, vom Benutzer oder anderen Programmen
/// hinzugefügten Einträge unberührt. Vor jeder Schreibänderung wird eine Sicherung
/// (<c>hosts.wincleaner.bak</c>) im selben Verzeichnis angelegt.
///
/// Änderungen an der hosts-Datei erfordern Adminrechte; der Aufrufer kann sich über
/// <see cref="Elevation"/> neu starten.
/// </summary>
public sealed class HostsBlocker
{
    /// <summary>Startmarkierung des von WinCleaner verwalteten Abschnitts.</summary>
    public const string MarkerStart = "# === WinCleaner Telemetrie-Block Start ===";

    /// <summary>Endmarkierung des von WinCleaner verwalteten Abschnitts.</summary>
    public const string MarkerEnd = "# === WinCleaner Telemetrie-Block Ende ===";

    private readonly Core.Logger _logger;

    /// <summary>Pfad zur systemweiten hosts-Datei.</summary>
    public string HostsPath { get; }

    /// <summary>Pfad zur Sicherungsdatei (neben der hosts-Datei).</summary>
    public string BackupPath { get; }

    public HostsBlocker(Core.Logger logger)
    {
        _logger = logger;
        // Robust gegenüber abweichendem Systemlaufwerk: %SystemRoot%\System32\drivers\etc\hosts.
        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrEmpty(systemRoot)) systemRoot = @"C:\Windows";
        HostsPath = Path.Combine(systemRoot, "System32", "drivers", "etc", "hosts");
        BackupPath = HostsPath + ".wincleaner.bak";
    }

    /// <summary>
    /// Kuratierte, konservative Liste bekannter Microsoft-Telemetrie-Endpunkte.
    /// Bewusst eng gehalten: nur reine Diagnose-/Telemetrie-Hosts, KEINE Update-,
    /// Lizenz- oder Store-Funktions-Hosts, um normale Windows-Funktionen nicht zu stören.
    /// </summary>
    public static readonly IReadOnlyList<string> TelemetryHosts = new[]
    {
        "vortex.data.microsoft.com",
        "vortex-win.data.microsoft.com",
        "telecommand.telemetry.microsoft.com",
        "telecommand.telemetry.microsoft.com.nsatc.net",
        "oca.telemetry.microsoft.com",
        "oca.telemetry.microsoft.com.nsatc.net",
        "sqm.telemetry.microsoft.com",
        "sqm.telemetry.microsoft.com.nsatc.net",
        "watson.telemetry.microsoft.com",
        "watson.telemetry.microsoft.com.nsatc.net",
        "redir.metaservices.microsoft.com",
        "choice.microsoft.com",
        "choice.microsoft.com.nsatc.net",
        "df.telemetry.microsoft.com",
        "reports.wes.df.telemetry.microsoft.com",
        "wes.df.telemetry.microsoft.com",
        "services.wes.df.telemetry.microsoft.com",
        "sqm.df.telemetry.microsoft.com",
        "telemetry.microsoft.com",
        "watson.ppe.telemetry.microsoft.com",
        "telemetry.appex.bing.net",
        "telemetry.urs.microsoft.com",
        "settings-sandbox.data.microsoft.com",
        "vortex-sandbox.data.microsoft.com",
        "survey.watson.microsoft.com",
        "watson.live.com",
        "statsfe2.ws.microsoft.com",
        "corpext.msitadfs.glbdns2.microsoft.com",
        "compatexchange.cloudapp.net",
        // Hinweis: "a-0001.a-msedge.net" (generischer Edge/Akamai-CDN-Knoten) und
        // "fe2.update.microsoft.com.akadns.net" (Windows-Update-CDN) wurden bewusst
        // entfernt – keine reinen Telemetrie-Hosts; Blocken könnte Updates/CDN stören.
        "diagnostics.support.microsoft.com",
        "feedback.windows.com",
        "feedback.microsoft-hohm.com",
        "feedback.search.microsoft.com",
    };

    /// <summary>Beschreibt den aktuellen Zustand des markierten Abschnitts.</summary>
    /// <param name="Active">True, wenn der markierte Block in der hosts-Datei vorhanden ist.</param>
    /// <param name="HostCount">Anzahl der derzeit im Block geblockten Hosts.</param>
    /// <param name="CuratedCount">Anzahl der von WinCleaner kuratierten Telemetrie-Hosts.</param>
    /// <param name="HostsPath">Pfad der ausgewerteten hosts-Datei.</param>
    public sealed record BlockStatus(bool Active, int HostCount, int CuratedCount, string HostsPath);

    /// <summary>Liest den aktuellen Zustand des markierten Abschnitts (read-only).</summary>
    public BlockStatus GetStatus()
    {
        int curated = TelemetryHosts.Count;
        try
        {
            if (!File.Exists(HostsPath))
                return new BlockStatus(false, 0, curated, HostsPath);

            var lines = File.ReadAllLines(HostsPath);
            var (start, end) = FindSection(lines);
            if (start < 0 || end < 0)
                return new BlockStatus(false, 0, curated, HostsPath);

            int count = 0;
            for (int i = start + 1; i < end; i++)
            {
                var t = lines[i].Trim();
                if (t.Length == 0 || t.StartsWith('#')) continue;
                count++;
            }
            return new BlockStatus(true, count, curated, HostsPath);
        }
        catch (Exception ex)
        {
            _logger.Error($"hosts-Datei konnte nicht gelesen werden: {ex.Message}");
            return new BlockStatus(false, 0, curated, HostsPath);
        }
    }

    /// <summary>
    /// Schreibt bzw. aktualisiert den markierten Telemetrie-Block. Ein bereits
    /// vorhandener WinCleaner-Abschnitt wird durch die aktuelle kuratierte Liste
    /// ersetzt; der Rest der Datei bleibt unverändert. True bei Erfolg.
    /// </summary>
    public bool Apply()
    {
        try
        {
            EnsureHostsFile();
            if (!CreateBackup()) return false;

            var lines = File.Exists(HostsPath)
                ? File.ReadAllLines(HostsPath).ToList()
                : new List<string>();

            // Vorhandenen markierten Abschnitt (falls vorhanden) entfernen.
            RemoveSection(lines);

            var block = BuildSection();

            // Leerzeile vor dem Block für saubere Trennung, wenn die Datei nicht leer endet.
            if (lines.Count > 0 && lines[^1].Trim().Length != 0)
                lines.Add(string.Empty);

            lines.AddRange(block);

            File.WriteAllLines(HostsPath, lines, new UTF8Encoding(false));
            _logger.Info($"Telemetrie-Block geschrieben: {TelemetryHosts.Count} Hosts in {HostsPath}.");
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            _logger.Error("Adminrechte erforderlich, um die hosts-Datei zu ändern.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error($"Schreiben des Telemetrie-Blocks fehlgeschlagen: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Entfernt ausschließlich den markierten WinCleaner-Abschnitt aus der hosts-Datei.
    /// Alle übrigen Zeilen bleiben unberührt. True bei Erfolg (auch, wenn kein
    /// Abschnitt vorhanden war).
    /// </summary>
    public bool Undo()
    {
        try
        {
            if (!File.Exists(HostsPath))
            {
                _logger.Info("Keine hosts-Datei vorhanden – nichts zu entfernen.");
                return true;
            }

            var lines = File.ReadAllLines(HostsPath).ToList();
            var (start, end) = FindSection(lines.ToArray());
            if (start < 0 || end < 0)
            {
                _logger.Info("Kein WinCleaner-Telemetrie-Block vorhanden – nichts zu entfernen.");
                return true;
            }

            if (!CreateBackup()) return false;

            RemoveSection(lines);
            TrimTrailingBlankLines(lines);

            File.WriteAllLines(HostsPath, lines, new UTF8Encoding(false));
            _logger.Info("Telemetrie-Block aus der hosts-Datei entfernt.");
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            _logger.Error("Adminrechte erforderlich, um die hosts-Datei zu ändern.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error($"Entfernen des Telemetrie-Blocks fehlgeschlagen: {ex.Message}");
            return false;
        }
    }

    /// <summary>Baut die Zeilen des markierten Abschnitts (inkl. Markierungen).</summary>
    private static List<string> BuildSection()
    {
        var block = new List<string>(TelemetryHosts.Count + 3) { MarkerStart };
        foreach (var host in TelemetryHosts)
            block.Add($"0.0.0.0 {host}");
        block.Add(MarkerEnd);
        return block;
    }

    /// <summary>Findet Start-/End-Index der Markierungen in den Zeilen (-1, wenn nicht vorhanden).</summary>
    private static (int start, int end) FindSection(string[] lines)
    {
        int start = -1, end = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (start < 0 && t.Equals(MarkerStart, StringComparison.Ordinal)) start = i;
            else if (start >= 0 && t.Equals(MarkerEnd, StringComparison.Ordinal)) { end = i; break; }
        }
        return (start, end);
    }

    /// <summary>Entfernt den markierten Abschnitt (inkl. beider Markierungen) aus der Liste.</summary>
    private static void RemoveSection(List<string> lines)
    {
        var (start, end) = FindSection(lines.ToArray());
        if (start < 0 || end < 0) return;
        lines.RemoveRange(start, end - start + 1);
    }

    /// <summary>Entfernt überzählige Leerzeilen am Dateiende.</summary>
    private static void TrimTrailingBlankLines(List<string> lines)
    {
        while (lines.Count > 0 && lines[^1].Trim().Length == 0)
            lines.RemoveAt(lines.Count - 1);
    }

    /// <summary>Legt eine Sicherung der aktuellen hosts-Datei an (überschreibt eine ältere). True bei Erfolg.</summary>
    private bool CreateBackup()
    {
        try
        {
            if (File.Exists(HostsPath))
            {
                File.Copy(HostsPath, BackupPath, overwrite: true);
                _logger.Info($"Sicherung der hosts-Datei angelegt: {BackupPath}");
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Sicherung der hosts-Datei fehlgeschlagen, Abbruch: {ex.Message}");
            return false;
        }
    }

    /// <summary>Stellt sicher, dass das etc-Verzeichnis existiert (hosts wird ggf. neu erzeugt).</summary>
    private void EnsureHostsFile()
    {
        var dir = Path.GetDirectoryName(HostsPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }
}
