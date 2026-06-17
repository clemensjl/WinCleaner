using WinCleaner.SystemTools;
using WinCleaner.Util;

namespace WinCleaner.Commands;

/// <summary>
/// Richtet eine geplante Aufgabe für automatische Updates ein oder entfernt sie.
/// Die Aufgabe ruft WinCleaner mit <c>update --no-dry-run --yes</c> auf.
/// Geplante Aufgaben für alle Nutzer (Lauf um 04:00 Uhr) benötigen Adminrechte;
/// fehlen diese, wird mit Rechteerhöhung neu gestartet.
/// </summary>
public sealed class ScheduleUpdateCommand : ICommand
{
    public string Name => "schedule-update";
    public string Summary => "Automatische Updates planen/entfernen (Admin)";
    public string Usage => "daily|weekly | unschedule";

    public int Execute(CommandContext ctx)
    {
        var arg = ctx.FirstPositional;
        if (arg is null)
        {
            Console.Error.WriteLine($"{Name} {Usage}");
            return 1;
        }

        // Geplante Aufgaben (system-weit, 04:00 Uhr) benötigen Adminrechte.
        if (!Elevation.IsAdministrator())
        {
            ctx.Logger.Info("Adminrechte nötig – starte mit Rechteerhöhung neu (UAC)...");
            return Elevation.RelaunchAsAdmin(ctx.FullArgs, ctx.Logger) ? 0 : 1;
        }

        var winget = new WingetWrapper(ctx.Logger);
        bool ok;

        if (string.Equals(arg, "unschedule", StringComparison.OrdinalIgnoreCase))
        {
            ok = winget.UnscheduleUpdate();
        }
        else
        {
            // winget muss vorhanden sein, damit die geplante Aufgabe später funktioniert.
            if (!winget.IsAvailable())
            {
                winget.ReportUnavailable();
                Prompt.PauseIfRelaunched(ctx.FullArgs);
                return 1;
            }
            ok = winget.ScheduleUpdate(arg);
        }

        Prompt.PauseIfRelaunched(ctx.FullArgs);
        return ok ? 0 : 1;
    }
}
