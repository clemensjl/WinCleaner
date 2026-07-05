using System.Windows.Input;
using WinCleaner.Gui.Mvvm;
using WinCleaner.Gui.Services;

namespace WinCleaner.Gui.ViewModels;

/// <summary>
/// Sicher löschen: unwiderrufliches Überschreiben einer Datei/eines Ordners
/// (shred) und Überschreiben des freien Speichers (wipe-free-space). Beides
/// IRREVERSIBEL – rot gekennzeichnet, mit deutlicher Extra-Bestätigung.
/// </summary>
public sealed class SecureDeleteViewModel : PageViewModelBase
{
    public override string Title => "Sicher löschen";
    public override string Glyph => Glyphs.Warning;

    private string _shredPath = "";
    public string ShredPath { get => _shredPath; set => SetProperty(ref _shredPath, value); }

    private int _passes = 3;
    public int Passes { get => _passes; set => SetProperty(ref _passes, value); }

    private string _wipeDrive = "C:";
    public string WipeDrive { get => _wipeDrive; set => SetProperty(ref _wipeDrive, value); }

    public ICommand ShredCommand { get; }
    public ICommand WipeCommand { get; }

    public SecureDeleteViewModel(ShellContext shell) : base(shell)
    {
        ShredCommand = new AsyncRelayCommand(ShredAsync);
        WipeCommand = new AsyncRelayCommand(WipeAsync);
    }

    private async Task ShredAsync() => await RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(ShredPath))
        {
            Dialogs.Info("Bitte eine Datei oder einen Ordner angeben.");
            return;
        }
        if (!Dialogs.ConfirmDanger(
                $"\"{ShredPath}\" mit {Passes} Durchgängen überschreiben und ENDGÜLTIG löschen?\n\n" +
                "Das ist UNWIDERRUFLICH – kein Papierkorb, keine Wiederherstellung."))
            return;

        Shell.Status("Überschreibe und lösche…");
        var r = await Cli.RunHiddenAsync($"shred {ElevatedCli.Quote(ShredPath)} --passes {Passes} --no-dry-run --yes");
        Shell.Status(r.Success ? "Sicher gelöscht." : "Fehlgeschlagen – siehe Meldung.");
        if (!string.IsNullOrWhiteSpace(r.Output)) Dialogs.Info(r.Output, "Sicher löschen");
    });

    private async Task WipeAsync() => await RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(WipeDrive))
        {
            Dialogs.Info("Bitte ein Laufwerk angeben (z. B. C:).");
            return;
        }
        if (!Dialogs.ConfirmDanger(
                $"Freien Speicher auf {WipeDrive} überschreiben?\n\n" +
                "Kann lange dauern und ist auf SSDs wirkungslos (die CLI warnt). " +
                "Belegte Dateien bleiben unangetastet."))
            return;

        Shell.Status("Überschreibe freien Speicher…");
        var r = await Task.Run(() => ElevatedCli.Run($"wipe-free-space {ElevatedCli.Quote(WipeDrive)} --no-dry-run --yes"));
        Shell.Status(r.Success ? "Freier Speicher überschrieben." : r.Error ?? "Abgebrochen.");
    });
}
