using Microsoft.EntityFrameworkCore;
using ProjectTime.Server.Data;
using ProjectTime.Shared;

namespace ProjectTime.Server.Services;

/// <summary>Tar emot segment från agenter och lagrar dem idempotent.</summary>
public sealed class ActivityIngestService
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public ActivityIngestService(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<Device?> AuthenticateAsync(string? apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return null;
        var hash = ApiKeys.Hash(apiKey);
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.ApiKeyHash == hash, ct);
    }

    public async Task TouchAsync(int deviceId, string? platform, string? version, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var device = await db.Devices.FindAsync(new object[] { deviceId }, ct);
        if (device is null) return;
        device.LastSeenUtc = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(platform)) device.Platform = platform;
        if (!string.IsNullOrWhiteSpace(version)) device.AgentVersion = version;
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> UpsertAsync(int deviceId, IReadOnlyList<ActivitySegmentDto> segments, CancellationToken ct)
    {
        if (segments.Count == 0) return 0;
        await using var db = await _factory.CreateDbContextAsync(ct);

        var matcher = new RuleMatcher(await db.Rules.AsNoTracking().Where(r => r.Enabled).ToListAsync(ct));
        var ids = segments.Select(s => s.ClientId).Distinct().ToList();
        var existing = await db.Activities.Where(a => a.DeviceId == deviceId && ids.Contains(a.ClientId))
                                          .ToDictionaryAsync(a => a.ClientId, ct);
        var accepted = 0;

        foreach (var s in segments)
        {
            var start = s.StartUtc.UtcDateTime;
            var end = s.EndUtc.UtcDateTime;
            existing.TryGetValue(s.ClientId, out var row);

            if (end <= start)
            {
                if (row is not null) db.Activities.Remove(row);
                continue;
            }

            if (row is null)
            {
                var app = Trunc(s.AppName, 200);
                var title = Trunc(s.WindowTitle, 512);
                var path = s.ProcessPath is null ? null : Trunc(s.ProcessPath, 1024);
                row = new Activity
                {
                    ClientId = s.ClientId,
                    DeviceId = deviceId,
                    StartUtc = start,
                    EndUtc = end,
                    AppName = app,
                    WindowTitle = title,
                    ProcessPath = path,
                    IsIdle = s.IsIdle,
                    IsOpen = s.IsOpen,
                    ProjectId = s.IsIdle ? null : matcher.Resolve(app, title, path),
                };
                db.Activities.Add(row);
                existing[s.ClientId] = row;
            }
            else
            {
                row.StartUtc = start;
                row.EndUtc = end;
                row.IsOpen = s.IsOpen;
            }
            accepted++;
        }

        await db.SaveChangesAsync(ct);
        return accepted;
    }

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];
}
