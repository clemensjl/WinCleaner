namespace WinCleaner.Commands;

/// <summary>
/// Ein WinCleaner-Unterbefehl. Jede Implementierung mit parameterlosem
/// Konstruktor wird von <see cref="CommandRegistry"/> per Reflection automatisch
/// erkannt – ein neuer Befehl = eine neue Datei, kein zentrales Registrieren
/// und keine Änderung an Program.cs nötig.
/// </summary>
public interface ICommand
{
    /// <summary>Befehlsname auf der Kommandozeile, z. B. "scan-junk".</summary>
    string Name { get; }

    /// <summary>Einzeilige Beschreibung für die Hilfe.</summary>
    string Summary { get; }

    /// <summary>
    /// Argument-Syntax für die Hilfe, z. B. "&lt;Pfad&gt; [--delete]".
    /// Leerstring, wenn der Befehl keine Argumente nimmt.
    /// </summary>
    string Usage { get; }

    /// <summary>
    /// Befehls-spezifische erlaubte <c>--flags</c> (z. B. "--yes", "--delete").
    /// Wird zur Tippfehler-Ablehnung genutzt. Globale Flags (--json, --help, -h,
    /// --relaunched) sind immer erlaubt und müssen hier NICHT aufgeführt werden.
    /// Standard: keine. Nur Flags angeben, KEINE Optionswerte.
    /// </summary>
    string[] AllowedFlags => Array.Empty<string>();

    /// <summary>Führt den Befehl aus und liefert den Prozess-Exitcode (0 = OK).</summary>
    int Execute(CommandContext ctx);
}
