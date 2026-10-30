using System.Diagnostics;

namespace RoG.Linux.App.Services;

/// <summary>
/// Startzeit messen (Stufe 10, Leistung): mit <c>ROG_STARTUP_TRACE=1</c> schreibt die App die Zeit seit Prozessstart
/// je Abschnitt auf stderr (<c>[startup] window-opened 412 ms</c>). Nur Zeiten, keine Inhalte – ohne die Variable passiert nichts.
/// </summary>
internal static class StartupTrace
{
    private static readonly bool s_enabled = Environment.GetEnvironmentVariable("ROG_STARTUP_TRACE") == "1";

    public static void Mark(string step)
    {
        if (!s_enabled)
        {
            return;
        }

        var elapsed = DateTime.Now - Process.GetCurrentProcess().StartTime;
        Console.Error.WriteLine($"[startup] {step} {elapsed.TotalMilliseconds:F0} ms");
    }
}
