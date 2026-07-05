using WinCleaner.SystemTools;
using WinCleaner.Util;

namespace WinCleaner.Commands;

/// <summary>
/// Richtet eine geplante Aufgabe ein, die die Privacy-Tweaks regelmäßig neu
/// anwendet – Windows-Updates setzen einzelne Schalter gern zurück. Die Aufgabe
/// ruft WinCleaner mit <c>privacy --apply &lt;Profil&gt; --no-dry-run --yes</c>
/// auf. Geplante Aufgaben (Lauf um 05:00 Uhr) benötigen Adminrechte; fehlen
/// diese, wird mit Rechteerhöhung neu gestartet.
/// </summary>
public sealed class SchedulePrivacyCommand : ICommand
{
    public string Name => "schedule-privacy";
    public string Summary => "Privacy-Tweaks regelmäßig neu anwenden (nach Windows-Updates; Admin)";
    public string Usage => "daily|weekly [--profile standard|advanced] | unschedule";
    public string[] AllowedFlags => new[] { "--profile" };

    public int Execute(CommandContext ctx)
    {
        var arg = ctx.FirstPositional;
        if (arg is null)
        {
            Console.Error.WriteLine($"{Name} {Usage}");
            return 1;
        }

        // Profil VOR der Elevation validieren, damit Tippfehler sofort auffallen.
        var profile = PrivacyTweaks.ParseProfile(ctx.Option("--profile"));
        if (profile is null)
        {
            ctx.Logger.Error($"Unbekanntes Profil \"{ctx.Option("--profile")}\". Erlaubt: standard | advanced.");
            return 1;
        }

        // Geplante Aufgaben (system-weit, 05:00 Uhr) benötigen Adminrechte.
        if (!Elevation.IsAdministrator())
        {
            ctx.Logger.Info("Adminrechte nötig – starte mit Rechteerhöhung neu (UAC)...");
            return Elevation.RelaunchAsAdmin(ctx.FullArgs, ctx.Logger) ? 0 : 1;
        }

        var scheduler = new PrivacyScheduler(ctx.Logger);
        bool ok = string.Equals(arg, "unschedule", StringComparison.OrdinalIgnoreCase)
            ? scheduler.Unschedule()
            : scheduler.Schedule(arg, profile.Value);

        Prompt.PauseIfRelaunched(ctx.FullArgs);
        return ok ? 0 : 1;
    }
}
