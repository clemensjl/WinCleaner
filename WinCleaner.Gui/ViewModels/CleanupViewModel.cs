using System.Collections.ObjectModel;
using System.Windows.Input;
using WinCleaner.Core;
using WinCleaner.Gui.Mvvm;
using WinCleaner.Gui.Services;

namespace WinCleaner.Gui.ViewModels;

/// <summary>
/// Aufraeumen: scannt Junk-Dateien (Vorschau) und bereinigt die ausgewaehlten
/// "Sicher"-Kategorien in den Papierkorb. Vorschau zuerst; echtes Loeschen erst
/// nach Bestaetigung.
/// </summary>
public sealed class CleanupViewModel : PageViewModelBase
{
    public override string Title => "Aufräumen";
    public override string Glyph => Glyphs.Delete;

    public ObservableCollection<JunkRow> Items { get; } = new();

    private string _summary = "Noch nicht gescannt.";
    public string Summary { get => _summary; set => SetProperty(ref _summary, value); }

    private bool _hasScanned;
    public bool HasScanned { get => _hasScanned; set => SetProperty(ref _hasScanned, value); }

    public ICommand ScanCommand { get; }
    public ICommand CleanCommand { get; }

    public CleanupViewModel(ShellContext shell) : base(shell)
    {
        ScanCommand = new AsyncRelayCommand(ScanAsync);
        CleanCommand = new AsyncRelayCommand(CleanAsync);
    }

    private async Task ScanAsync() => await RunAsync(async () =>
    {
        Shell.Status("Scanne Junk-Dateien…");
        var logger = Shell.NewLogger();
        var report = await Task.Run(() => new JunkScanner(logger).Scan());

        Items.Clear();
        foreach (var it in report.Items) Items.Add(new JunkRow(it));

        HasScanned = true;
        Summary = $"{report.Items.Count} Kategorien · {DiskAnalyzer.FormatSize(report.TotalBytes)} · {report.TotalFiles} Dateien";
        Shell.Status("Scan fertig.");
    });

    private async Task CleanAsync() => await RunAsync(async () =>
    {
        var selected = Items.Where(i => i.IsSelected && i.IsSafe).ToList();
        if (selected.Count == 0)
        {
            Dialogs.Info("Keine sicheren Kategorien ausgewählt.");
            return;
        }

        long bytes = selected.Sum(s => s.Bytes);
        if (!Dialogs.Confirm(
                $"{selected.Count} Kategorien ({DiskAnalyzer.FormatSize(bytes)}) in den Papierkorb verschieben?\n\n" +
                "Rückgängig über den Papierkorb möglich."))
            return;

        Shell.Status("Bereinige…");
        var logger = Shell.NewLogger();
        var report = new JunkReport();
        foreach (var s in selected) report.Items.Add(s.Item);

        await Task.Run(() => new JunkCleaner(logger).Clean(report, dryRun: false, sendToRecycleBin: true));
        Shell.Status($"{selected.Count} Kategorien bereinigt ({DiskAnalyzer.FormatSize(bytes)}).");

        await ScanAsync();
    });

    public override void OnActivated()
    {
        if (!HasScanned) ScanCommand.Execute(null);
    }
}
