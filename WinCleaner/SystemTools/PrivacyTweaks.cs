using Microsoft.Win32;

namespace WinCleaner.SystemTools;

/// <summary>
/// Profil-Stufe für Privacy-Tweaks. <see cref="Standard"/> umfasst breit
/// empfohlene, risikoarme Schalter; <see cref="Advanced"/> schließt zusätzlich
/// aggressivere Tweaks ein (z. B. Cortana komplett aus, Standortverlauf,
/// Diagnose-Feedback), die einzelne Komfortfunktionen einschränken können.
/// </summary>
public enum PrivacyProfile
{
    /// <summary>Sichere, breit empfohlene Tweaks.</summary>
    Standard,

    /// <summary>Aggressivere Tweaks zusätzlich zum Standard-Profil.</summary>
    Advanced
}

/// <summary>
/// Ein Privacy-Tweak: der reversible Registry-Eintrag plus die Information, ab
/// welchem Profil er angewendet wird. Standard-Tweaks sind in beiden Profilen
/// enthalten, Advanced-Tweaks nur im erweiterten Profil.
/// </summary>
/// <param name="Tweak">Der zugrunde liegende reversible Registry-Tweak.</param>
/// <param name="Profile">Niedrigstes Profil, in dem dieser Tweak aktiv wird.</param>
public sealed record PrivacyTweakEntry(RegistryTweak Tweak, PrivacyProfile Profile);

/// <summary>
/// Kuratierter, faktisch belegter Katalog von Telemetrie-/Tracking-Tweaks für
/// Windows 10/11 – inklusive KI-Funktionen (Windows Copilot, Recall). Jeder
/// Tweak ist über <see cref="TweakEngine"/> umkehrbar (JSON-Backup des
/// Vorzustands). Wo möglich wird <c>HKCU</c> verwendet (kein Admin nötig),
/// systemweite Richtlinien nutzen <c>HKLM</c> (Adminrechte erforderlich).
/// Alle verwendeten Schlüssel sind dokumentierte Policy-/Settings-Keys.
/// </summary>
public static class PrivacyTweaks
{
    /// <summary>
    /// Vollständiger Katalog aller Privacy-Tweaks mit Profil-Zuordnung.
    /// Reihenfolge = Anzeige-/Anwendungsreihenfolge.
    /// </summary>
    public static readonly IReadOnlyList<PrivacyTweakEntry> All = new List<PrivacyTweakEntry>
    {
        // --- STANDARD: breit empfohlen, risikoarm ---------------------------

        // Telemetrie auf Minimum (HKLM-Policy). 0 = Security (Enterprise) bzw.
        // niedrigstmöglich für die Edition; greift systemweit.
        new(new RegistryTweak(
            "privacy.telemetry-allow",
            "Telemetrie auf Minimum (AllowTelemetry = 0)",
            RegistryHive.LocalMachine,
            @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
            "AllowTelemetry",
            RegistryValueKind.DWord,
            0),
            PrivacyProfile.Standard),

        // Werbe-ID des Nutzers deaktivieren (HKCU).
        new(new RegistryTweak(
            "privacy.advertising-id",
            "Werbe-ID deaktivieren (personalisierte Werbung aus)",
            RegistryHive.CurrentUser,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo",
            "Enabled",
            RegistryValueKind.DWord,
            0),
            PrivacyProfile.Standard),

        // Aktivitätsverlauf / "Timeline" nicht an Microsoft senden (HKLM-Policy).
        new(new RegistryTweak(
            "privacy.activity-feed",
            "Aktivitätsverlauf (Timeline) deaktivieren",
            RegistryHive.LocalMachine,
            @"SOFTWARE\Policies\Microsoft\Windows\System",
            "EnableActivityFeed",
            RegistryValueKind.DWord,
            0),
            PrivacyProfile.Standard),

        // Tipps, Tricks und Vorschläge zu Windows (HKCU).
        new(new RegistryTweak(
            "privacy.tips-suggestions",
            "Windows-Tipps und -Vorschläge deaktivieren",
            RegistryHive.CurrentUser,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
            "SubscribedContent-338389Enabled",
            RegistryValueKind.DWord,
            0),
            PrivacyProfile.Standard),

        // Automatische Installation vorgeschlagener Apps (Consumer Features, HKCU).
        new(new RegistryTweak(
            "privacy.consumer-features",
            "Automatische Installation vorgeschlagener Apps deaktivieren",
            RegistryHive.CurrentUser,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
            "SilentInstalledAppsEnabled",
            RegistryValueKind.DWord,
            0),
            PrivacyProfile.Standard),

        // --- KI (Standard): Copilot und Recall standardmäßig empfohlen aus ---

        // Windows Copilot abschalten (HKCU-Policy). 1 = Copilot deaktiviert.
        new(new RegistryTweak(
            "privacy.windows-copilot",
            "Windows Copilot deaktivieren (KI-Assistent aus)",
            RegistryHive.CurrentUser,
            @"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot",
            "TurnOffWindowsCopilot",
            RegistryValueKind.DWord,
            1),
            PrivacyProfile.Standard),

        // Windows Recall / KI-Datenanalyse deaktivieren (HKCU-Policy).
        // 1 = DisableAIDataAnalysis (Recall sammelt keine Snapshots).
        new(new RegistryTweak(
            "privacy.recall-disable",
            "Windows Recall (KI-Snapshots) deaktivieren",
            RegistryHive.CurrentUser,
            @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI",
            "DisableAIDataAnalysis",
            RegistryValueKind.DWord,
            1),
            PrivacyProfile.Standard),

        // --- ADVANCED: aggressiver, kann Komfort einschränken ----------------

        // Cortana systemweit deaktivieren (HKLM-Policy).
        new(new RegistryTweak(
            "privacy.cortana",
            "Cortana deaktivieren (Sprachassistent aus)",
            RegistryHive.LocalMachine,
            @"SOFTWARE\Policies\Microsoft\Windows\Windows Search",
            "AllowCortana",
            RegistryValueKind.DWord,
            0),
            PrivacyProfile.Advanced),

        // Standortverlauf/-zugriff per Richtlinie sperren (HKLM-Policy).
        // "Deny" verhindert App-Zugriff auf den Standort systemweit.
        new(new RegistryTweak(
            "privacy.location-history",
            "Standortzugriff/-verlauf systemweit sperren",
            RegistryHive.LocalMachine,
            @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy",
            "LetAppsAccessLocation",
            RegistryValueKind.DWord,
            2),
            PrivacyProfile.Advanced),

        // Feedback-Häufigkeit auf "nie" (HKCU). 0 = keine Feedback-Anfragen.
        new(new RegistryTweak(
            "privacy.feedback-frequency",
            "Diagnose-Feedback-Anfragen deaktivieren (nie nachfragen)",
            RegistryHive.CurrentUser,
            @"SOFTWARE\Microsoft\Siuf\Rules",
            "NumberOfSIUFInPeriod",
            RegistryValueKind.DWord,
            0),
            PrivacyProfile.Advanced),

        // Tailored Experiences (Diagnosedaten für personalisierte Tipps, HKCU).
        new(new RegistryTweak(
            "privacy.tailored-experiences",
            "Personalisierte Erlebnisse aus Diagnosedaten deaktivieren",
            RegistryHive.CurrentUser,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Privacy",
            "TailoredExperiencesWithDiagnosticDataEnabled",
            RegistryValueKind.DWord,
            0),
            PrivacyProfile.Advanced),
    };

    /// <summary>
    /// Liefert die für ein Profil anzuwendenden Tweaks: <see cref="PrivacyProfile.Standard"/>
    /// nur die Standard-Tweaks, <see cref="PrivacyProfile.Advanced"/> Standard + Advanced.
    /// </summary>
    public static IEnumerable<RegistryTweak> ForProfile(PrivacyProfile profile) =>
        All.Where(e => profile == PrivacyProfile.Advanced || e.Profile == PrivacyProfile.Standard)
           .Select(e => e.Tweak);

    /// <summary>Alle Tweaks (profilunabhängig) – für Status/Undo/Audit.</summary>
    public static IEnumerable<RegistryTweak> AllTweaks => All.Select(e => e.Tweak);

    /// <summary>
    /// Parst einen Profilnamen ("standard"/"advanced", deutsch tolerant).
    /// Liefert <c>null</c> bei unbekanntem Wert. Leer/null = Standard.
    /// </summary>
    public static PrivacyProfile? ParseProfile(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return PrivacyProfile.Standard;
        return value.Trim().ToLowerInvariant() switch
        {
            "standard" or "sicher" or "empfohlen" => PrivacyProfile.Standard,
            "advanced" or "erweitert" or "aggressiv" => PrivacyProfile.Advanced,
            _ => null
        };
    }

    /// <summary>True, wenn der Tweak die HKLM-Hive nutzt und damit Adminrechte braucht.</summary>
    public static bool NeedsAdmin(RegistryTweak t) => t.Hive == RegistryHive.LocalMachine;
}
