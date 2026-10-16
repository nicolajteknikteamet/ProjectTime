using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using ProjectTime.Agent.Tracking;

namespace ProjectTime.Agent.Platform;

[SupportedOSPlatform("windows")]
public sealed class WindowsWindowProvider : IActiveWindowProvider
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLengthW(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    public ActiveWindowInfo? GetActiveWindow()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        var len = GetWindowTextLengthW(hwnd);
        var sb = new StringBuilder(len + 1);
        GetWindowTextW(hwnd, sb, sb.Capacity);
        var title = sb.ToString();

        GetWindowThreadProcessId(hwnd, out var pid);
        string app = "unknown";
        string? path = null;
        if (pid != 0)
        {
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                app = proc.ProcessName;
                try { path = proc.MainModule?.FileName; } catch { /* åtkomst nekad för förhöjda processer */ }
            }
            catch { /* processen hann avslutas */ }
        }

        // Låsskärmen rapporterar typiskt LockApp/LogonUI – räkna som "inget fönster".
        if (app.Equals("LockApp", StringComparison.OrdinalIgnoreCase) || app.Equals("LogonUI", StringComparison.OrdinalIgnoreCase))
            return null;

        return new ActiveWindowInfo(app, title, path);
    }

    public TimeSpan GetIdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        var elapsedMs = unchecked((uint)Environment.TickCount - info.dwTime);
        return TimeSpan.FromMilliseconds(elapsedMs);
    }
}
