using WinCleaner.SystemTools;
using WinCleaner.Util;

namespace WinCleaner.Commands;

/// <summary>
/// Reiner Lese-Audit der Privacy-Lage: zeigt für jeden kuratierten Tweak
/// (Telemetrie, Tracking, KI: Copilot/Recall) den aktuellen Zustand
/// (angewendet / nicht angewendet / unbekannt). Ändert NIEMALS etwas – kein
/// Schreibzugriff, keine Adminrechte nötig. Zum Anwenden/Rückgängigmachen dient
/// der Befehl <c>privacy</c>. Unterstützt <c>--json</c> für maschinenlesbare Ausgabe.
/// </summary>
public sealed class ScanPrivacyCommand : ICommand
{
    public string Name => "scan-privacy";
    public string Summary => "Privacy-Audit (nur lesen): Zustand aller Telemetrie-/KI-Tweaks anzeigen";
    public string Usage => "[--json]";
    public string[] AllowedFlags => Array.Empty<string>();

    public int Execute(CommandContext ctx)
    {
        var engine = new TweakEngine(ctx.Logger);

        var items = PrivacyTweaks.All.Select(e =>
        {
            var status = engine.Status(e.Tweak);
            return new
            {
                id = e.Tweak.Id,
                description = e.Tweak.Description,
                profile = e.Profile.ToString(),
                hive = e.Tweak.Hive.ToString(),
                status = status.ToString(),
                applied = status == TweakStatus.Applied
            };
        }).ToList();

        if (ctx.Json)
        {
            int activ = items.Count(i => i.applied);
            JsonOut.Write(new
            {
                total = items.Count,
                applied = activ,
                tweaks = items
            });
            return 0;
        }

        var rows = items.Select(i => new[]
        {
            i.description,
            i.profile,
            i.hive,
            DescribeStatus(Enum.Parse<TweakStatus>(i.status))
        });
        ConsoleTable.From(rows, "Tweak", "Profil", "Hive", "Status").Write();

        int applied = items.Count(i => i.applied);
        Console.WriteLine($"\n{applied} von {items.Count} Privacy-Tweaks aktiv. " +
                          "Anwenden/rückgängig: Befehl \"privacy\".");
        return 0;
    }

    private static string DescribeStatus(TweakStatus s) => s switch
    {
        TweakStatus.Applied    => "aktiv",
        TweakStatus.NotApplied => "nicht aktiv",
        _                      => "unbekannt"
    };
}
