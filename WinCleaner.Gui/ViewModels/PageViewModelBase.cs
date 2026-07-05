using WinCleaner.Gui.Mvvm;
using WinCleaner.Gui.Services;

namespace WinCleaner.Gui.ViewModels;

/// <summary>
/// Basis jeder Navigationsseite: Titel + Icon-Glyph für die Navigationsleiste,
/// Zugriff auf die Fensterschale (<see cref="ShellContext"/>) und ein Helfer,
/// der Hintergrundarbeit mit Busy-Anzeige und Fehlermeldung kapselt.
/// </summary>
public abstract class PageViewModelBase : ViewModelBase
{
    protected ShellContext Shell { get; }

    protected PageViewModelBase(ShellContext shell) => Shell = shell;

    /// <summary>Anzeigename in der Navigation.</summary>
    public abstract string Title { get; }

    /// <summary>Segoe-Fluent-Icons-Glyph für die Navigation.</summary>
    public abstract string Glyph { get; }

    /// <summary>Wird beim Wechsel auf diese Seite aufgerufen (z. B. für ein Auto-Laden).</summary>
    public virtual void OnActivated() { }

    /// <summary>
    /// Führt <paramref name="work"/> aus, schaltet dabei die Busy-Anzeige und
    /// leitet Ausnahmen in die Statuszeile (statt sie zu verschlucken).
    /// </summary>
    protected async Task RunAsync(Func<Task> work)
    {
        Shell.Busy(true);
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            Shell.Status($"Fehler: {ex.Message}");
        }
        finally
        {
            Shell.Busy(false);
        }
    }
}
