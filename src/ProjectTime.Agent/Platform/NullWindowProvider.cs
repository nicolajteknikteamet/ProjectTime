using ProjectTime.Agent.Tracking;

namespace ProjectTime.Agent.Platform;

/// <summary>Används på plattformar som inte stöds; registrerar ingenting.</summary>
public sealed class NullWindowProvider : IActiveWindowProvider
{
    public ActiveWindowInfo? GetActiveWindow() => null;
    public TimeSpan GetIdleTime() => TimeSpan.Zero;
}
