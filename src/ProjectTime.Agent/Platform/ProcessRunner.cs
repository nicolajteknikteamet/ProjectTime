using System.Diagnostics;

namespace ProjectTime.Agent.Platform;

internal static class ProcessRunner
{
    /// <summary>Kör ett kommando och returnerar stdout, eller null vid fel/timeout.</summary>
    public static string? Run(string fileName, IEnumerable<string> args, int timeoutMs = 2000)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return null;
            var stdout = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); } catch { /* ignoreras */ }
                return null;
            }
            return p.ExitCode == 0 ? stdout.GetAwaiter().GetResult() : null;
        }
        catch
        {
            return null;
        }
    }
}
