using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using WinCleaner.Core;
using WinCleaner.Gui.Mvvm;
using WinCleaner.Gui.Services;

namespace WinCleaner.Gui.ViewModels;

/// <summary>
/// Speicher: größte Ordner/Dateien eines Pfads analysieren und inhaltsgleiche
/// Duplikate finden. Beides rein lesend.
/// </summary>
public sealed class StorageViewModel : PageViewModelBase
{
    public override string Title => "Speicher";
    public override string Glyph => Glyphs.Storage;

    private string _path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string Path { get => _path; set => SetProperty(ref _path, value); }

    public ObservableCollection<DiskRow> DiskItems { get; } = new();
    public ObservableCollection<DupRow> Duplicates { get; } = new();

    private string _diskSummary = "";
    public string DiskSummary { get => _diskSummary; set => SetProperty(ref _diskSummary, value); }
    private string _dupSummary = "";
    public string DupSummary { get => _dupSummary; set => SetProperty(ref _dupSummary, value); }

    public ICommand AnalyzeCommand { get; }
    public ICommand DuplicatesCommand { get; }

    public StorageViewModel(ShellContext shell) : base(shell)
    {
        AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync);
        DuplicatesCommand = new AsyncRelayCommand(FindDuplicatesAsync);
    }

    private async Task AnalyzeAsync() => await RunAsync(async () =>
    {
        if (!Directory.Exists(Path)) { Dialogs.Info("Pfad nicht gefunden: " + Path); return; }

        Shell.Status("Analysiere Speicher…");
        var logger = Shell.NewLogger();
        var analysis = await Task.Run(() => new DiskAnalyzer(logger).Analyze(Path, 50));

        DiskItems.Clear();
        foreach (var e in analysis.Entries) DiskItems.Add(new DiskRow(e, analysis.TotalBytes));
        DiskSummary = $"Gesamt: {DiskAnalyzer.FormatSize(analysis.TotalBytes)}";
        Shell.Status("Analyse fertig.");
    });

    private async Task FindDuplicatesAsync() => await RunAsync(async () =>
    {
        if (!Directory.Exists(Path)) { Dialogs.Info("Pfad nicht gefunden: " + Path); return; }

        Shell.Status("Suche Duplikate…");
        var logger = Shell.NewLogger();
        var groups = await Task.Run(() => new DuplicateFinder(logger).Find(Path));

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
}
