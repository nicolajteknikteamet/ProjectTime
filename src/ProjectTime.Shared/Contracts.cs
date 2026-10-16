namespace ProjectTime.Shared;

/// <summary>
/// En sammanhängande period då ett visst fönster/program var aktivt (eller då datorn var inaktiv).
/// Identifieras av ett klientgenererat id så att agenten kan skicka om samma segment utan dubbletter.
/// </summary>
public sealed record ActivitySegmentDto(
    Guid ClientId,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string AppName,
    string WindowTitle,
    string? ProcessPath,
    bool IsIdle,
    bool IsOpen);

public sealed record ActivityBatchRequest(
    string DeviceName,
    string Platform,
    string AgentVersion,
    IReadOnlyList<ActivitySegmentDto> Segments);

public sealed record ActivityBatchResponse(int Accepted, DateTimeOffset ServerTimeUtc);

public sealed record HeartbeatRequest(string DeviceName, string Platform, string AgentVersion);

public static class AgentApi
{
    public const string ApiKeyHeader = "X-Api-Key";
    public const string ActivitiesPath = "/api/agent/activities";
    public const string HeartbeatPath = "/api/agent/heartbeat";
    public const int MaxBatchSize = 500;
}
