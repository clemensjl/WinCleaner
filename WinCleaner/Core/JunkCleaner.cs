using System;
using System.Collections.Generic;
using System.IO;
using VB = Microsoft.VisualBasic.FileIO; // für Recycle Bin-Operationen

namespace WinCleaner.Core;

public class JunkCleaner
{
    private static readonly EnumerationOptions TopOnly = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false
    };

    private readonly Logger _logger;
    public JunkCleaner(Logger logger) => _logger = logger;

    public void Clean(JunkReport report, bool dryRun, bool sendToRecycleBin)
    {
        foreach (var item in report.Items)
        {
            // Nur "Safe" automatisch löschen
            if (item.Safety != Safety.Safe)
            {
                _logger.Info($"[SKIP] {item.Category} (Sicherheitsstufe: {item.Safety})");
                continue;
            }

            _logger.Info($"{(dryRun ? "[DRY]" : "[DEL]")} {item.Category} → {item.Path}");
            if (dryRun) continue;
            if (!Directory.Exists(item.Path)) continue;

            try
            {
                // 1) Unterordner in EINEM Schritt je Ordner entsorgen (viel schneller als Datei-für-Datei)
                foreach (var subdir in Directory.EnumerateDirectories(item.Path, "*", TopOnly))
                {
                    try
                    {
                        if (sendToRecycleBin)
                        {
                            VB.FileSystem.DeleteDirectory(
                                subdir,
                                VB.UIOption.OnlyErrorDialogs,
                                VB.RecycleOption.SendToRecycleBin);
                        }
                        else
                        {
                            Directory.Delete(subdir, true);
                        }
                    }
                    catch { /* gesperrte Ordner überspringen */ }
                }

                // 2) Dateien im Wurzelordner entsorgen
                foreach (var file in Directory.EnumerateFiles(item.Path, "*", TopOnly))
                {
                    try
                    {
                        if (sendToRecycleBin)
                        {
                            VB.FileSystem.DeleteFile(
                                file,
                                VB.UIOption.OnlyErrorDialogs,
                                VB.RecycleOption.SendToRecycleBin);
                        }
                        else
                        {
                            File.Delete(file);
                        }
                    }
                    catch { /* ignore locked */ }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Fehler beim Löschen in {item.Path}: {ex.Message}");
            }
        }

        _logger.Info("Bereinigung abgeschlossen.");
    }
}
