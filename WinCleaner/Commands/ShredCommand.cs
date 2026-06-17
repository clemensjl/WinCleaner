using WinCleaner.Core;
using WinCleaner.SystemTools;
using WinCleaner.Util;

namespace WinCleaner.Commands;

/// <summary>
/// Sicheres, NICHT umkehrbares Löschen einzelner Dateien oder Ordner durch
/// mehrfaches Überschreiben (Standard: 3 Pässe) mit anschließendem endgültigen
/// Löschen. Wegen der Irreversibilität ist die echte Aktion bewusst doppelt
/// abgesichert: Sie läuft nur mit <c>--no-dry-run</c> und Bestätigung (oder
/// <c>--yes</c>); andernfalls wird
/// lediglich ein Trockenlauf angezeigt. Auf SSDs ist Überschreiben wirkungslos –
/// darauf wird laut hingewiesen.
/// </summary>
public sealed class ShredCommand : ICommand
{
    public string Name => "shred";
    public string Summary => "Datei/Ordner sicher und unwiderruflich löschen (mehrfaches Überschreiben)";
    public string Usage => "<Pfad> [--passes <n>] [--no-dry-run] [--yes]";
    public string[] AllowedFlags => new[] { "--passes", "--no-dry-run", "--yes" };

    public int Execute(CommandContext ctx)
    {
        // Pfad = erstes Positional, das NICHT der Wert von --passes ist.
        // Bei "shred --passes 3 C:\\datei" steht "3" als Positional vor dem Pfad;
        // diesen Wert hier überspringen (OptionInt liest ihn separat).
        var passesValue = ctx.Option("--passes");
        var path = ctx.Positionals.FirstOrDefault(p => !string.Equals(p, passesValue, StringComparison.Ordinal));
        if (path is null)
        {
            Console.Error.WriteLine($"{Name} {Usage}");
            return 1;
        }

        bool isFile = File.Exists(path);
        bool isDir = Directory.Exists(path);
        if (!isFile && !isDir)
        {
            ctx.Logger.Error($"Pfad nicht gefunden: {path}");
            return 1;
        }

        int passes = ctx.OptionInt("--passes", SecureDelete.DefaultPasses);
        if (passes < 1)
        {
            ctx.Logger.Error("--passes muss mindestens 1 sein.");
            return 1;
        }

        var secure = new SecureDelete(ctx.Logger);

        // Umfang ermitteln (für Anzeige/JSON).
        var (fileCount, totalBytes) = Measure(path, isDir);
        var media = secure.DetectMediaType(path);

        bool dryRun = !ctx.HasFlag("--no-dry-run");

        if (ctx.Json)
        {
            // Nur EIN JSON-Objekt ausgeben: im Trockenlauf den Plan (und beenden),
            // bei echter Aktion wird ausschließlich das Ergebnis-Objekt geschrieben.
            if (dryRun)
            {
                JsonOut.Write(new
                {
                    command = Name,
                    path,
                    kind = isDir ? "directory" : "file",
                    passes,
                    files = fileCount,
                    bytes = totalBytes,
                    mediaType = media.ToString(),
                    dryRun
                });
                return 0;
            }
        }
        else
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("=== SICHERES LÖSCHEN (shred) ===");
            Console.Error.WriteLine($"Ziel:    {path} ({(isDir ? "Ordner" : "Datei")})");
            Console.Error.WriteLine($"Dateien: {fileCount}   Größe: {DiskAnalyzer.FormatSize(totalBytes)}");
            Console.Error.WriteLine($"Pässe:   {passes}");
            WarnIfSsd(ctx, media);
        }

        // --- Trockenlauf (Default) ---
        if (dryRun)
        {
            ctx.Logger.Info("Trockenlauf: Es wird NICHTS gelöscht. Für echtes, " +
                            "UNWIDERRUFLICHES Löschen: --no-dry-run angeben und " +
                            "bestätigen (oder --yes).");
            return 0;
        }

        // --- Echte, irreversible Aktion ---
        Console.Error.WriteLine();
        Console.Error.WriteLine("!!! WARNUNG: Diese Aktion ist ENDGÜLTIG und NICHT umkehrbar. !!!");
        Console.Error.WriteLine("!!! Die Daten landen NICHT im Papierkorb und sind danach unwiederbringlich. !!!");

        if (!ctx.HasFlag("--yes"))
        {
            // Irreversibel -> ohne --yes ist interaktive Bestätigung nötig.
            if (!Prompt.Confirm($"\"{path}\" wirklich unwiderruflich vernichten?"))
            {
                Console.Error.WriteLine("Abgebrochen.");
                return 1;
            }
        }

        int okFiles;
        if (isDir)
        {
            okFiles = secure.OverwriteDirectory(path, passes, out int failed);
            if (failed > 0)
                ctx.Logger.Error($"{failed} Datei(en) konnten nicht überschrieben werden (gesperrt/unzugänglich).");
        }
        else
        {
            okFiles = secure.OverwriteFile(path, passes) ? 1 : 0;
        }

        if (ctx.Json)
        {
            JsonOut.Write(new { command = Name, path, deletedFiles = okFiles, dryRun = false });
        }
        else
        {
            Console.WriteLine(okFiles > 0
                ? $"Sicher gelöscht: {okFiles} Datei(en) aus \"{path}\"."
                : $"Es wurde nichts gelöscht (Fehler/leer): \"{path}\".");
        }

        return okFiles > 0 ? 0 : 2;
    }

    /// <summary>Warnt nach stderr, wenn der Zieldatenträger eine SSD ist (oder unbekannt).</summary>
    private static void WarnIfSsd(CommandContext ctx, DriveMediaType media)
    {
        switch (media)
        {
            case DriveMediaType.Ssd:
                Console.Error.WriteLine(
                    "HINWEIS: SSD erkannt. Überschreiben ist wegen Wear-Leveling/TRIM " +
                    "WIRKUNGSLOS und verkürzt nur die Lebensdauer. Für SSDs sind " +
                    "Geräte-Verschlüsselung oder ein Secure-Erase des Herstellers sinnvoller.");
                break;
            case DriveMediaType.Unknown:
                Console.Error.WriteLine(
                    "HINWEIS: Datenträgertyp unbekannt. Falls es sich um eine SSD handelt, " +
                    "ist Überschreiben wirkungslos (Wear-Leveling/TRIM).");
                break;
        }
    }

    /// <summary>Ermittelt Dateianzahl und Gesamtgröße des Ziels (still bei Fehlern).</summary>
    private static (int files, long bytes) Measure(string path, bool isDir)
    {
        if (!isDir)
        {
            try { return (1, new FileInfo(path).Length); }
            catch { return (1, 0); }
        }

        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        int files = 0; long bytes = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(path, "*", opts))
            {
                try { bytes += new FileInfo(f).Length; files++; }
                catch { /* gesperrt/weg */ }
            }
        }
        catch { /* unzugänglich */ }

        return (files, bytes);
    }
}
