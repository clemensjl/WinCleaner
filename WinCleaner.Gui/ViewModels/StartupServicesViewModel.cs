using System.Collections.ObjectModel;
using System.Windows.Input;
using WinCleaner.Gui.Mvvm;
using WinCleaner.Gui.Services;
using WinCleaner.SystemTools;

namespace WinCleaner.Gui.ViewModels;

/// <summary>
/// Autostart &amp; Dienste: Autostart-Einträge anzeigen und reversibel
/// deaktivieren (HKCU direkt, HKLM via UAC-CLI), plus das kuratierte, sichere
/// Dienste-Profil über die CLI.
/// </summary>
public sealed class StartupServicesViewModel : PageViewModelBase
{
    public override string Title => "Autostart & Dienste";
    public override string Glyph => Glyphs.Settings;

    public ObservableCollection<StartupRow> Items { get; } = new();

    private StartupRow? _selected;
    public StartupRow? Selected { get => _selected; set => SetProperty(ref _selected, value); }

    private bool _loaded;

    public ICommand RefreshCommand { get; }
    public ICommand DisableCommand { get; }
    public ICommand SafeServicesCommand { get; }

    public StartupServicesViewModel(ShellContext shell) : base(shell)
    {
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        DisableCommand = new AsyncRelayCommand(DisableAsync);
        SafeServicesCommand = new AsyncRelayCommand(SafeServicesAsync);
    }

    public override void OnActivated()
    {
        if (!_loaded) RefreshCommand.Execute(null);
    }

    private async Task LoadAsync() => await RunAsync(async () =>
    {
        Shell.Status("Lese Autostart-Einträge…");
        var logger = Shell.NewLogger();
        var list = await Task.Run(() => new StartupManager(logger).List());

        Items.Clear();
        foreach (var i in list) Items.Add(new StartupRow(i));
        _loaded = true;
        Shell.Status($"{Items.Count} Autostart-Einträge.");
    });

    private async Task DisableAsync() => await RunAsync(async () =>
    {
        if (Selected is null) { Dialogs.Info("Bitte zuerst einen Eintrag auswählen."); return; }
        if (!Selected.Enabled) { Dialogs.Info($"\"{Selected.Name}\" ist bereits deaktiviert."); return; }
        if (!Dialogs.Confirm($"Autostart \"{Selected.Name}\" deaktivieren?\n\n" +
                             "Reversibel – im Task-Manager oder hier wieder aktivierbar."))
            return;

        var name = Selected.Name;
        Shell.Status($"Deaktiviere {name}…");
        var logger = Shell.NewLogger();
        var result = await Task.Run(() => new StartupManager(logger).Disable(name));

        if (result == DisableResult.NeedsAdmin)
        {
            // HKLM/Common: über die CLI mit UAC.
            var r = await Task.Run(() => ElevatedCli.Run($"startup-disable {ElevatedCli.Quote(name)}"));
            Shell.Status(r.Success ? $"\"{name}\" deaktiviert." : r.Error ?? "Abgebrochen.");
        }
        else
        {
            Shell.Status(result switch
            {
                DisableResult.Success        => $"\"{name}\" deaktiviert.",
                DisableResult.AlreadyDisabled=> "War bereits deaktiviert.",
                DisableResult.NotFound       => "Eintrag nicht gefunden.",
                _                            => "Deaktivieren fehlgeschlagen."
            });
        }
        await LoadAsync();
    });

    private async Task SafeServicesAsync() => await RunAsync(async () =>
    {
        Shell.Status("Ermittle Dienste-Profil…");
        var preview = await Cli.RunHiddenAsync("services --profile safe-disable");
        if (!Dialogs.Confirm("Kuratiertes, sicheres Dienste-Profil anwenden?\n\n" +
                             "Setzt unkritische Dienste reversibel auf Manuell/Deaktiviert (mit Backup).\n\n" +
                             Trim(preview.Output)))
            return;

        Shell.Status("Wende Dienste-Profil an…");
        var r = await Task.Run(() => ElevatedCli.Run("services --profile safe-disable --no-dry-run --yes"));
        Shell.Status(r.Success ? "Dienste-Profil angewendet." : r.Error ?? "Abgebrochen.");
    });

    private static string Trim(string s) => s.Length > 1500 ? s[..1500] + "…" : s;
}
