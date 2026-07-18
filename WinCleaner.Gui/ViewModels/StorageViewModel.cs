using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Input;
using WinCleaner.Core;
using WinCleaner.Gui.Mvvm;
using WinCleaner.Gui.Services;

namespace WinCleaner.Gui.ViewModels;

/// <summary>
/// Speicher: größte Ordner/Dateien eines Pfads analysieren (optional per
/// NTFS-Schnellscan), interaktiver HTML-Report, inhaltsgleiche Duplikate finden
/// und durch Hardlinks ersetzen sowie visuell ähnliche Bilder aufspüren.
/// Scans sind rein lesend; verändernde Aktionen laufen Preview-first mit
/// expliziter Bestätigung und Papierkorb.
/// </summary>
public sealed class StorageViewModel : PageViewModelBase
{
    public override string Title => "Speicher";
    public override string Glyph => Glyphs.Storage;

    /// <summary>Anzahl angezeigter Analyse-Einträge (wie bisher).</summary>
    private const int TopCount = 50;

    private string _path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string Path { get => _path; set => SetProperty(ref _path, value); }

    private bool _fastScan;
    public bool FastScan { get => _fastScan; set => SetProperty(ref _fastScan, value); }

    public ObservableCollection<DiskRow> DiskItems { get; } = new();
    public ObservableCollection<DupRow> Duplicates { get; } = new();
    public ObservableCollection<ImageGroupRow> ImageGroups { get; } = new();

    private string _diskSummary = "";
    public string DiskSummary { get => _diskSummary; set => SetProperty(ref _diskSummary, value); }
    private string _dupSummary = "";
    public string DupSummary { get => _dupSummary; set => SetProperty(ref _dupSummary, value); }
    private string _imageSummary = "";
    public string ImageSummary { get => _imageSummary; set => SetProperty(ref _imageSummary, value); }

    // ---- Behalte-Strategie (Duplikate bzw. Bild-Duplikate) ----

    public IReadOnlyList<KeepStrategyOption> KeepStrategies => StorageLogic.KeepStrategies;

    private KeepStrategyOption _dupKeep = StorageLogic.KeepStrategies[0];
    public KeepStrategyOption DupKeep { get => _dupKeep; set => SetProperty(ref _dupKeep, value); }

    private KeepStrategyOption _imageKeep = StorageLogic.KeepStrategies[0];
    public KeepStrategyOption ImageKeep { get => _imageKeep; set => SetProperty(ref _imageKeep, value); }

    // ---- Bild-Duplikate ----

    private string _imagePath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    public string ImagePath { get => _imagePath; set => SetProperty(ref _imagePath, value); }

    private int _imageThreshold = 5;
    public int ImageThreshold { get => _imageThreshold; set => SetProperty(ref _imageThreshold, value); }

    private bool _imageRecurse = true;
    public bool ImageRecurse { get => _imageRecurse; set => SetProperty(ref _imageRecurse, value); }

    public ICommand AnalyzeCommand { get; }
    public ICommand HtmlReportCommand { get; }
    public ICommand DuplicatesCommand { get; }
    public ICommand HardLinkCommand { get; }
    public ICommand FindImagesCommand { get; }
    public ICommand DeleteImagesCommand { get; }

    /// <summary>Rohdaten des letzten Duplikat-Scans (für die Hardlink-Aktion).</summary>
    private List<DuplicateGroup> _dupGroups = new();
    /// <summary>Rohdaten des letzten Bilder-Scans (für den Lösch-Flow).</summary>
    private List<SimilarImageGroup> _imageGroups = new();

    public StorageViewModel(ShellContext shell) : base(shell)
    {
        AnalyzeCommand      = new AsyncRelayCommand(AnalyzeAsync);
        HtmlReportCommand   = new AsyncRelayCommand(HtmlReportAsync);
        DuplicatesCommand   = new AsyncRelayCommand(FindDuplicatesAsync);
        HardLinkCommand     = new AsyncRelayCommand(HardLinkAsync, () => _dupGroups.Count > 0);
        FindImagesCommand   = new AsyncRelayCommand(FindImagesAsync);
        DeleteImagesCommand = new AsyncRelayCommand(DeleteImagesAsync, () => _imageGroups.Count > 0);
    }

    // ---- Speicheranalyse (Standard oder NTFS-Schnellscan) ----

    private async Task AnalyzeAsync() => await RunAsync(async () =>
    {
        string path = Path;
        bool fastScan = FastScan;
        var logger = Shell.NewLogger();
        DiskAnalysis? analysis = null;
        string mode = "Standard-Scan";

        // Directory.Exists und IsSupported machen Datei-I/O und können z. B. bei
        // einer schlafenden HDD sekundenlang blockieren — nie auf dem
        // Dispatcher-Thread ausführen, sondern im Hintergrund prüfen.
        Shell.Status("Prüfe Pfad…");
        var pre = await Task.Run<(bool Exists, bool FastOk, string Reason, FastScanBlockReason Block)>(() =>
        {
            if (!Directory.Exists(path))
                return (false, false, "", FastScanBlockReason.NotFound);
            if (!fastScan)
                return (true, false, "", FastScanBlockReason.None);
            bool ok = NtfsFastScanner.IsSupported(path, out string reason, out var block);
            return (true, ok, reason, block);
        });
        if (!pre.Exists) { Dialogs.Info("Pfad nicht gefunden: " + path); return; }

        if (fastScan)
        {
            if (pre.FastOk)
            {
                // Prozess läuft bereits mit Adminrechten: Schnellscan direkt in-process.
                Shell.Status("Schneller NTFS-Scan…");
                analysis = await Task.Run(() => new NtfsFastScanner(logger).TryAnalyze(path, TopCount));
                if (analysis is not null) mode = "NTFS-Schnellscan";
            }
            else if (pre.Block == FastScanBlockReason.NeedsAdmin)
            {
                // Einziges Hindernis sind fehlende Adminrechte (die Prüfung testet
                // Rechte zuletzt) -> bewährter Admin-Pfad der GUI: CLI per UAC
                // starten, Ergebnis über die Snapshot-Datei zurückholen. Die GUI
                // selbst bleibt ohne Elevation (asInvoker).
                analysis = await RunFastScanElevatedAsync();
                if (analysis is not null) mode = "NTFS-Schnellscan";
            }
            else
            {
                Shell.Status($"Schnellscan nicht möglich ({pre.Reason}) – Standard-Scan läuft…");
            }
        }

        if (analysis is null)
        {
            if (fastScan) mode = "Standard-Scan (Fallback)";
            else Shell.Status("Analysiere Speicher…");
            analysis = await Task.Run(() => new DiskAnalyzer(logger).Analyze(path, TopCount));
        }

        DiskItems.Clear();
        foreach (var e in analysis.Entries) DiskItems.Add(new DiskRow(e, analysis.TotalBytes));
        DiskSummary = $"Gesamt: {DiskAnalyzer.FormatSize(analysis.TotalBytes)} · {mode}";
        Shell.Status($"Analyse fertig ({mode}).");
    });

    /// <summary>
    /// NTFS-Schnellscan über die elevated gestartete CLI (UAC). stdout eines
    /// runas-Prozesses lässt sich nicht umleiten, daher schreibt die CLI einen
    /// Snapshot in eine Temp-Datei, den die GUI anschließend einliest.
    /// Liefert null, wenn der Lauf scheitert oder abgelehnt wird (dann Fallback).
    /// </summary>
    private async Task<DiskAnalysis?> RunFastScanElevatedAsync()
    {
        // Preflight OHNE Elevation: kennt die aufgelöste CLI --fast überhaupt?
        // Eine ältere installierte CLI (< 2.1.0) würde sonst erst NACH dem
        // UAC-Prompt mit "Unbekannte Option" scheitern.
        Shell.Status("Prüfe installierte WinCleaner-CLI…");
        bool cliOk = await Task.Run(() => ProbeCliSupportsFastScan(ElevatedCli.ResolveCliPath()));
        if (!cliOk)
        {
            Shell.Status("Schnellscan nicht möglich: installierte WinCleaner-CLI ist zu alt " +
                         "(braucht mindestens 2.1.0) – Standard-Scan läuft…");
            return null;
        }

        string snapFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"wincleaner-fastscan-{Guid.NewGuid():N}.json");
        try
        {
            Shell.Status("Schneller NTFS-Scan – Adminrechte werden angefordert (UAC)…");
            var r = await Task.Run(() =>
                ElevatedCli.Run(StorageLogic.BuildFastScanArguments(Path, TopCount, snapFile)));

            if (!r.Success || !File.Exists(snapFile))
            {
                Shell.Status($"Schnellscan nicht möglich " +
                             $"({r.Error ?? $"CLI-Lauf fehlgeschlagen (ExitCode {r.ExitCode})"}) " +
                             "– Standard-Scan läuft…");
                return null;
            }

            var snapshot = await Task.Run(() => DiskSnapshot.Load(snapFile));
            return StorageLogic.ToAnalysis(snapshot, TopCount);
        }
        catch (Exception ex)
        {
            Shell.Status($"Schnellscan nicht möglich ({ex.Message}) – Standard-Scan läuft…");
            return null;
        }
        finally
        {
            try { if (File.Exists(snapFile)) File.Delete(snapFile); } catch { /* Temp-Rest */ }
        }
    }

    /// <summary>
    /// Fragt die CLI nicht-elevated nach ihrer analyze-disk-Hilfe und prüft per
    /// <see cref="StorageLogic.CliSupportsFastScan"/>, ob sie --fast kennt.
    /// Fehler oder Timeout (~5 s) gelten konservativ als "kennt --fast nicht".
    /// </summary>
    private static bool ProbeCliSupportsFastScan(string cliPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = cliPath,
                Arguments              = "--help analyze-disk",
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                CreateNoWindow         = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return false;

            var stdout = proc.StandardOutput.ReadToEndAsync();
            if (!proc.WaitForExit(5000))
            {
                try { proc.Kill(); } catch { /* schon beendet */ }
                return false;
            }
            return StorageLogic.CliSupportsFastScan(
                stdout.Wait(1000) ? stdout.Result : null);
        }
        catch
        {
            return false;
        }
    }

    // ---- HTML-Report (Treemap) ----

    private async Task HtmlReportAsync() => await RunAsync(async () =>
    {
        if (!Directory.Exists(Path)) { Dialogs.Info("Pfad nicht gefunden: " + Path); return; }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title      = "HTML-Report speichern",
            Filter     = "HTML-Report (*.html)|*.html",
            DefaultExt = ".html",
            FileName   = StorageLogic.DefaultReportFileName(DateTime.Now)
        };
        if (dialog.ShowDialog() != true) return;
        string target = dialog.FileName;

        Shell.Status("Erstelle HTML-Report (kompletter Scan, kann dauern)…");
        var logger = Shell.NewLogger();
        string root = Path;

        // Kein Admin nötig: Baum-Scan + Report-Erzeugung direkt über Core im
        // Hintergrund-Thread, wie bei den anderen Nicht-Admin-Operationen.
        await Task.Run(() =>
        {
            var extensions = new ExtensionAnalysis();
            var tree = new DiskAnalyzer(logger).AnalyzeTree(root, maxDepth: 4,
                extensionsOut: extensions, extensionsTopN: 20);

            var html = HtmlReportWriter.Build(new HtmlReportData
            {
                RootPath    = System.IO.Path.GetFullPath(root),
                GeneratedAt = DateTime.Now,
                Tree        = tree,
                Extensions  = extensions
            });
            File.WriteAllText(target, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        });

        Shell.Status($"HTML-Report erstellt: {target}");
        // Im Standardbrowser öffnen.
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    });

    // ---- Duplikate ----

    private async Task FindDuplicatesAsync() => await RunAsync(async () =>
    {
        if (!Directory.Exists(Path)) { Dialogs.Info("Pfad nicht gefunden: " + Path); return; }

        Shell.Status("Suche Duplikate…");
        var logger = Shell.NewLogger();
        var groups = await Task.Run(() => new DuplicateFinder(logger).Find(Path));

        _dupGroups = groups;
        Duplicates.Clear();
        long wasted = 0;
        foreach (var g in groups)
        {
            Duplicates.Add(new DupRow(g));
            if (g.Files.Count > 1)
                wasted += g.TotalBytes / g.Files.Count * (g.Files.Count - 1);
        }
        DupSummary = groups.Count == 0
            ? "Keine Duplikate gefunden."
            : $"{groups.Count} Gruppen · {DiskAnalyzer.FormatSize(wasted)} durch Kopien belegt";
        Shell.Status("Duplikatsuche fertig.");
    });

    /// <summary>
    /// Ersetzt Duplikate durch NTFS-Hardlinks – Preview-first: erst Probelauf
    /// (geplante Links, Ersparnis, Hindernisse), dann explizite Bestätigung,
    /// dann echte Ausführung. Ersetzte Originale wandern in den Papierkorb.
    /// </summary>
    private async Task HardLinkAsync() => await RunAsync(async () =>
    {
        if (_dupGroups.Count == 0) { Dialogs.Info("Zuerst Duplikate suchen."); return; }

        var keep = DupKeep.Value;
        var logger = Shell.NewLogger();

        Shell.Status("Probelauf: prüfe Hardlink-Ersetzung…");
        var plan = await Task.Run(() => new DuplicateFinder(logger).ProcessDuplicates(
            _dupGroups, keep, hardLink: true, dryRun: true));

        if (plan.FilesAffected == 0)
        {
            Shell.Status("Keine Duplikate für die Hardlink-Ersetzung geeignet.");
            Dialogs.Info(plan.FilesSkipped > 0
                ? $"Nichts zu ersetzen: {plan.FilesSkipped} Dateien nicht geeignet " +
                  "(z. B. anderes Volume, kein NTFS, bereits verlinkt oder Datei-Identität nicht lesbar)."
                : "Nichts zu ersetzen.", "Durch Hardlinks ersetzen");
            return;
        }

        if (!Dialogs.Confirm(StorageLogic.BuildHardLinkPreview(plan), "Durch Hardlinks ersetzen"))
        {
            Shell.Status("Hardlink-Ersetzung abgebrochen.");
            return;
        }

        Shell.Status("Ersetze Duplikate durch Hardlinks…");
        var result = await Task.Run(() => new DuplicateFinder(logger).ProcessDuplicates(
            _dupGroups, keep, hardLink: true, sendToRecycleBin: true, dryRun: false));

        // Liste zurücksetzen: verlinkte Paare wären beim erneuten Scan weiter
        // inhaltsgleich und würden als "Verschwendung" fehlgedeutet.
        _dupGroups = new List<DuplicateGroup>();
        Duplicates.Clear();
        DupSummary = StorageLogic.BuildHardLinkResultText(result);
        Shell.Status(DupSummary);
        Dialogs.Info(StorageLogic.BuildHardLinkResultText(result), "Durch Hardlinks ersetzen");
    });

    // ---- Bild-Duplikate ----

    private async Task FindImagesAsync() => await RunAsync(async () =>
    {
        if (!Directory.Exists(ImagePath)) { Dialogs.Info("Pfad nicht gefunden: " + ImagePath); return; }

        Shell.Status("Suche ähnliche Bilder…");
        var logger = Shell.NewLogger();
        string root = ImagePath; bool recurse = ImageRecurse; int threshold = ImageThreshold;
        var groups = await Task.Run(() => new ImageSimilarityFinder(logger)
            .Find(root, recurse, threshold));

        _imageGroups = groups;
        ImageGroups.Clear();
        foreach (var g in groups) ImageGroups.Add(new ImageGroupRow(g));
        ImageSummary = groups.Count == 0
            ? "Keine ähnlichen Bilder gefunden."
            : $"{groups.Count} Gruppen · {DiskAnalyzer.FormatSize(groups.Sum(g => g.TotalBytes))} betroffen " +
              $"· Schwelle {threshold}";
        Shell.Status("Bildersuche fertig.");
    });

    /// <summary>
    /// Verschiebt ähnliche Bilder in den Papierkorb – gleicher Bestätigungs-/
    /// Papierkorb-Flow wie bei Duplikaten (<see cref="DuplicateFinder.ProcessDuplicates"/>):
    /// Probelauf, Bestätigung, echte Ausführung, danach frischer Scan.
    /// </summary>
    private async Task DeleteImagesAsync() => await RunAsync(async () =>
    {
        if (_imageGroups.Count == 0) { Dialogs.Info("Zuerst Bilder scannen."); return; }

        var keep = ImageKeep.Value;
        var logger = Shell.NewLogger();
        var dupGroups = StorageLogic.ToDuplicateGroups(_imageGroups);

        Shell.Status("Probelauf: prüfe Verschieben in den Papierkorb…");
        var plan = await Task.Run(() => new DuplicateFinder(logger).ProcessDuplicates(
            dupGroups, keep, hardLink: false, sendToRecycleBin: true, dryRun: true));

        if (plan.FilesAffected == 0)
        {
            Shell.Status("Keine Bilder zu verschieben.");
            Dialogs.Info("Nichts zu verschieben.", "Ähnliche Bilder");
            return;
        }

        if (!Dialogs.Confirm(StorageLogic.BuildImageDeletePreview(plan), "Ähnliche Bilder"))
        {
            Shell.Status("Verschieben abgebrochen.");
            return;
        }

        Shell.Status("Verschiebe ähnliche Bilder in den Papierkorb…");
        var result = await Task.Run(() => new DuplicateFinder(logger).ProcessDuplicates(
            dupGroups, keep, hardLink: false, sendToRecycleBin: true, dryRun: false));

        Shell.Status(StorageLogic.BuildImageDeleteResultText(result));
        Dialogs.Info(StorageLogic.BuildImageDeleteResultText(result), "Ähnliche Bilder");

        await FindImagesAsync(); // Ansicht auffrischen
    });
}
