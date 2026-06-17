using WinCleaner.SystemTools;
using WinCleaner.Util;

namespace WinCleaner.Commands;

public sealed class StartupDisableCommand : ICommand
{
    public string Name => "startup-disable";
    public string Summary => "Autostart-Eintrag deaktivieren";
    public string Usage => "<Name>";

    public int Execute(CommandContext ctx)
    {
        var name = string.Join(' ', ctx.Positionals);
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.WriteLine("Name fehlt: startup-disable <Name>");
            return 1;
        }

        var sm = new StartupManager(ctx.Logger);
        var result = sm.Disable(name);

        // HKLM/Common brauchen Admin -> ggf. eleviert neu starten.
        if (result == DisableResult.NeedsAdmin && !Elevation.IsAdministrator())
        {
            ctx.Logger.Info("Starte mit Rechteerhöhung neu (UAC)...");
            return Elevation.RelaunchAsAdmin(ctx.FullArgs, ctx.Logger) ? 0 : 1;
        }

        Prompt.PauseIfRelaunched(ctx.FullArgs);
        return result is DisableResult.Success or DisableResult.AlreadyDisabled ? 0 : 1;
    }
}
