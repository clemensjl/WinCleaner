using System.Windows.Input;
using WinCleaner.Gui.Mvvm;
using WinCleaner.Gui.Services;

namespace WinCleaner.Gui.ViewModels;

/// <summary>
/// System: Wiederherstellungspunkt erstellen und die automatische
/// Junk-Bereinigung planen/entfernen (Task-Scheduler). Der Restore-Punkt
/// braucht Adminrechte (UAC).
/// </summary>
public sealed class SystemViewModel : PageViewModelBase
{
    public override string Title => "System";
    public override string Glyph => Glyphs.Restore;

    public ICommand RestorePointCommand { get; }
    public ICommand ScheduleWeeklyCommand { get; }
    public ICommand ScheduleDailyCommand { get; }
    public ICommand UnscheduleCommand { get; }

    public SystemViewModel(ShellContext shell) : base(shell)
    {
        RestorePointCommand = new AsyncRelayCommand(RestorePointAsync);
        ScheduleWeeklyCommand = new AsyncRelayCommand(() => ScheduleAsync("weekly"));
        ScheduleDailyCommand = new AsyncRelayCommand(() => ScheduleAsync("daily"));
        UnscheduleCommand = new AsyncRelayCommand(UnscheduleAsync);
    }

    private async Task RestorePointAsync() => await RunAsync(async () =>
    {
        if (!Dialogs.Confirm("Jetzt einen System-Wiederherstellungspunkt erstellen?"))
            return;
        Shell.Status("Erstelle Wiederherstellungspunkt (UAC)…");
        var r = await Task.Run(() => ElevatedCli.Run("create-restore-point \"WinCleaner GUI\""));
        Shell.Status(r.Success ? "Wiederherstellungspunkt erstellt." : r.Error ?? "Abgebrochen.");
    });

    private async Task ScheduleAsync(string interval) => await RunAsync(async () =>
    {
        Shell.Status($"Plane Bereinigung ({interval})…");
        var r = await Cli.RunHiddenAsync($"schedule-clean {interval}");
        Shell.Status(r.Success ? $"Bereinigung {interval} um 03:00 geplant." : "Planen: siehe Meldung.");
        if (!r.Success && !string.IsNullOrWhiteSpace(r.Output)) Dialogs.Info(r.Output, "Planen");
    });

    private async Task UnscheduleAsync() => await RunAsync(async () =>
    {
        Shell.Status("Entferne geplante Bereinigung…");
        var r = await Cli.RunHiddenAsync("unschedule-clean");
        Shell.Status(r.Success ? "Geplante Bereinigung entfernt." : "Entfernen: siehe Meldung.");
    });
}
