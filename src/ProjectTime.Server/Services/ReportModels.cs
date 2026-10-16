using ProjectTime.Server.Data;

namespace ProjectTime.Server.Services;

public sealed record ProjectRef(int? Id, string Name, string Color)
{
    public static readonly ProjectRef Unassigned = new(null, "Ej tilldelad", Palette.Unassigned);
    public static ProjectRef From(Project? p) => p is null ? Unassigned : new ProjectRef(p.Id, p.Name, p.Color);
}

public sealed record ProjectTotal(ProjectRef Project, TimeSpan Duration, double Share);
public sealed record AppTotal(string AppName, TimeSpan Duration, double Share);
public sealed record DeviceTotal(int DeviceId, string DeviceName, TimeSpan Active, TimeSpan Idle);

/// <summary>Grupp av aktiviteter med samma program och fönstertitel.</summary>
public sealed record TitleTotal(string AppName, string WindowTitle, TimeSpan Duration, ProjectRef Project, bool Manual, int Count, DateTime FirstUtc, DateTime LastUtc);

public sealed record TimelineBlock(DateTime StartUtc, DateTime EndUtc, string Color, string Label, string DeviceName, bool IsIdle)
{
    public TimeSpan Duration => EndUtc - StartUtc;
}

public sealed class DaySummary
{
    public DateOnly Day { get; init; }
    public TimeSpan Active { get; init; }
    public TimeSpan Idle { get; init; }
    public TimeSpan Unassigned { get; init; }
    public DateTime? FirstUtc { get; init; }
    public DateTime? LastUtc { get; init; }
    public List<ProjectTotal> Projects { get; init; } = new();
    public List<AppTotal> Apps { get; init; } = new();
    public List<TitleTotal> Titles { get; init; } = new();
    public List<TimelineBlock> Blocks { get; init; } = new();
    public List<DeviceTotal> Devices { get; init; } = new();
    public List<Activity> Activities { get; init; } = new();
    public bool IsEmpty => Activities.Count == 0;
}

public sealed class RangeSummary
{
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public List<DateOnly> Days { get; init; } = new();
    public TimeSpan Active { get; init; }
    public TimeSpan Idle { get; init; }
    public TimeSpan Booked { get; init; }
    public List<ProjectTotal> Projects { get; init; } = new();
    /// <summary>Bokförd tid per projekt-id.</summary>
    public Dictionary<int, TimeSpan> BookedByProject { get; init; } = new();

    public TimeSpan BookedFor(int? projectId) => projectId is { } id ? BookedByProject.GetValueOrDefault(id) : TimeSpan.Zero;

    public Dictionary<(DateOnly Day, int? ProjectId), TimeSpan> ByDayAndProject { get; init; } = new();
    public Dictionary<DateOnly, TimeSpan> ByDay { get; init; } = new();
    public List<AppTotal> Apps { get; init; } = new();
    public List<DeviceTotal> Devices { get; init; } = new();
    public List<ProjectRef> ProjectOrder { get; init; } = new();
}
