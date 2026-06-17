using WinCleaner.SystemTools;

namespace WinCleaner.Util;

/// <summary>
/// Kleine Konsolen-Helfer für Bestätigungen und elevierte Fenster. Alle
/// Rückfragen gehen bewusst nach <see cref="Console.Error"/>, damit die
/// stdout-Ausgabe (Tabellen/JSON) maschinenlesbar bleibt.
/// </summary>
public static class Prompt
{
    /// <summary>
    /// Ja/Nein-Abfrage. Bei umgeleiteter Eingabe (kein interaktiver Kanal) wird
    /// false geliefert – Aufrufer sollen dann <c>--yes</c> verlangen.
    /// </summary>
    public static bool Confirm(string question)
    {
        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("Keine interaktive Konsole. Mit --yes bestätigen.");
            return false;
        }

        Console.Error.Write(question + " [j/N] ");
        var a = Console.ReadLine()?.Trim().ToLowerInvariant();
        return a is "j" or "ja" or "y" or "yes";
    }

    /// <summary>Hält ein eleviert neu gestartetes Fenster offen, bis eine Taste kommt.</summary>
    public static void PauseIfRelaunched(string[] fullArgs)
    {
        if (!fullArgs.Contains(Elevation.RelaunchFlag)) return;
        Console.WriteLine("\nTaste drücken zum Schließen...");
        try { Console.ReadKey(true); } catch { /* keine interaktive Konsole */ }
    }
}
