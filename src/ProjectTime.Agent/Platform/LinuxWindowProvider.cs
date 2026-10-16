using System.Globalization;
using Microsoft.Extensions.Logging;
using ProjectTime.Agent.Tracking;

namespace ProjectTime.Agent.Platform;

/// <summary>
/// Linux (X11): använder xdotool för aktivt fönster och xprintidle för inaktivitet.
/// Under Wayland finns inget generellt API; då rapporteras inget fönster.
/// </summary>
public sealed class LinuxWindowProvider : IActiveWindowProvider
{
    private readonly ILogger<LinuxWindowProvider> _logger;
    private bool _warned;

    public LinuxWindowProvider(ILogger<LinuxWindowProvider> logger) => _logger = logger;

    public ActiveWindowInfo? GetActiveWindow()
    {
        var output = ProcessRunner.Run("xdotool", new[] { "getactivewindow", "getwindowname", "getwindowpid" });
        if (output is null)
        {
            if (!_warned)
            {
                _warned = true;
                _logger.LogWarning("Kunde inte läsa aktivt fönster. Kontrollera att xdotool och xprintidle är installerade och att sessionen kör X11 (DISPLAY={Display}).",
                    Environment.GetEnvironmentVariable("DISPLAY"));
            }
            return null;
        }

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var title = lines.Length > 0 ? lines[0].Trim() : "";
        var app = "unknown";
        string? path = null;
        if (lines.Length > 1 && int.TryParse(lines[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
        {
            try { app = File.ReadAllText($"/proc/{pid}/comm").Trim(); } catch { /* processen borta */ }
            try { path = new FileInfo($"/proc/{pid}/exe").LinkTarget; } catch { /* åtkomst nekad */ }
        }
        return new ActiveWindowInfo(app, title, path);
    }

    public TimeSpan GetIdleTime()
    {
        var output = ProcessRunner.Run("xprintidle", Array.Empty<string>());
        return output is not null && long.TryParse(output.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms)
            ? TimeSpan.FromMilliseconds(ms)
            : TimeSpan.Zero;
    }
}
