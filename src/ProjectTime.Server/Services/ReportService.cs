using Microsoft.EntityFrameworkCore;
using ProjectTime.Server.Data;
using static ProjectTime.Server.Services.Intervals;

namespace ProjectTime.Server.Services;

/// <summary>Bygger dags- och periodsammanställningar av spårade aktiviteter.</summary>
public sealed class ReportService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly Clock _clock;

    public ReportService(IDbContextFactory<AppDbContext> factory, Clock clock)
    {
        _factory = factory;
        _clock = clock;
    }

    public async Task<DaySummary> GetDayAsync(DateOnly day, int? deviceId, CancellationToken ct = default)
    {
        var (lo, hi) = _clock.DayBounds(day);
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.Activities.AsNoTracking()
            .Include(a => a.Device).Include(a => a.Project)
            .Where(a => a.EndUtc > lo && a.StartUtc < hi && (deviceId == null || a.DeviceId == deviceId))
            .OrderBy(a => a.StartUtc)
            .ToListAsync(ct);

        // Klipp till dagens gränser.
        foreach (var a in rows)
        {
            if (a.StartUtc < lo) a.StartUtc = lo;
            if (a.EndUtc > hi) a.EndUtc = hi;
        }
        rows = rows.Where(a => a.EndUtc > a.StartUtc).ToList();

        var active = rows.Where(a => !a.IsIdle).ToList();
        var activeTotal = UnionDuration(active.Select(a => new Span(a.StartUtc, a.EndUtc)));
        var idleTotal = UnionDuration(rows.Where(a => a.IsIdle).Select(a => new Span(a.StartUtc, a.EndUtc)));

        var projects = active
            .GroupBy(a => a.ProjectId)
            .Select(g => new ProjectTotal(ProjectRef.From(g.First().Project), UnionDuration(g.Select(a => new Span(a.StartUtc, a.EndUtc))), 0))
            .OrderByDescending(p => p.Duration)
            .Select(p => p with { Share = activeTotal > TimeSpan.Zero ? p.Duration / activeTotal : 0 })
            .ToList();

        var appSum = active.Sum(a => a.Duration.Ticks);
        var apps = active
            .GroupBy(a => a.AppName)
            .Select(g => new AppTotal(g.Key, TimeSpan.FromTicks(g.Sum(a => a.Duration.Ticks)), 0))
            .OrderByDescending(a => a.Duration)
            .Select(a => a with { Share = appSum > 0 ? (double)a.Duration.Ticks / appSum : 0 })
            .ToList();

        var titles = active
            .GroupBy(a => (a.AppName, a.WindowTitle))
            .Select(g =>
            {
                var dominant = g.GroupBy(a => a.ProjectId).OrderByDescending(x => x.Sum(a => a.Duration.Ticks)).First().First();
                return new TitleTotal(g.Key.AppName, g.Key.WindowTitle, TimeSpan.FromTicks(g.Sum(a => a.Duration.Ticks)),
                    ProjectRef.From(dominant.Project), g.Any(a => a.ProjectIsManual), g.Count(), g.Min(a => a.StartUtc), g.Max(a => a.EndUtc));
            })
            .OrderBy(t => t.AppName).ThenByDescending(t => t.Duration)
            .ToList();

        var devices = rows.GroupBy(a => a.DeviceId)
            .Select(g => new DeviceTotal(g.Key, g.First().Device?.Name ?? $"Enhet {g.Key}",
                UnionDuration(g.Where(a => !a.IsIdle).Select(a => new Span(a.StartUtc, a.EndUtc))),
                UnionDuration(g.Where(a => a.IsIdle).Select(a => new Span(a.StartUtc, a.EndUtc)))))
            .OrderBy(d => d.DeviceName).ToList();

        return new DaySummary
        {
            Day = day,
            Active = activeTotal,
            Idle = idleTotal,
            Unassigned = projects.FirstOrDefault(p => p.Project.Id is null)?.Duration ?? TimeSpan.Zero,
            FirstUtc = active.Count > 0 ? active.Min(a => a.StartUtc) : null,
            LastUtc = active.Count > 0 ? active.Max(a => a.EndUtc) : null,
            Projects = projects,
            Apps = apps,
            Titles = titles,
            Blocks = BuildBlocks(rows),
            Devices = devices,
            Activities = rows,
        };
    }

    /// <summary>Slår ihop angränsande aktiviteter med samma färg per enhet till block för tidslinjen.</summary>
    public static List<TimelineBlock> BuildBlocks(IEnumerable<Activity> rows)
    {
        var blocks = new List<TimelineBlock>();
        foreach (var byDevice in rows.GroupBy(a => a.DeviceId))
        {
            TimelineBlock? cur = null;
            foreach (var a in byDevice.OrderBy(a => a.StartUtc))
            {
                var color = a.IsIdle ? Palette.Idle : a.Project?.Color ?? Palette.Unassigned;
                var label = a.IsIdle ? "Inaktiv" : a.Project?.Name ?? "Ej tilldelad";
                var device = a.Device?.Name ?? "";
                if (cur is not null && cur.Color == color && cur.IsIdle == a.IsIdle && a.StartUtc - cur.EndUtc <= TimeSpan.FromSeconds(5))
                {
                    cur = cur with { EndUtc = a.EndUtc > cur.EndUtc ? a.EndUtc : cur.EndUtc };
                }
                else
                {
                    if (cur is not null) blocks.Add(cur);
                    cur = new TimelineBlock(a.StartUtc, a.EndUtc, color, label, device, a.IsIdle);
                }
            }
            if (cur is not null) blocks.Add(cur);
        }
        return blocks.OrderBy(b => b.StartUtc).ToList();
    }

    public async Task<RangeSummary> GetRangeAsync(DateOnly from, DateOnly to, int? deviceId, CancellationToken ct = default)
    {
        if (to < from) (from, to) = (to, from);
        var lo = _clock.DayBounds(from).StartUtc;
        var hi = _clock.DayBounds(to).EndUtc;

        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.Activities.AsNoTracking()
            .Include(a => a.Device).Include(a => a.Project)
            .Where(a => a.EndUtc > lo && a.StartUtc < hi && (deviceId == null || a.DeviceId == deviceId))
            .Select(a => new { a.StartUtc, a.EndUtc, a.IsIdle, a.AppName, a.ProjectId, Project = a.Project, a.DeviceId, DeviceName = a.Device!.Name })
            .ToListAsync(ct);

        var entries = await db.TimeEntries.AsNoTracking().Include(t => t.Project)
            .Where(t => t.EndUtc > lo && t.StartUtc < hi).ToListAsync(ct);

        var days = new List<DateOnly>();
        for (var d = from; d <= to; d = d.AddDays(1)) days.Add(d);

        var byDayProject = new Dictionary<(DateOnly, int?), List<Span>>();
        var byDay = new Dictionary<DateOnly, List<Span>>();
        var byProject = new Dictionary<int, List<Span>>();   // 0 = ej tilldelad
        var projectRefs = new Dictionary<int, ProjectRef>();
        var allActive = new List<Span>();
        var allIdle = new List<Span>();
        var apps = new Dictionary<string, long>();
        var devActive = new Dictionary<(int, string), List<Span>>();
        var devIdle = new Dictionary<(int, string), List<Span>>();

        foreach (var a in rows)
        {
            var s = Clip(a.StartUtc, a.EndUtc, lo, hi);
            if (s is null) continue;
            var span = s.Value;
            var devKey = (a.DeviceId, a.DeviceName);
            if (a.IsIdle)
            {
                allIdle.Add(span);
                Add(devIdle, devKey, span);
                continue;
            }
            allActive.Add(span);
            Add(devActive, devKey, span);
            apps[a.AppName] = apps.GetValueOrDefault(a.AppName) + span.Duration.Ticks;
            projectRefs.TryAdd(a.ProjectId ?? 0, ProjectRef.From(a.Project));
            Add(byProject, a.ProjectId ?? 0, span);

            // Dela upp per lokal dag om aktiviteten korsar midnatt.
            foreach (var day in days)
            {
                var (dlo, dhi) = _clock.DayBounds(day);
                var part = Clip(span.Start, span.End, dlo, dhi);
                if (part is null) continue;
                Add(byDayProject, (day, a.ProjectId), part.Value);
                Add(byDay, day, part.Value);
            }
        }

        var activeTotal = UnionDuration(allActive);
        var projects = byProject
            .Select(kv => new ProjectTotal(projectRefs[kv.Key], UnionDuration(kv.Value), 0))
            .OrderByDescending(p => p.Duration)
            .Select(p => p with { Share = activeTotal > TimeSpan.Zero ? p.Duration / activeTotal : 0 })
            .ToList();

        var appSum = apps.Values.Sum();
        var bookedByProject = entries.GroupBy(e => e.ProjectId)
            .ToDictionary(g => g.Key, g => TimeSpan.FromTicks(g.Sum(e => (e.EndUtc - e.StartUtc).Ticks)));
        foreach (var e in entries) projectRefs.TryAdd(e.ProjectId, ProjectRef.From(e.Project));

        var order = projects.Select(p => p.Project).ToList();
        foreach (var id in bookedByProject.Keys) if (order.All(o => o.Id != id)) order.Add(projectRefs[id]);

        return new RangeSummary
        {
            From = from,
            To = to,
            Days = days,
            Active = activeTotal,
            Idle = UnionDuration(allIdle),
            Booked = TimeSpan.FromTicks(bookedByProject.Values.Sum(t => t.Ticks)),
            Projects = projects,
            BookedByProject = bookedByProject,
            ByDayAndProject = byDayProject.ToDictionary(kv => kv.Key, kv => UnionDuration(kv.Value)),
            ByDay = byDay.ToDictionary(kv => kv.Key, kv => UnionDuration(kv.Value)),
            Apps = apps.Select(kv => new AppTotal(kv.Key, TimeSpan.FromTicks(kv.Value), appSum > 0 ? (double)kv.Value / appSum : 0))
                       .OrderByDescending(a => a.Duration).ToList(),
            Devices = devActive.Keys.Union(devIdle.Keys)
                .Select(k => new DeviceTotal(k.Item1, k.Item2,
                    UnionDuration(devActive.GetValueOrDefault(k) ?? new()),
                    UnionDuration(devIdle.GetValueOrDefault(k) ?? new())))
                .OrderBy(d => d.DeviceName).ToList(),
            ProjectOrder = order,
        };
    }

    private static void Add<TKey>(Dictionary<TKey, List<Span>> dict, TKey key, Span span) where TKey : notnull
    {
        if (!dict.TryGetValue(key, out var list)) dict[key] = list = new List<Span>();
        list.Add(span);
    }
}
