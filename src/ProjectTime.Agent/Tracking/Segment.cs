namespace ProjectTime.Agent.Tracking;

/// <summary>Ett sammanhängande aktivitetssegment under uppbyggnad eller avslutat.</summary>
public sealed class Segment
{
    public Guid ClientId { get; init; } = Guid.NewGuid();
    public DateTimeOffset StartUtc { get; init; }
    public DateTimeOffset EndUtc { get; set; }
    public string AppName { get; init; } = "";
    public string WindowTitle { get; init; } = "";
    public string? ProcessPath { get; init; }
    public bool IsIdle { get; init; }
    public bool IsOpen { get; set; } = true;

    public TimeSpan Duration => EndUtc - StartUtc;

    public bool SameActivityAs(ActiveWindowInfo w) =>
        !IsIdle &&
        string.Equals(AppName, w.AppName, StringComparison.Ordinal) &&
        string.Equals(WindowTitle, w.WindowTitle, StringComparison.Ordinal);
}
