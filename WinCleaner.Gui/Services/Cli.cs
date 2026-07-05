using System.Diagnostics;
using System.Text;

namespace WinCleaner.Gui.Services;

/// <summary>
/// Startet die WinCleaner-CLI OHNE sichtbares Konsolenfenster (für Aktionen, die
/// keine Adminrechte brauchen) und fängt deren Ausgabe ab. Für Admin-pflichtige
/// Aktionen dient <see cref="ElevatedCli"/> (UAC). So bleibt die GUI im
/// Normalfall fensterrein.
/// </summary>
public static class Cli
{
    public sealed record Result(int ExitCode, string Output)
    {
        public bool Success => ExitCode == 0;
    }

    public static async Task<Result> RunHiddenAsync(string arguments)
    {
        return await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = ElevatedCli.ResolveCliPath(),
                    Arguments              = arguments,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding  = Encoding.UTF8
                };

                using var proc = Process.Start(psi);
                if (proc is null) return new Result(-1, "Prozess konnte nicht gestartet werden.");

                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();

                var text = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
                return new Result(proc.ExitCode, text.Trim());
            }
            catch (Exception ex)
            {
                return new Result(-1, ex.Message);
            }
        });
    }
}
