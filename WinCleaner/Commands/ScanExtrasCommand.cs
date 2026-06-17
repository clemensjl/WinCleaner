using WinCleaner.Core;
using WinCleaner.Util;

namespace WinCleaner.Commands;

/// <summary>
/// Befehl <c>scan-extras &lt;Pfad&gt;</c>: findet rekursiv leere Ordner, 0-Byte-
/// Dateien sowie kaputte Verknüpfungen (.lnk) und kaputte Symlinks/Reparse-Points
/// (Ziel existiert nicht mehr). Standard ist ein reiner Suchlauf; mit
/// <c>--delete</c> werden die Funde umkehrbar in den Papierkorb verschoben
/// (Default: Probelauf, echte Aktion nur mit <c>--no-dry-run</c>).
/// </summary>
public sealed class ScanExtrasCommand : ICommand
{
    public string Name => "scan-extras";

    public string Summary =>
        "Leere Ordner, 0-Byte-Dateien und kaputte Verknüpfungen/Symlinks finden (optional in Papierkorb)";

    public string Usage => "<Pfad> [--delete] [--no-dry-run] [--yes]";

    public string[] AllowedFlags => new[] { "--delete", "--no-dry-run", "--yes" };

    public int Execute(CommandContext ctx)
    {
        var path = ctx.FirstPositional;
        if (path is null)
        {
            Console.Error.WriteLine($"{Name} {Usage}");
            return 1;
        }

        if (!Directory.Exists(path))
        {
            ctx.Logger.Error($"Pfad nicht gefunden oder kein Ordner: {path}");
            return 1;
        }

        bool wantsDelete = ctx.HasFlag("--delete");
        // Sicherheits-Default: Probelauf. Echte Löschung nur mit --no-dry-run.
        bool dryRun = !ctx.HasFlag("--no-dry-run");

        var scanner = new ExtraScanner(ctx.Logger);
        var result = scanner.Scan(path);

        // Nutzdaten (Fundliste) nach stdout ausgeben, solange keine Aktion ansteht
        // bzw. bei --json immer maschinenlesbar.
        if (!wantsDelete)
        {
            Report(ctx, result);
            return 0;
        }

        if (result.Total == 0)
        {
            if (ctx.Json) JsonOut.Write(new ExtraDeleteResult(0, dryRun, true));
            else Console.WriteLine("Keine zu bereinigenden Einträge gefunden.");
            return 0;
        }

        // Vor der eigentlichen Aktion die Fundliste zeigen (nur Nicht-JSON, sonst
        // bliebe stdout nicht maschinenlesbar – im JSON-Fall folgt unten das Ergebnis).
        if (!ctx.Json)
            Report(ctx, result);

        if (dryRun)
        {
            // Probelauf: nichts wird verändert.
            ctx.Logger.Info("Probelauf (Standard) – es wird NICHTS gelöscht. Mit --no-dry-run echt ausführen.");
            return DoDelete(ctx, result, dryRun: true);
        }

        // Echte Löschung bestätigen (außer --yes); Rückfrage nach stderr.
        Console.Error.WriteLine(
            $"\n{result.Total} Einträge werden in den Papierkorb verschoben " +
            $"({result.EmptyFolders} leere Ordner, {result.EmptyFiles} 0-Byte-Dateien, " +
            $"{result.BrokenShortcuts} kaputte Verknüpfungen, {result.BrokenSymlinks} kaputte Symlinks).");

        if (!ctx.HasFlag("--yes") && !Prompt.Confirm("Fortfahren?"))
        {
            Console.Error.WriteLine("Abgebrochen.");
            return 1;
        }

        return DoDelete(ctx, result, dryRun: false);
    }

    /// <summary>Führt die (ggf. simulierte) Verschiebung in den Papierkorb durch und meldet das Ergebnis.</summary>
    private static int DoDelete(CommandContext ctx, ExtraScanResult result, bool dryRun)
    {
        int deleted = 0;

        if (!dryRun)
        {
            // Reihenfolge: zuerst Dateien/Verknüpfungen/Symlinks, dann (von tief nach
            // flach sortiert) leere Ordner – so werden verschachtelte Leerordner sauber
            // entfernt, ohne dass ein Elternordner vor seinem Kind verschwindet.
            foreach (var e in result.Entries
                         .Where(e => e.Kind != ExtraKind.EmptyFolder))
            {
                if (TryDelete(ctx, e)) deleted++;
            }

            foreach (var e in result.Entries
                         .Where(e => e.Kind == ExtraKind.EmptyFolder)
                         .OrderByDescending(e => e.Path.Length))
            {
                if (TryDelete(ctx, e)) deleted++;
            }
        }
        else
        {
            deleted = result.Total; // im Probelauf gälten alle als betroffen
        }

        var report = new ExtraDeleteResult(deleted, dryRun, true);
        if (ctx.Json)
        {
            JsonOut.Write(report);
        }
        else
        {
            string praefix = dryRun ? "[Probelauf] Es würden" : "Es wurden";
            string verb    = dryRun ? "verschoben werden" : "verschoben";
            Console.WriteLine($"\n{praefix} {deleted} Einträge in den Papierkorb {verb}.");
        }
        return 0;
    }

    /// <summary>Verschiebt einen einzelnen Fund umkehrbar in den Papierkorb; Fehler werden still gezählt/geloggt.</summary>
    private static bool TryDelete(CommandContext ctx, ExtraEntry e)
    {
        try
        {
            // Verzeichnis-Erkennung: EmptyFolder ist immer ein Ordner. Ein als
            // BrokenSymlink erkannter Reparse-Point kann ebenfalls ein Verzeichnis
            // sein (symlinkd/Junction). Directory.Exists liefert bei kaputtem
            // symlinkd false, daher das Directory-Bit via File.GetAttributes prüfen.
            bool isDirectory = e.Kind == ExtraKind.EmptyFolder;
            if (!isDirectory && e.Kind == ExtraKind.BrokenSymlink)
            {
                try
                {
                    isDirectory = (File.GetAttributes(e.Path) & FileAttributes.Directory) != 0;
                }
                catch
                {
                    // Attribute nicht lesbar -> als Datei behandeln (DeleteFile-Fallback).
                }
            }

            if (isDirectory)
                RecycleBinHelper.DeleteDirectory(e.Path);
            else
                RecycleBinHelper.DeleteFile(e.Path);
            return true;
        }
        catch (Exception ex)
        {
            ctx.Logger.Error($"Konnte nicht löschen: {e.Path} ({ex.Message})");
            return false;
        }
    }

    /// <summary>Gibt die Fundliste als JSON (stdout) oder als Tabelle mit Summen aus.</summary>
    private static void Report(CommandContext ctx, ExtraScanResult result)
    {
        if (ctx.Json)
        {
            JsonOut.Write(result);
            return;
        }

        if (result.Total == 0)
        {
            Console.WriteLine("Keine leeren Ordner, 0-Byte-Dateien oder kaputten Verknüpfungen/Symlinks gefunden.");
            return;
        }

        var rows = result.Entries.Select(e => new[] { Label(e.Kind), e.Path });
        ConsoleTable.From(rows, "Typ", "Pfad").Write();

        Console.WriteLine();
        Console.WriteLine($"Leere Ordner:            {result.EmptyFolders}");
        Console.WriteLine($"0-Byte-Dateien:          {result.EmptyFiles}");
        Console.WriteLine($"Kaputte Verknüpfungen:   {result.BrokenShortcuts}");
        Console.WriteLine($"Kaputte Symlinks:        {result.BrokenSymlinks}");
        Console.WriteLine($"Gesamt:                  {result.Total}");
    }

    /// <summary>Deutsche Bezeichnung einer Fund-Art für die Tabelle.</summary>
    private static string Label(ExtraKind kind) => kind switch
    {
        ExtraKind.EmptyFolder    => "Leerer Ordner",
        ExtraKind.EmptyFile      => "0-Byte-Datei",
        ExtraKind.BrokenShortcut => "Kaputte Verknüpfung",
        ExtraKind.BrokenSymlink  => "Kaputter Symlink",
        _                        => "Unbekannt"
    };
}

/// <summary>Maschinenlesbares Ergebnis der Lösch-/Probelauf-Aktion von <c>scan-extras</c>.</summary>
public record ExtraDeleteResult(int Deleted, bool DryRun, bool Ok);
