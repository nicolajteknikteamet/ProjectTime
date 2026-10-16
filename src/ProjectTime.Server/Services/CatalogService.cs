using Microsoft.EntityFrameworkCore;
using ProjectTime.Server.Data;

namespace ProjectTime.Server.Services;

/// <summary>Projekt, regler, enheter och manuell tilldelning av aktiviteter.</summary>
public sealed class CatalogService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly Clock _clock;

    public CatalogService(IDbContextFactory<AppDbContext> factory, Clock clock)
    {
        _factory = factory;
        _clock = clock;
    }

    // ---- Projekt ----

    public async Task<List<Project>> GetProjectsAsync(bool includeArchived = false)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Projects.AsNoTracking().Include(p => p.Rules)
            .Where(p => includeArchived || !p.IsArchived)
            .OrderBy(p => p.IsArchived).ThenBy(p => p.Client).ThenBy(p => p.Name).ToListAsync();
    }

    public async Task<Project> SaveProjectAsync(Project input)
    {
        await using var db = await _factory.CreateDbContextAsync();
        Project p;
        if (input.Id == 0)
        {
            p = new Project { CreatedUtc = DateTime.UtcNow };
            db.Projects.Add(p);
            if (string.IsNullOrWhiteSpace(input.Color))
                input.Color = Palette.NextUnused(await db.Projects.Select(x => x.Color).ToListAsync());
        }
        else p = await db.Projects.FirstAsync(x => x.Id == input.Id);

        p.Name = input.Name.Trim();
        p.Client = string.IsNullOrWhiteSpace(input.Client) ? null : input.Client.Trim();
        p.Color = input.Color;
        p.IsArchived = input.IsArchived;
        p.HourlyRate = input.HourlyRate;
        await db.SaveChangesAsync();
        return p;
    }

    public async Task DeleteProjectAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        await db.Projects.Where(p => p.Id == id).ExecuteDeleteAsync();
    }

    public async Task<string> NextColorAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return Palette.NextUnused(await db.Projects.Select(x => x.Color).ToListAsync());
    }

    // ---- Regler ----

    public async Task<List<Rule>> GetRulesAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Rules.AsNoTracking().Include(r => r.Project)
            .OrderByDescending(r => r.Priority).ThenBy(r => r.Id).ToListAsync();
    }

    public async Task SaveRuleAsync(Rule input)
    {
        await using var db = await _factory.CreateDbContextAsync();
        Rule r;
        if (input.Id == 0) { r = new Rule(); db.Rules.Add(r); }
        else r = await db.Rules.FirstAsync(x => x.Id == input.Id);
        r.ProjectId = input.ProjectId;
        r.Field = input.Field;
        r.Match = input.Match;
        r.Pattern = input.Pattern.Trim();
        r.Priority = input.Priority;
        r.Enabled = input.Enabled;
        await db.SaveChangesAsync();
    }

    public async Task DeleteRuleAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        await db.Rules.Where(r => r.Id == id).ExecuteDeleteAsync();
    }

    /// <summary>Räknar om projekt för alla aktiviteter som inte tilldelats manuellt. Returnerar antal ändrade.</summary>
    public async Task<int> ReapplyRulesAsync(bool onlyUnassigned, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var matcher = new RuleMatcher(await db.Rules.AsNoTracking().Where(r => r.Enabled).ToListAsync(ct));
        var changed = 0;
        long lastId = 0;
        while (true)
        {
            var batch = await db.Activities
                .Where(a => a.Id > lastId && !a.IsIdle && !a.ProjectIsManual && (!onlyUnassigned || a.ProjectId == null))
                .OrderBy(a => a.Id).Take(2000).ToListAsync(ct);
            if (batch.Count == 0) break;
            foreach (var a in batch)
            {
                var pid = matcher.Resolve(a.AppName, a.WindowTitle, a.ProcessPath);
                if (pid != a.ProjectId) { a.ProjectId = pid; changed++; }
            }
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            lastId = batch[^1].Id;
        }
        return changed;
    }

    /// <summary>Tilldelar ett projekt manuellt till alla aktiviteter med givet program/titel under en dag.</summary>
    public async Task<int> AssignAsync(DateOnly day, int? deviceId, string appName, string? windowTitle, int? projectId)
    {
        var (lo, hi) = _clock.DayBounds(day);
        await using var db = await _factory.CreateDbContextAsync();
        var q = db.Activities.Where(a => a.EndUtc > lo && a.StartUtc < hi && !a.IsIdle && a.AppName == appName
                                      && (deviceId == null || a.DeviceId == deviceId));
        if (windowTitle is not null) q = q.Where(a => a.WindowTitle == windowTitle);
        return await q.ExecuteUpdateAsync(s => s.SetProperty(a => a.ProjectId, projectId).SetProperty(a => a.ProjectIsManual, projectId != null));
    }

    // ---- Enheter ----

    public async Task<List<Device>> GetDevicesAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Devices.AsNoTracking().OrderBy(d => d.Name).ToListAsync();
    }

    /// <summary>Skapar en enhet och returnerar API-nyckeln i klartext (visas bara en gång).</summary>
    public async Task<(Device Device, string ApiKey)> CreateDeviceAsync(string name)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var key = ApiKeys.Generate();
        var device = new Device { Name = name.Trim(), ApiKeyHash = ApiKeys.Hash(key), CreatedUtc = DateTime.UtcNow };
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        return (device, key);
    }

    public async Task<string> RotateKeyAsync(int deviceId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var d = await db.Devices.FirstAsync(x => x.Id == deviceId);
        var key = ApiKeys.Generate();
        d.ApiKeyHash = ApiKeys.Hash(key);
        await db.SaveChangesAsync();
        return key;
    }

    public async Task RenameDeviceAsync(int deviceId, string name)
    {
        await using var db = await _factory.CreateDbContextAsync();
        await db.Devices.Where(d => d.Id == deviceId).ExecuteUpdateAsync(s => s.SetProperty(d => d.Name, name.Trim()));
    }

    public async Task DeleteDeviceAsync(int deviceId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        await db.Activities.Where(a => a.DeviceId == deviceId).ExecuteDeleteAsync();
        await db.Devices.Where(d => d.Id == deviceId).ExecuteDeleteAsync();
    }

    public async Task<long> CountActivitiesAsync(int deviceId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Activities.LongCountAsync(a => a.DeviceId == deviceId);
    }

    /// <summary>Raderar all spårad data äldre än ett visst datum (dataminimering).</summary>
    public async Task<int> PurgeActivitiesBeforeAsync(DateOnly day)
    {
        var cutoff = _clock.DayBounds(day).StartUtc;
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Activities.Where(a => a.EndUtc < cutoff).ExecuteDeleteAsync();
    }
}
