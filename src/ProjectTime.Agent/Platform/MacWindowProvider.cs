using System.Globalization;
using System.Text.RegularExpressions;
using ProjectTime.Agent.Tracking;

namespace ProjectTime.Agent.Platform;

/// <summary>
/// macOS: använder osascript (System Events) för aktivt program/fönster och ioreg för inaktivitet.
/// Kräver att agenten får "Hjälpmedel"/"Automation"-behörighet i Systeminställningar.
/// </summary>
public sealed class MacWindowProvider : IActiveWindowProvider
{
    private const string Script = """
        tell application "System Events"
            set frontApp to first application process whose frontmost is true
            set appName to name of frontApp
            set appPath to ""
            try
                set appPath to POSIX path of (application file of frontApp)
            end try
            set winTitle to ""
            try
                set winTitle to name of front window of frontApp
            end try
            return appName & linefeed & winTitle & linefeed & appPath
        end tell
        """;

    private static readonly Regex IdleRegex = new("\"HIDIdleTime\"\\s*=\\s*(\\d+)", RegexOptions.Compiled);

    public ActiveWindowInfo? GetActiveWindow()
    {
        var output = ProcessRunner.Run("/usr/bin/osascript", new[] { "-e", Script });
        if (string.IsNullOrWhiteSpace(output)) return null;
        var lines = output.Split('\n');
        var app = lines.Length > 0 ? lines[0].Trim() : "";
        if (app.Length == 0) return null;
        if (app.Equals("loginwindow", StringComparison.OrdinalIgnoreCase) || app.Equals("ScreenSaverEngine", StringComparison.OrdinalIgnoreCase))
            return null;
        var title = lines.Length > 1 ? lines[1].Trim() : "";
        var path = lines.Length > 2 && lines[2].Trim().Length > 0 ? lines[2].Trim() : null;
        return new ActiveWindowInfo(app, title, path);
    }

    public TimeSpan GetIdleTime()
    {
        var output = ProcessRunner.Run("/usr/sbin/ioreg", new[] { "-c", "IOHIDSystem", "-d", "4" });
        if (output is null) return TimeSpan.Zero;
        var m = IdleRegex.Match(output);
        if (!m.Success) return TimeSpan.Zero;
        var ns = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        return TimeSpan.FromMilliseconds(ns / 1_000_000d);
    }
}
