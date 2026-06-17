using System.Security.Cryptography;
using WinCleaner.Core;
using WinCleaner.SystemTools;
using WinCleaner.Util;

namespace WinCleaner.Commands;

/// <summary>
/// Überschreibt den FREIEN Speicher eines Laufwerks, indem eine große
/// Füll-Datei angelegt und mit Daten beschrieben wird, bis das Laufwerk voll ist;
/// danach wird die Datei wieder gelöscht. So lassen sich Reste bereits gelöschter
/// (aber noch nicht überschriebener) Dateien unkenntlich machen. Vorhandene
/// Dateien bleiben unberührt.
/// Wegen der Eingriffstiefe (Laufwerk wird vorübergehend vollgeschrieben) ist die
/// echte Aktion doppelt abgesichert: nur mit <c>--no-dry-run UND --yes</c>;
/// andernfalls nur Trockenlauf. Auf SSDs ist das Verfahren wirkungslos – Hinweis folgt.
/// </summary>
public sealed class WipeFreeSpaceCommand : ICommand
{
    public string Name => "wipe-free-space";
    public string Summary => "Freien Speicher eines Laufwerks überschreiben (Reste gelöschter Dateien tilgen)";
    public string Usage => "<Laufwerk> [--no-dry-run] [--yes]";
    public string[] AllowedFlags => new[] { "--no-dry-run", "--yes" };

    private const int BufferSize = 4 * 1024 * 1024; // 4 MB Schreibpuffer
    private const long SafetyMargin = 64L * 1024 * 1024; // 64 MB Reserve, damit das System nicht hängt

    public int Execute(CommandContext ctx)
    {
        var input = ctx.FirstPositional;
        if (input is null)
        {
            Console.Error.WriteLine($"{Name} {Usage}");
            return 1;
        }

        // Eingabe normalisieren: "C", "C:" oder "C:\" -> Wurzelpfad.
        string root;
        try
        {
            string letter = input.TrimEnd('\\', '/', ':');
            if (letter.Length == 0)
            {
                ctx.Logger.Error($"Ungültiges Laufwerk: {input}");
                return 1;
            }
            root = letter.Length == 1 ? $"{char.ToUpperInvariant(letter[0])}:\\" : Path.GetFullPath(input);
        }
        catch (Exception ex)
        {
            ctx.Logger.Error($"Ungültiges Laufwerk \"{input}\": {ex.Message}");
            return 1;
        }

        DriveInfo drive;
        try
        {
            drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                ctx.Logger.Error($"Laufwerk nicht bereit: {root}");
                return 1;
            }
        }
        catch (Exception ex)
        {
            ctx.Logger.Error($"Laufwerk \"{root}\" nicht zugreifbar: {ex.Message}");
            return 1;
        }

        long freeBytes = drive.AvailableFreeSpace;
        var secure = new SecureDelete(ctx.Logger);
        var media = secure.DetectMediaType(root);
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
                    drive = root,
                    freeBytes,
                    mediaType = media.ToString(),
                    dryRun
                });
                return 0;
            }
        }
        else
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("=== FREIEN SPEICHER ÜBERSCHREIBEN (wipe-free-space) ===");
            Console.Error.WriteLine($"Laufwerk:     {root}");
            Console.Error.WriteLine($"Freier Platz: {DiskAnalyzer.FormatSize(freeBytes)}");
            Console.Error.WriteLine("Vorgehen: Eine temporäre Datei wird mit Zufallsdaten gefüllt, " +
                                    "bis das Laufwerk voll ist, danach wieder gelöscht.");
            WarnIfSsd(ctx, media);
        }

        // --- Trockenlauf (Default) ---
        if (dryRun)
        {
            ctx.Logger.Info("Trockenlauf: Es wird NICHTS geschrieben. Für die echte Aktion: " +
                            "--no-dry-run --yes angeben.");
            return 0;
        }

        // --- Echte Aktion ---
        Console.Error.WriteLine();
        Console.Error.WriteLine("!!! WARNUNG: Das Laufwerk wird kurzzeitig nahezu vollständig gefüllt. !!!");
        Console.Error.WriteLine("!!! Bricht die Aktion ab, kann eine große temporäre Datei zurückbleiben. !!!");

        if (!ctx.HasFlag("--yes"))
        {
            if (!Prompt.Confirm($"Freien Speicher auf {root} jetzt überschreiben?"))
            {
                Console.Error.WriteLine("Abgebrochen.");
                return 1;
            }
        }

        long written = WipeFreeSpace(ctx.Logger, root);

        if (ctx.Json)
        {
            JsonOut.Write(new { command = Name, drive = root, bytesWritten = written, dryRun = false });
        }
        else
        {
            Console.WriteLine(written > 0
                ? $"Freier Speicher überschrieben: {DiskAnalyzer.FormatSize(written)} auf {root}."
                : $"Es konnte kein freier Speicher überschrieben werden auf {root}.");
        }

        return written > 0 ? 0 : 2;
    }

    /// <summary>
    /// Füllt den freien Speicher über eine temporäre Datei mit Zufallsdaten und
    /// löscht sie anschließend. Liefert die Anzahl geschriebener Bytes. Das
    /// erwartete „Datenträger voll“ (<see cref="IOException"/>) wird als
    /// regulärer Abschluss behandelt.
    /// </summary>
    private static long WipeFreeSpace(Logger logger, string root)
    {
        string tempFile = Path.Combine(root, $"WinCleaner_Wipe_{Guid.NewGuid():N}.tmp");
        long written = 0;

        try
        {
            using var rng = RandomNumberGenerator.Create();
            var buffer = new byte[BufferSize];

            using (var fs = new FileStream(tempFile, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, BufferSize, FileOptions.WriteThrough))
            {
                while (true)
                {
                    // Sicherheitsreserve wahren, damit das Betriebssystem nicht blockiert.
                    long free = new DriveInfo(root).AvailableFreeSpace;
                    if (free <= SafetyMargin) break;

                    int chunk = (int)Math.Min(buffer.Length, free - SafetyMargin);
                    if (chunk <= 0) break;

                    rng.GetBytes(buffer, 0, chunk);
                    try
                    {
                        fs.Write(buffer, 0, chunk);
                        written += chunk;
                    }
                    catch (IOException)
                    {
                        // Datenträger voll – erwartetes Ende.
                        break;
                    }
                }

                try { fs.Flush(flushToDisk: true); } catch (IOException) { /* voll */ }
            }
        }
        catch (Exception ex)
        {
            logger.Error($"Überschreiben des freien Speichers fehlgeschlagen: {ex.Message}");
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); }
            catch (Exception ex) { logger.Error($"Temporäre Füll-Datei konnte nicht gelöscht werden: {ex.Message}"); }
        }

        return written;
    }

    /// <summary>Warnt nach stderr, wenn der Zieldatenträger eine SSD ist (oder unbekannt).</summary>
    private static void WarnIfSsd(CommandContext ctx, DriveMediaType media)
    {
        switch (media)
        {
            case DriveMediaType.Ssd:
                Console.Error.WriteLine(
                    "HINWEIS: SSD erkannt. Das Überschreiben freien Speichers ist wegen " +
                    "Wear-Leveling/TRIM WIRKUNGSLOS und verschleißt nur die Zellen. " +
                    "Für SSDs ist Geräte-Verschlüsselung sinnvoller.");
                break;
            case DriveMediaType.Unknown:
                Console.Error.WriteLine(
                    "HINWEIS: Datenträgertyp unbekannt. Auf SSDs ist dieses Verfahren " +
                    "wirkungslos (Wear-Leveling/TRIM).");
                break;
        }
    }
}
