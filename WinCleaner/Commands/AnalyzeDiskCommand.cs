using System.Globalization;
using System.Text;
using WinCleaner.Core;
using WinCleaner.Util;

namespace WinCleaner.Commands;

public sealed class AnalyzeDiskCommand : ICommand
{
    public string Name => "analyze-disk";
    public string Summary => "Größte Ordner/Dateien anzeigen (Filter, nach Endung, Export)";
    public string Usage =>
        "<Pfad> [--fast] [--by-type] [--min-size <z.B.100MB>] [--type <.ext,.ext>] " +
        "[--age-days <n>] [--depth <n>] [--top <n>] [--export csv|html] [--out <Pfad>] " +
        "[--snapshot <Datei>] [--html <report.html>]";

    public string[] AllowedFlags => new[]
    {
        "--fast", "--by-type", "--min-size", "--type", "--age-days", "--depth", "--top", "--export", "--out",
        "--snapshot", "--html"
    };

    public int Execute(CommandContext ctx)
    {
        var path = ctx.FirstPositional;
        if (path is null)
        {
            Console.Error.WriteLine($"Pfad fehlt: {Name} {Usage}");
            return 1;
        }

        // ---- Optionen einlesen ----
        int top = ctx.OptionInt("--top", 25);
        if (top < 1) top = 25;

        int depth = ctx.OptionInt("--depth", 1);
        if (depth < 1) depth = 1;

        bool byType = ctx.HasFlag("--by-type");

        // Filter zusammenbauen.
        long? minSize = null;
        var minSizeRaw = ctx.Option("--min-size");
        if (minSizeRaw is not null)
        {
            minSize = DiskAnalyzer.ParseSize(minSizeRaw);
            if (minSize is null)
            {
                ctx.Logger.Error($"Ungültige Größenangabe für --min-size: '{minSizeRaw}' (z.B. 100MB, 2GB).");
                return 1;
            }
        }

        IReadOnlyCollection<string>? extensions = null;
        var typeRaw = ctx.Option("--type");
        if (typeRaw is not null)
        {
            extensions = ParseExtensions(typeRaw);
            if (extensions.Count == 0)
            {
                ctx.Logger.Error($"Ungültige Endungsangabe für --type: '{typeRaw}' (z.B. .jpg,.png).");
                return 1;
            }
        }

        int? ageDays = null;
        var ageRaw = ctx.Option("--age-days");
        if (ageRaw is not null)
        {
            if (!int.TryParse(ageRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d))
            {
                ctx.Logger.Error($"Ungültiger Wert für --age-days: '{ageRaw}' (ganze Zahl; negativ = jünger als).");
                return 1;
            }
            ageDays = d;
        }

        var filter = new DiskFilter
        {
            MinSizeBytes = minSize,
            Extensions   = extensions,
            AgeDays      = ageDays
        };

        // Export-Optionen.
        var exportRaw = ctx.Option("--export");
        string? exportFormat = null;
        if (exportRaw is not null)
        {
            exportFormat = exportRaw.Trim().ToLowerInvariant();
            if (exportFormat is not ("csv" or "html"))
            {
                ctx.Logger.Error($"Unbekanntes Export-Format: '{exportRaw}' (erlaubt: csv, html).");
                return 1;
            }
        }
        var outPath = ctx.Option("--out");
        if (outPath is not null && exportFormat is null)
        {
            ctx.Logger.Error("--out ohne --export ist wirkungslos. Bitte --export csv|html angeben.");
            return 1;
        }

        var snapshotPath = ctx.Option("--snapshot");
        if (snapshotPath is not null && byType)
        {
            ctx.Logger.Error("--snapshot ist nur im Top-Level-Modus möglich (nicht mit --by-type).");
            return 1;
        }

        // Interaktiver HTML-Treemap-Report (zusätzlich zur normalen Ausgabe).
        // Präsenz über beide Schreibweisen erkennen (--html pfad UND --html=pfad).
        string? htmlPath = null;
        bool htmlRequested = ctx.Args.Any(a =>
            string.Equals(a, "--html", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("--html=", StringComparison.OrdinalIgnoreCase));
        if (htmlRequested)
        {
            htmlPath = ctx.Option("--html");
            if (string.IsNullOrWhiteSpace(htmlPath) ||
                htmlPath.StartsWith("--", StringComparison.Ordinal))
            {
                ctx.Logger.Error("--html braucht einen Zielpfad, z.B. --html report.html.");
                return 1;
            }
        }

        var analyzer = new DiskAnalyzer(ctx.Logger);
        var activeFilter = filter.IsActive ? filter : null;

        // --fast: NTFS-Schnellscan (MFT/USN); fällt bei fehlenden Adminrechten,
        // Nicht-NTFS oder Fehlern automatisch auf den Standard-Scan zurück
        // (Meldung auf stderr). Ausgabeformat ist in beiden Fällen identisch.
        bool fast = ctx.HasFlag("--fast");
        var fastScanner = fast ? new NtfsFastScanner(ctx.Logger) : null;

        // ---- Modus: nach Endung gruppiert ----
        if (byType)
        {
            var ext = fastScanner?.TryAnalyzeByExtension(path, top, activeFilter)
                      ?? analyzer.AnalyzeByExtension(path, top, activeFilter);
            long extTotal = ext.TotalBytes;

            if (ctx.Json)
                JsonOut.Write(new { mode = "by-type", ext.TotalBytes, ext.Entries });
            else
            {
                var rows = ext.Entries.Select(e => new[]
                {
                    e.Extension,
                    DiskAnalyzer.FormatSize(e.Bytes),
                    e.Files.ToString(),
                    extTotal > 0 ? $"{(e.Bytes * 100.0 / extTotal):N1}%" : "-"
                });
                ConsoleTable.From(rows, "Endung", "Größe", "Dateien", "%").Write();
                Console.WriteLine($"\nGesamt (gefiltert): {DiskAnalyzer.FormatSize(extTotal)}");
            }

            if (htmlPath is not null)
            {
                var rcHtml = WriteHtmlReport(ctx, analyzer, path, filter, htmlPath);
                if (rcHtml != 0) return rcHtml;
            }

            if (exportFormat is not null)
                return ExportByType(ctx, path, ext, exportFormat, outPath);

            return 0;
        }

        // ---- Modus: Top-Level-Einträge (Standardverhalten) ----
        // Für einen Snapshot ALLE Einträge messen (nicht nur Top-N), damit der
        // spätere disk-diff auch kleine, aber gewachsene Pfade sieht; die
        // Anzeige bleibt bei Top-N.
        var topN = snapshotPath is null ? top : int.MaxValue;
        var analysis = fastScanner?.TryAnalyze(path, topN, activeFilter, depth)
                       ?? analyzer.Analyze(path, topN, activeFilter, depth);
        long total = analysis.TotalBytes;
        var shown = analysis.Entries.Take(top).ToList();

        if (ctx.Json)
        {
            JsonOut.Write(new { mode = "top-level", analysis.TotalBytes, Entries = shown });
        }
        else
        {
            var rows = shown.Select(e => new[]
            {
                e.IsDir ? "Ordner" : "Datei",
                e.Path,
                DiskAnalyzer.FormatSize(e.Bytes),
                e.Files.ToString(),
                total > 0 ? $"{(e.Bytes * 100.0 / total):N1}%" : "-"
            });
            ConsoleTable.From(rows, "Typ", "Pfad/Name", "Größe", "Dateien", "%").Write();
            Console.WriteLine($"\nGesamt (Top-Level): {DiskAnalyzer.FormatSize(total)}");
        }

        if (snapshotPath is not null)
        {
            try
            {
                DiskSnapshot.FromAnalysis(Path.GetFullPath(path), analysis).Save(snapshotPath);
                ctx.Logger.Info($"Snapshot gespeichert: {Path.GetFullPath(snapshotPath)} " +
                                $"({analysis.Entries.Count} Einträge). Vergleich: disk-diff <alt> <neu>.");
            }
            catch (Exception ex)
            {
                ctx.Logger.Error($"Snapshot konnte nicht gespeichert werden: {ex.Message}");
                return 2;
            }
        }

        if (htmlPath is not null)
        {
            var rcHtml = WriteHtmlReport(ctx, analyzer, path, filter, htmlPath);
            if (rcHtml != 0) return rcHtml;
        }

        if (exportFormat is not null)
        {
            // Export bleibt auch mit --snapshot auf die angezeigten Top-N begrenzt.
            var exportAnalysis = new DiskAnalysis { TotalBytes = total };
            exportAnalysis.Entries.AddRange(shown);
            return ExportTopLevel(ctx, path, exportAnalysis, exportFormat, outPath);
        }

        return 0;
    }

    // ---- Interaktiver HTML-Treemap-Report (--html) ----

    /// <summary>
    /// Schreibt den selbst-enthaltenen HTML-Report (Treemap + Tabellen). Nutzt
    /// denselben Filter wie die normale Ausgabe; Baum UND Endungs-Aufschlüsselung
    /// entstehen in einem einzigen Scan (<see cref="DiskAnalyzer.AnalyzeTree"/>).
    /// </summary>
    private static int WriteHtmlReport(CommandContext ctx, DiskAnalyzer analyzer, string root,
                                       DiskFilter filter, string htmlPath)
    {
        var activeFilter = filter.IsActive ? filter : null;
        var extensions = new ExtensionAnalysis();
        var tree = analyzer.AnalyzeTree(root, maxDepth: 4, activeFilter,
                                        extensionsOut: extensions, extensionsTopN: 20);

        var html = HtmlReportWriter.Build(new HtmlReportData
        {
            RootPath = Path.GetFullPath(root),
            GeneratedAt = DateTime.Now,
            Tree = tree,
            Extensions = extensions
        });

        try
        {
            File.WriteAllText(htmlPath, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            ctx.Logger.Info($"HTML-Report geschrieben: {Path.GetFullPath(htmlPath)}");
        }
        catch (Exception ex)
        {
            ctx.Logger.Error($"HTML-Report konnte nicht geschrieben werden: {ex.Message}");
            return 2;
        }
        return 0;
    }

    // ---- Export-Hilfen ----

    private int ExportTopLevel(CommandContext ctx, string root, DiskAnalysis analysis,
                              string format, string? outPath)
    {
        long total = analysis.TotalBytes;
        var headers = new[] { "Typ", "Pfad/Name", "Größe", "Bytes", "Dateien", "%" };
        var rows = analysis.Entries.Select(e => new[]
        {
            e.IsDir ? "Ordner" : "Datei",
            e.Path,
            DiskAnalyzer.FormatSize(e.Bytes),
            e.Bytes.ToString(CultureInfo.InvariantCulture),
            e.Files.ToString(CultureInfo.InvariantCulture),
            total > 0 ? $"{(e.Bytes * 100.0 / total):N1}%" : "-"
        }).ToList();

        return WriteReport(ctx, root, "analyze-disk", headers, rows,
                           total, format, outPath, "Top-Level");
    }

    private int ExportByType(CommandContext ctx, string root, ExtensionAnalysis analysis,
                            string format, string? outPath)
    {
        long total = analysis.TotalBytes;
        var headers = new[] { "Endung", "Größe", "Bytes", "Dateien", "%" };
        var rows = analysis.Entries.Select(e => new[]
        {
            e.Extension,
            DiskAnalyzer.FormatSize(e.Bytes),
            e.Bytes.ToString(CultureInfo.InvariantCulture),
            e.Files.ToString(CultureInfo.InvariantCulture),
            total > 0 ? $"{(e.Bytes * 100.0 / total):N1}%" : "-"
        }).ToList();

        return WriteReport(ctx, root, "analyze-disk-by-type", headers, rows,
                           total, format, outPath, "nach Endung");
    }

    private int WriteReport(CommandContext ctx, string root, string baseName,
                           string[] headers, List<string[]> rows, long total,
                           string format, string? outPath, string title)
    {
        // Sinnvoller Default-Dateiname, falls --out fehlt.
        var target = outPath;
        if (string.IsNullOrWhiteSpace(target))
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            target = $"{baseName}-{stamp}.{format}";
        }
        else if (!Path.HasExtension(target))
        {
            target = target + "." + format;
        }

        try
        {
            var content = format == "csv"
                ? BuildCsv(headers, rows)
                : BuildHtml(headers, rows, root, total, title);

            File.WriteAllText(target, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            ctx.Logger.Info($"Report geschrieben: {Path.GetFullPath(target)}");
        }
        catch (Exception ex)
        {
            ctx.Logger.Error($"Report konnte nicht geschrieben werden: {ex.Message}");
            return 2;
        }
        return 0;
    }

    private static string BuildCsv(string[] headers, List<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", r.Select(CsvEscape)));
        return sb.ToString();
    }

    private static string CsvEscape(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    private static string BuildHtml(string[] headers, List<string[]> rows,
                                    string root, long total, string title)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"de\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<title>WinCleaner – Speicheranalyse</title>");
        sb.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:1.5rem;}" +
                      "table{border-collapse:collapse;width:100%;}" +
                      "th,td{border:1px solid #ccc;padding:.4rem .6rem;text-align:left;}" +
                      "th{background:#f0f0f0;}tr:nth-child(even){background:#fafafa;}" +
                      "caption{font-weight:bold;margin-bottom:.6rem;text-align:left;}</style>");
        sb.AppendLine("</head><body>");
        sb.AppendLine($"<h1>WinCleaner – Speicheranalyse ({HtmlEscape(title)})</h1>");
        sb.AppendLine($"<p>Pfad: <code>{HtmlEscape(root)}</code><br>");
        sb.AppendLine($"Gesamt: {HtmlEscape(DiskAnalyzer.FormatSize(total))}<br>");
        sb.AppendLine($"Erstellt: {HtmlEscape(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))}</p>");
        sb.AppendLine("<table>");
        sb.Append("<tr>");
        foreach (var h in headers) sb.Append($"<th>{HtmlEscape(h)}</th>");
        sb.AppendLine("</tr>");
        foreach (var r in rows)
        {
            sb.Append("<tr>");
            foreach (var c in r) sb.Append($"<td>{HtmlEscape(c)}</td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</table></body></html>");
        return sb.ToString();
    }

    /// <summary>Maskiert HTML-Sonderzeichen, damit Pfade/Endungen sicher im Report stehen
    /// (geteilter Helfer mit dem Treemap-Report).</summary>
    private static string HtmlEscape(string value) => HtmlReportWriter.Esc(value);

    /// <summary>Zerlegt eine kommagetrennte Endungsliste in normalisierte Endungen (klein, mit Punkt).</summary>
    private static IReadOnlyCollection<string> ParseExtensions(string raw)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var e = part.ToLowerInvariant();
            if (!e.StartsWith('.')) e = "." + e;
            set.Add(e);
        }
        return set;
    }
}
