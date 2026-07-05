using System.Collections.ObjectModel;
using System.Windows.Input;
using WinCleaner.Gui.Mvvm;
using WinCleaner.Gui.Services;
using WinCleaner.SystemTools;

namespace WinCleaner.Gui.ViewModels;

/// <summary>
/// Programme: installierte Programme auflisten/suchen, das gewählte über die CLI
/// deinstallieren, sowie Bloatware entfernen und Paket-Updates prüfen/einspielen.
/// Deinstallation/Debloat/Update laufen über die gehärtete CLI.
/// </summary>
public sealed class ProgramsViewModel : PageViewModelBase
{
    public override string Title => "Programme";
    public override string Glyph => Glyphs.Apps;

    private readonly List<ProgramRow> _all = new();

    public ObservableCollection<ProgramRow> Items { get; } = new();

    private string _search = "";
    public string Search
    {
        get => _search;
        set { if (SetProperty(ref _search, value)) ApplyFilter(); }
    }

    private ProgramRow? _selected;
    public ProgramRow? Selected { get => _selected; set => SetProperty(ref _selected, value); }

    private bool _loaded;

    public ICommand RefreshCommand { get; }
    public ICommand UninstallCommand { get; }
    public ICommand DebloatCommand { get; }
    public ICommand UpdatesCommand { get; }

    public ProgramsViewModel(ShellContext shell) : base(shell)
    {
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        UninstallCommand = new AsyncRelayCommand(UninstallAsync);
        DebloatCommand = new AsyncRelayCommand(DebloatAsync);
        UpdatesCommand = new AsyncRelayCommand(UpdatesAsync);
    }

    public override void OnActivated()
    {
        if (!_loaded) RefreshCommand.Execute(null);
    }

    private async Task LoadAsync() => await RunAsync(async () =>
    {
        Shell.Status("Lese installierte Programme…");
        var logger = Shell.NewLogger();
        var list = await Task.Run(() => new ProgramInventory(logger).List());

        _all.Clear();
        _all.AddRange(list.Select(p => new ProgramRow(p)));
        _loaded = true;
        ApplyFilter();
        Shell.Status($"{_all.Count} Programme.");
    });

    private void ApplyFilter()
    {
        Items.Clear();
        IEnumerable<ProgramRow> q = _all;
        if (!string.IsNullOrWhiteSpace(Search))
            q = _all.Where(p => p.Name.Contains(Search, StringComparison.OrdinalIgnoreCase));
        foreach (var p in q) Items.Add(p);
    }

    private async Task UninstallAsync() => await RunAsync(async () =>
    {
        if (Selected is null) { Dialogs.Info("Bitte zuerst ein Programm auswählen."); return; }
        if (!Dialogs.Confirm($"\"{Selected.Name}\" deinstallieren?\n\nEs wird ein Wiederherstellungspunkt " +
                             "erstellt; der Deinstaller des Programms öffnet sich."))
            return;

        Shell.Status($"Deinstalliere {Selected.Name}…");
        var r = await Cli.RunHiddenAsync($"uninstall {ElevatedCli.Quote(Selected.Name)} --no-dry-run --yes");
        Shell.Status(r.Success ? "Deinstallation abgeschlossen." : "Deinstallation: siehe Meldung.");
        if (!string.IsNullOrWhiteSpace(r.Output)) Dialogs.Info(r.Output, "Deinstallation");
        await LoadAsync();
    });

    private async Task DebloatAsync() => await RunAsync(async () =>
    {
        // Erst Vorschau (Dry-Run) zeigen.
        Shell.Status("Ermittle entfernbare Bloatware…");
        var preview = await Cli.RunHiddenAsync("debloat --list");
        if (!Dialogs.Confirm("Kuratierte vorinstallierte Apps entfernen?\n\n" +
                             "Aus dem Store wieder installierbar; vorher wird ein Wiederherstellungspunkt erstellt.\n\n" +
                             Trim(preview.Output)))
            return;

        Shell.Status("Entferne Bloatware…");
        var r = await Task.Run(() => ElevatedCli.Run("debloat --no-dry-run --yes")); // Restore-Point braucht Admin
        Shell.Status(r.Success ? "Bloatware entfernt." : r.Error ?? "Abgebrochen.");
    });

    private async Task UpdatesAsync() => await RunAsync(async () =>
    {
        Shell.Status("Suche Paket-Updates (winget)…");
        var r = await Cli.RunHiddenAsync("list-updates");
        Dialogs.Info(string.IsNullOrWhiteSpace(r.Output) ? "Keine Updates gefunden." : r.Output, "Verfügbare Updates");
        Shell.Status("Update-Prüfung fertig.");
    });

    private static string Trim(string s) => s.Length > 1500 ? s[..1500] + "…" : s;
}
