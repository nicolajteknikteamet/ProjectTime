using Microsoft.AspNetCore.Mvc;
using ProjectTime.Server.Services;
using ProjectTime.Shared;

namespace ProjectTime.Server.Api;

public static class AgentEndpoints
{
    public static IEndpointRouteBuilder MapAgentApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/agent").AllowAnonymous().DisableAntiforgery();

        group.MapPost("/activities", async (
            [FromHeader(Name = AgentApi.ApiKeyHeader)] string? apiKey,
            ActivityBatchRequest request,
            ActivityIngestService ingest,
            CancellationToken ct) =>
        {
            var device = await ingest.AuthenticateAsync(apiKey, ct);
            if (device is null) return Results.Unauthorized();
            if (request.Segments.Count > AgentApi.MaxBatchSize)
                return Results.BadRequest($"Högst {AgentApi.MaxBatchSize} segment per anrop.");

            var accepted = await ingest.UpsertAsync(device.Id, request.Segments, ct);
            await ingest.TouchAsync(device.Id, request.Platform, request.AgentVersion, ct);
            return Results.Ok(new ActivityBatchResponse(accepted, DateTimeOffset.UtcNow));
        });

        group.MapPost("/heartbeat", async (
            [FromHeader(Name = AgentApi.ApiKeyHeader)] string? apiKey,
            HeartbeatRequest request,
            ActivityIngestService ingest,
            CancellationToken ct) =>
        {
            var device = await ingest.AuthenticateAsync(apiKey, ct);
            if (device is null) return Results.Unauthorized();
            await ingest.TouchAsync(device.Id, request.Platform, request.AgentVersion, ct);
            return Results.Ok(new { serverTimeUtc = DateTimeOffset.UtcNow });
        });

        return app;
    }
}
