using Microsoft.Win32;

namespace WinCleaner.SystemTools;

public record StartupItem(string Source, string Name, string Path, bool Enabled);

/// <summary>
/// Liest und verwaltet Autostart-Einträge aus Registry-Run-Keys und den
/// Startup-Ordnern. Der Aktiv/Inaktiv-Status wird – wie im Task-Manager –
/// über die <c>StartupApproved</c>-Schlüssel ermittelt und gesetzt
/// (12-Byte-Blob: erstes Byte ungerade = deaktiviert), sodass Deaktivieren
/// reversibel bleibt und den Eintrag nicht zerstört.
/// </summary>
public class StartupManager
{
    private const string RunKey       = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunKeyWow    = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRun  = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedRun32= @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
    private const string ApprovedFold = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    private readonly Core.Logger _logger;
    public StartupManager(Core.Logger logger) => _logger = logger;

    public List<StartupItem> List()
    {
        var items = new List<StartupItem>();

        // Registry-Run-Keys
        ReadRunKey(items, Registry.CurrentUser,  RunKey,    ApprovedRun,   "Registry HKCU");
        ReadRunKey(items, Registry.LocalMachine, RunKey,    ApprovedRun,   "Registry HKLM");
        ReadRunKey(items, Registry.LocalMachine, RunKeyWow, ApprovedRun32, "Registry HKLM (WOW64)");

        // Startup-Ordner (User + systemweit)
        ReadStartupFolder(items, Environment.SpecialFolder.Startup,       Registry.CurrentUser,  "Startup-Ordner (User)");
        ReadStartupFolder(items, Environment.SpecialFolder.CommonStartup, Registry.LocalMachine, "Startup-Ordner (Common)");

        _logger.Info($"Startup-Liste erzeugt: {items.Count} Einträge.");
        return items;
    }

    public void Disable(string name)
    {
        // Passenden Eintrag über alle Quellen suchen (Name case-insensitive).
        var match = List().FirstOrDefault(i =>
            string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            _logger.Error($"Kein Autostart-Eintrag mit Name \"{name}\" gefunden.");
            return;
        }

        if (!match.Enabled)
        {
            _logger.Info($"\"{match.Name}\" ist bereits deaktiviert.");
            return;
        }

        // Ziel-StartupApproved-Schlüssel + Wertname je nach Quelle bestimmen.
        (RegistryKey root, string approvedSub, string valueName) target = match.Source switch
        {
            "Registry HKCU"          => (Registry.CurrentUser,  ApprovedRun,   match.Name),
            "Registry HKLM"          => (Registry.LocalMachine, ApprovedRun,   match.Name),
            "Registry HKLM (WOW64)"  => (Registry.LocalMachine, ApprovedRun32, match.Name),
            // Startup-Ordner: Wertname ist der Dateiname der .lnk-Verknüpfung.
            "Startup-Ordner (User)"   => (Registry.CurrentUser,  ApprovedFold, System.IO.Path.GetFileName(match.Path)),
            "Startup-Ordner (Common)" => (Registry.LocalMachine, ApprovedFold, System.IO.Path.GetFileName(match.Path)),
            _ => (Registry.CurrentUser, ApprovedRun, match.Name)
        };

        try
        {
            using var key = target.root.CreateSubKey(target.approvedSub, writable: true);
            if (key is null)
            {
                _logger.Error($"StartupApproved-Schlüssel nicht beschreibbar (Adminrechte für {match.Source}?).");
                return;
            }
            key.SetValue(target.valueName, BuildDisabledBlob(), RegistryValueKind.Binary);
            _logger.Info($"Autostart \"{match.Name}\" deaktiviert ({match.Source}).");
        }
        catch (UnauthorizedAccessException)
        {
            _logger.Error($"Keine Berechtigung – \"{match.Source}\" erfordert Adminrechte.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Deaktivieren fehlgeschlagen: {ex.Message}");
        }
    }

    // ---- Helpers ----

    private void ReadRunKey(List<StartupItem> items, RegistryKey hive, string subKey,
        string approvedSub, string sourceLabel)
    {
        try
        {
            using var run = hive.OpenSubKey(subKey);
            if (run is null) return;

            // Deaktiviert-Status aus StartupApproved laden (Wertname -> deaktiviert?).
            var disabled = ReadApprovedDisabledSet(hive, approvedSub);

            foreach (var valueName in run.GetValueNames())
            {
                if (string.IsNullOrEmpty(valueName)) continue;
                var path = run.GetValue(valueName)?.ToString() ?? "";
                bool enabled = !disabled.Contains(valueName);
                items.Add(new StartupItem(sourceLabel, valueName, path, enabled));
            }
        }
        catch (Exception ex)
        {
            _logger.Debug($"Run-Key {sourceLabel} nicht lesbar: {ex.Message}");
        }
    }

    private void ReadStartupFolder(List<StartupItem> items, Environment.SpecialFolder folder,
        RegistryKey approvedHive, string sourceLabel)
    {
        try
        {
            var dir = Environment.GetFolderPath(folder);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            var disabled = ReadApprovedDisabledSet(approvedHive, ApprovedFold);

            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var fileName = System.IO.Path.GetFileName(file);
                // desktop.ini u. Ä. ausblenden
                if (string.Equals(fileName, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                var name = System.IO.Path.GetFileNameWithoutExtension(file);
                bool enabled = !disabled.Contains(fileName);
                items.Add(new StartupItem(sourceLabel, name, file, enabled));
            }
        }
        catch (Exception ex)
        {
            _logger.Debug($"Startup-Ordner {sourceLabel} nicht lesbar: {ex.Message}");
        }
    }

    /// <summary>Wertnamen, die in StartupApproved als deaktiviert markiert sind.</summary>
    private static HashSet<string> ReadApprovedDisabledSet(RegistryKey hive, string approvedSub)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var approved = hive.OpenSubKey(approvedSub);
            if (approved is null) return set;

            foreach (var valueName in approved.GetValueNames())
            {
                if (approved.GetValue(valueName) is byte[] blob && blob.Length > 0 && IsDisabledBlob(blob))
                    set.Add(valueName);
            }
        }
        catch { /* StartupApproved fehlt -> nichts deaktiviert */ }
        return set;
    }

    // Erstes Byte ungerade (Bit 0 gesetzt) => deaktiviert. Aktiv ist typ. 0x02.
    private static bool IsDisabledBlob(byte[] blob) => (blob[0] & 1) == 1;

    // 12-Byte-Blob: DWORD-Status 0x03 (deaktiviert) + 8-Byte-FILETIME des Zeitpunkts.
    private static byte[] BuildDisabledBlob()
    {
        var blob = new byte[12];
        blob[0] = 0x03;
        long fileTime = DateTime.UtcNow.ToFileTimeUtc();
        BitConverter.GetBytes(fileTime).CopyTo(blob, 4);
        return blob;
    }
}
