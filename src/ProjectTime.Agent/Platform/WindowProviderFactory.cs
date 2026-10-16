using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProjectTime.Agent.Tracking;

namespace ProjectTime.Agent.Platform;

public static class WindowProviderFactory
{
    public static IActiveWindowProvider Create(IServiceProvider sp)
    {
        if (OperatingSystem.IsWindows()) return new WindowsWindowProvider();
        if (OperatingSystem.IsMacOS()) return new MacWindowProvider();
        if (OperatingSystem.IsLinux()) return new LinuxWindowProvider(sp.GetRequiredService<ILogger<LinuxWindowProvider>>());
        sp.GetRequiredService<ILoggerFactory>().CreateLogger("ProjectTime.Agent")
          .LogWarning("Plattformen {Os} stöds inte för fönsterspårning.", RuntimeInformation.OSDescription);
        return new NullWindowProvider();
    }

    public static string PlatformName()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsMacOS()) return "macos";
        if (OperatingSystem.IsLinux()) return "linux";
        return RuntimeInformation.OSDescription;
    }
}
