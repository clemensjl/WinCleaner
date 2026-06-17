using WinCleaner.SystemTools;
using WinCleaner.Util;

namespace WinCleaner.Commands;

public sealed class StartupListCommand : ICommand
{
    public string Name => "startup-list";
    public string Summary => "Autostart-Einträge auflisten";
    public string Usage => "";

    public int Execute(CommandContext ctx)
    {
        var items = new StartupManager(ctx.Logger).List();

        if (ctx.Json)
        {
            JsonOut.Write(items);
            return 0;
        }

        ConsoleTable.From(
            items.Select(i => new[] { i.Source, i.Name, i.Path, i.Enabled ? "Ja" : "Nein" }),
            "Quelle", "Name", "Pfad", "Aktiv"
        ).Write();
        return 0;
    }
}
