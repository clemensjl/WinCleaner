using System.Reflection;

namespace WinCleaner.Util;

/// <summary>Programm-Metadaten (Version) aus den Assembly-Attributen.</summary>
public static class AppInfo
{
    /// <summary>Informational-/Assembly-Version ohne Build-Metadaten (+hash).</summary>
    public static string Version
    {
        get
        {
            var asm = typeof(AppInfo).Assembly;
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(info)) return info.Split('+')[0];
            return asm.GetName().Version?.ToString() ?? "unbekannt";
        }
    }
}
