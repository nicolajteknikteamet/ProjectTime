using System.ComponentModel.DataAnnotations;

namespace ProjectTime.Server.Data;

public sealed class Device
{
    public int Id { get; set; }
    [MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(128)] public string ApiKeyHash { get; set; } = "";
    [MaxLength(40)] public string? Platform { get; set; }
    [MaxLength(40)] public string? AgentVersion { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? LastSeenUtc { get; set; }
    public List<Activity> Activities { get; set; } = new();
}

public sealed class Activity
{
    public long Id { get; set; }
    public Guid ClientId { get; set; }
    public int DeviceId { get; set; }
    public Device? Device { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    [MaxLength(200)] public string AppName { get; set; } = "";
    [MaxLength(512)] public string WindowTitle { get; set; } = "";
    [MaxLength(1024)] public string? ProcessPath { get; set; }
    public bool IsIdle { get; set; }
    public bool IsOpen { get; set; }
    public int? ProjectId { get; set; }
    public Project? Project { get; set; }
    /// <summary>Sant om projektet valts manuellt och därför inte ska skrivas över av regler.</summary>
    public bool ProjectIsManual { get; set; }

    public TimeSpan Duration => EndUtc - StartUtc;
}

public sealed class Project
{
    public int Id { get; set; }
    [MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(100)] public string? Client { get; set; }
    [MaxLength(9)] public string Color { get; set; } = "#2a78d6";
    public bool IsArchived { get; set; }
    public decimal? HourlyRate { get; set; }
    public DateTime CreatedUtc { get; set; }
    public List<Rule> Rules { get; set; } = new();
}

public enum RuleField { App = 0, Title = 1, Path = 2, Any = 3 }
public enum RuleMatch { Contains = 0, StartsWith = 1, Equals = 2, Regex = 3 }

public sealed class Rule
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public RuleField Field { get; set; }
    public RuleMatch Match { get; set; }
    [MaxLength(500)] public string Pattern { get; set; } = "";
    /// <summary>Högre värde prövas först.</summary>
    public int Priority { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class TimeEntry
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
    public DateTime CreatedUtc { get; set; }

    public TimeSpan Duration => EndUtc - StartUtc;
}
