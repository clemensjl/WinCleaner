namespace WinCleaner.Core;

public class Logger
{
    /// <summary>
    /// Optionale Ausgabesenke (Level, Nachricht). Ist sie gesetzt, gehen alle
    /// Meldungen dorthin statt nach <see cref="Console.Error"/> – so kann die GUI
    /// Meldungen in ihre Statusleiste leiten. Ohne Sink bleibt das bisherige
    /// stderr-Verhalten (abwärtskompatibel; CLI/Tests unverändert).
    /// </summary>
    private readonly Action<string, string>? _sink;

    public Logger(Action<string, string>? sink = null) => _sink = sink;

    public void Info(string msg)  => Write("INFO", msg);
    public void Error(string msg) => Write("ERROR", msg);
    public void Debug(string msg)
    {
#if DEBUG
        Write("DEBUG", msg);
#endif
    }

    protected virtual void Write(string level, string msg)
    {
        if (_sink is not null) { _sink(level, msg); return; }

        // Diagnostik nach stderr, damit stdout (Tabellen/JSON) maschinenlesbar bleibt.
        Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss}] {level}  {msg}");
    }
}
