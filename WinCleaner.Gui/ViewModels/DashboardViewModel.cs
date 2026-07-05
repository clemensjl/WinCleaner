using System.Windows.Input;
using WinCleaner.Core;
using WinCleaner.Gui.Mvvm;
using WinCleaner.Gui.Services;
using WinCleaner.SystemTools;

namespace WinCleaner.Gui.ViewModels;

/// <summary>
/// Übersicht: kompakter Readout aus schnellen, rein lesenden Scans
/// (Junk-Größe, Autostart, Privacy) – der „Instrumententafel"-Blick.
/// </summary>
public sealed class DashboardViewModel : PageViewModelBase
{
    public override string Title => "Übersicht";
    public override string Glyph => Glyphs.Home;

    private string _junkValue = "–";
    public string JunkValue { get => _junkValue; set => SetProperty(ref _junkValue, value); }
    private string _junkSub = "noch nicht gemessen";
    public string JunkSub { get => _junkSub; set => SetProperty(ref _junkSub, value); }

    private string _startupValue = "–";
    public string StartupValue { get => _startupValue; set => SetProperty(ref _startupValue, value); }
    private string _startupSub = "";
    public string StartupSub { get => _startupSub; set => SetProperty(ref _startupSub, value); }

    private string _privacyValue = "–";
    public string PrivacyValue { get => _privacyValue; set => SetProperty(ref _privacyValue, value); }
    private string _privacySub = "";
    public string PrivacySub { get => _privacySub; set => SetProperty(ref _privacySub, value); }

    private bool _loaded;

    public ICommand RefreshCommand { get; }

    public DashboardViewModel(ShellContext shell) : base(shell)
    {
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
    }

    public override void OnActivated()
    {
        if (!_loaded) RefreshCommand.Execute(null);
    }

    private async Task LoadAsync() => await RunAsync(async () =>
    {
        Shell.Status("Messe Systemzustand…");
        var logger = Shell.NewLogger();

        var (junkBytes, junkCats, junkFiles, startTotal, startActive, privApplied, privTotal) =
            await Task.Run(() =>
            {
                var junk = new JunkScanner(logger).Scan();
                var startup = new StartupManager(logger).List();
                var engine = new TweakEngine(logger);
                int applied = PrivacyTweaks.All.Count(e => engine.Status(e.Tweak) == TweakStatus.Applied);
                return (junk.TotalBytes, junk.Items.Count, junk.TotalFiles,
                        startup.Count, startup.Count(s => s.Enabled),
                        applied, PrivacyTweaks.All.Count);
            });

        JunkValue = DiskAnalyzer.FormatSize(junkBytes);
        JunkSub = $"{junkCats} Kategorien · {junkFiles} Dateien";

        StartupValue = startActive.ToString();
        StartupSub = $"von {startTotal} Autostart-Einträgen aktiv";

        PrivacyValue = $"{privApplied}/{privTotal}";
        PrivacySub = "Privacy-Tweaks aktiv";

        _loaded = true;
        Shell.Status("Bereit.");
    });
}
