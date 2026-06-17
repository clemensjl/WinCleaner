using WinCleaner.SystemTools;

namespace WinCleaner.Commands;

public sealed class UnscheduleCleanCommand : ICommand
{
    public string Name => "unschedule-clean";
    public string Summary => "Geplante Bereinigung entfernen";
    public string Usage => "";

    public int Execute(CommandContext ctx)
        => new TaskSchedulerHelper(ctx.Logger).RemoveScheduledClean() ? 0 : 1;
}
