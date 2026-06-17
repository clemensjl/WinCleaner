using WinCleaner.SystemTools;

namespace WinCleaner.Commands;

public sealed class ScheduleCleanCommand : ICommand
{
    public string Name => "schedule-clean";
    public string Summary => "Automatische Bereinigung planen";
    public string Usage => "daily|weekly";

    public int Execute(CommandContext ctx)
    {
        var interval = ctx.FirstPositional;
        if (interval is null)
        {
            Console.WriteLine("Intervall fehlt: schedule-clean daily|weekly");
            return 1;
        }

        return new TaskSchedulerHelper(ctx.Logger).CreateScheduledClean(interval) ? 0 : 1;
    }
}
