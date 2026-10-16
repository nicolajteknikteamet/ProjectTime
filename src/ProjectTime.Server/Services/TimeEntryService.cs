using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectTime.Server.Data;
using static ProjectTime.Server.Services.Intervals;

namespace ProjectTime.Server.Services;

public sealed record BookingSuggestion(int ProjectId, string ProjectName, string Color, DateTime StartUtc, DateTime EndUtc)
{
    public TimeSpan Duration => EndUtc - StartUtc;
}

/// <summary>Tidsposter: bokförd tid per projekt, manuellt eller härledd från aktiviteter.</summary>
public sealed class TimeEntryService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly Clock _clock;
    private readonly ServerOptions _options;

    public TimeEntryService(IDbContextFactory<AppDbContext> factory, Clock clock, IOptions<ServerOptions> options)
    {
        _factory = factory;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<List<TimeEntry>> GetRangeAsync(DateOnly from, DateOnly to)
    {
        var lo = _clock.DayBounds(from).StartUtc;
        var hi = _clock.DayBounds(to).EndUtc;
        await using var db = await _factory.CreateDbContextAsync();
        return await db.TimeEntries.AsNoTracking().Include(t => t.Project)
            .Where(t => t.EndUtc > lo && t.StartUtc < hi).OrderBy(t => t.StartUtc).ToListAsync();
    }

    public async Task<TimeEntry> SaveAsync(TimeEntry input)
    {
        if (input.EndUtc <= input.StartUtc) throw new ArgumentException("Sluttiden måste vara efter starttiden.");
        await using var db = await _factory.CreateDbContextAsync();
        TimeEntry e;
        if (input.Id == 0) { e = new TimeEntry { CreatedUtc = DateTime.UtcNow }; db.TimeEntries.Add(e); }
        else e = await db.TimeEntries.FirstAsync(x => x.Id == input.Id);
        e.ProjectId = input.ProjectId;
        e.StartUtc = input.StartUtc;
        e.EndUtc = input.EndUtc;
        e.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        await db.SaveChangesAsync();
        return e;
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        await db.TimeEntries.Where(t => t.Id == id).ExecuteDeleteAsync();
    }

    /// <summary>
    /// Föreslår tidsposter för en dag genom att slå ihop aktiviteter per projekt.
    /// Tid som redan är bokförd på projektet räknas bort.
    /// </summary>
    public async Task<List<BookingSuggestion>> SuggestAsync(DateOnly day, int? deviceId, int? mergeGapMinutes = null)
    {
        var (lo, hi) = _clock.DayBounds(day);
        var gap = TimeSpan.FromMinutes(mergeGapMinutes ?? _options.BookingMergeGapMinutes);
        await using var db = await _factory.CreateDbContextAsync();
        var rows = await db.Activities.AsNoTracking().Include(a => a.Project)
            .Where(a => a.EndUtc > lo && a.StartUtc < hi && !a.IsIdle && a.ProjectId != null && (deviceId == null || a.DeviceId == deviceId))
            .Select(a => new { a.StartUtc, a.EndUtc, Project = a.Project! })
            .ToListAsync();
        var existing = await db.TimeEntries.AsNoTracking().Where(t => t.EndUtc > lo && t.StartUtc < hi).ToListAsync();

        var result = new List<BookingSuggestion>();
        foreach (var g in rows.GroupBy(r => r.Project.Id))
        {
            var project = g.First().Project;
            var spans = g.Select(r => Clip(r.StartUtc, r.EndUtc, lo, hi)).Where(s => s != null).Select(s => s!.Value);
            var merged = Merge(spans, gap);
            var booked = existing.Where(e => e.ProjectId == project.Id).Select(e => new Span(e.StartUtc, e.EndUtc)).ToList();
            foreach (var m in merged)
            {
                foreach (var free in Subtract(m, booked))
                    if (free.Duration >= TimeSpan.FromMinutes(1))
                        result.Add(new BookingSuggestion(project.Id, project.Name, project.Color, RoundDown(free.Start), RoundUp(free.End)));
            }
        }
        return result.OrderBy(r => r.StartUtc).ToList();
    }

    public async Task<int> BookAsync(IEnumerable<BookingSuggestion> suggestions)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var n = 0;
        foreach (var s in suggestions)
        {
            db.TimeEntries.Add(new TimeEntry { ProjectId = s.ProjectId, StartUtc = s.StartUtc, EndUtc = s.EndUtc, CreatedUtc = DateTime.UtcNow });
            n++;
        }
        await db.SaveChangesAsync();
        return n;
    }

    private static IEnumerable<Span> Subtract(Span span, List<Span> holes)
    {
        var cursor = span.Start;
        foreach (var h in holes.Where(h => h.End > span.Start && h.Start < span.End).OrderBy(h => h.Start))
        {
            if (h.Start > cursor) yield return new Span(cursor, h.Start);
            if (h.End > cursor) cursor = h.End;
        }
        if (cursor < span.End) yield return new Span(cursor, span.End);
    }

    private static DateTime RoundDown(DateTime t) => new(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, DateTimeKind.Utc);
    private static DateTime RoundUp(DateTime t) => t.Second == 0 && t.Millisecond == 0 ? t : RoundDown(t).AddMinutes(1);
}
