using System.Collections.ObjectModel;
using System.Windows.Input;
using WinCleaner.Gui.Mvvm;
using WinCleaner.Gui.Services;
using WinCleaner.SystemTools;

namespace WinCleaner.Gui.ViewModels;

/// <summary>
/// Privatsphäre: Audit aller Telemetrie-/KI-Tweaks (nur lesen) plus Anwenden/
/// Rückgängig über die gehärtete CLI (die HKLM eleviert und vorher einen
/// Wiederherstellungspunkt anlegt). Auch Telemetrie-Blocking per hosts-Datei.
/// </summary>
public sealed class PrivacyViewModel : PageViewModelBase
{
    public override string Title => "Privatsphäre";
    public override string Glyph => Glyphs.Shield;

    public ObservableCollection<PrivacyRow> Items { get; } = new();

    private string _summary = "";
    public string Summary { get => _summary; set => SetProperty(ref _summary, value); }

    private bool _advanced;
    public bool Advanced { get => _advanced; set => SetProperty(ref _advanced, value); }

    private bool _loaded;

    public ICommand RefreshCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand BlockTelemetryCommand { get; }

    public PrivacyViewModel(ShellContext shell) : base(shell)
    {
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync);
        UndoCommand = new AsyncRelayCommand(UndoAsync);
        BlockTelemetryCommand = new AsyncRelayCommand(BlockTelemetryAsync);
    }

    public override void OnActivated()
    {
        if (!_loaded) RefreshCommand.Execute(null);
    }

    private async Task LoadAsync() => await RunAsync(async () =>
    {
        Shell.Status("Prüfe Privacy-Zustand…");
        var logger = Shell.NewLogger();
        var rows = await Task.Run(() =>
        {
            var engine = new TweakEngine(logger);
            return PrivacyTweaks.All.Select(e => new PrivacyRow(
                e.Tweak.Description, e.Profile.ToString(), e.Tweak.Hive.ToString(),
                engine.Status(e.Tweak))).ToList();
        });

        Items.Clear();
        foreach (var r in rows) Items.Add(r);
        int applied = rows.Count(r => r.Applied);
        Summary = $"{applied} von {rows.Count} Tweaks aktiv";
        _loaded = true;
        Shell.Status("Bereit.");
    });

    private async Task ApplyAsync() => await RunAsync(async () =>
    {
        string profile = Advanced ? "advanced" : "standard";
        if (!Dialogs.Confirm($"Privacy-Tweaks anwenden (Profil: {profile})?\n\n" +
                             "Umkehrbar; vor systemweiten Änderungen wird ein Wiederherstellungspunkt erstellt."))
            return;

        Shell.Status("Wende Privacy-Tweaks an…");
        var r = await Task.Run(() => ElevatedCli.Run($"privacy --apply {profile} --no-dry-run --yes"));
        Shell.Status(r.Success ? "Privacy-Tweaks angewendet." : r.Error ?? "Abgebrochen.");
        await LoadAsync();
    });

    private async Task UndoAsync() => await RunAsync(async () =>
    {
        if (!Dialogs.Confirm("Alle angewendeten Privacy-Tweaks rückgängig machen?"))
            return;

        Shell.Status("Mache Privacy-Tweaks rückgängig…");
        var r = await Task.Run(() => ElevatedCli.Run("privacy --undo --no-dry-run --yes"));
        Shell.Status(r.Success ? "Rückgängig gemacht." : r.Error ?? "Abgebrochen.");
        await LoadAsync();
    });

    private async Task BlockTelemetryAsync() => await RunAsync(async () =>
    {
        if (!Dialogs.Confirm("Microsoft-Telemetrie-Hosts über die hosts-Datei blocken?\n\n" +
                             "Reversibel (eigener, markierter Abschnitt mit Backup)."))
            return;

        Shell.Status("Blocke Telemetrie-Hosts…");
        var r = await Task.Run(() => ElevatedCli.Run("block-telemetry --apply --no-dry-run --yes"));
        Shell.Status(r.Success ? "Telemetrie-Hosts geblockt." : r.Error ?? "Abgebrochen.");
    });
}
