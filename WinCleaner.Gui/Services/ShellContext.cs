using System.Windows.Threading;
using WinCleaner.Core;

namespace WinCleaner.Gui.Services;

/// <summary>
/// Verbindung der Seiten zur Fensterschale: Statuszeile setzen, Busy-Anzeige
/// schalten und einen <see cref="Logger"/> liefern, dessen Meldungen in die
/// Statuszeile fließen. Alle UI-Zugriffe werden auf den UI-Thread gemarshallt,
/// weil Scans/Aktionen im Hintergrund laufen.
/// </summary>
public sealed class ShellContext
{
    private readonly Dispatcher _dispatcher;
    private readonly Action<string> _setStatus;
    private readonly Action<bool> _setBusy;

    public ShellContext(Dispatcher dispatcher, Action<string> setStatus, Action<bool> setBusy)
    {
        _dispatcher = dispatcher;
        _setStatus = setStatus;
        _setBusy = setBusy;
    }

    public void Status(string message) => OnUi(() => _setStatus(message));
    public void Busy(bool busy) => OnUi(() => _setBusy(busy));

    /// <summary>Logger, dessen Meldungen in der Statuszeile landen (statt auf stderr).</summary>
    public Logger NewLogger() => new((_, msg) => Status(msg));

    private void OnUi(Action action)
    {
        if (_dispatcher.CheckAccess()) action();
        else _dispatcher.Invoke(action);
    }
}
