using WinCleaner.SystemTools;
using WinCleaner.Util;

namespace WinCleaner.Commands;

public sealed class CreateRestorePointCommand : ICommand
{
    public string Name => "create-restore-point";
    public string Summary => "Wiederherstellungspunkt erstellen (Admin)";
    public string Usage => "[Name]";

    public int Execute(CommandContext ctx)
    {
        // Wiederherstellungspunkte brauchen Adminrechte -> ggf. eleviert neu starten.
        if (!Elevation.IsAdministrator())
        {
            ctx.Logger.Info("Adminrechte nötig – starte mit Rechteerhöhung neu (UAC)...");
            return Elevation.RelaunchAsAdmin(ctx.FullArgs, ctx.Logger) ? 0 : 1;
        }

        var nameParts = ctx.Positionals.ToList();
        string name = nameParts.Count > 0
            ? string.Join(' ', nameParts)
            : $"WinCleaner {DateTime.Now:yyyy-MM-dd HH:mm}";

        bool ok = new RestorePoint(ctx.Logger).Create(name);
        Console.WriteLine(ok
            ? "Wiederherstellungspunkt erstellt."
            : "Fehlgeschlagen (Systemschutz aktiv? Adminrechte?).");

        Prompt.PauseIfRelaunched(ctx.FullArgs);
        return ok ? 0 : 1;
    }
}
