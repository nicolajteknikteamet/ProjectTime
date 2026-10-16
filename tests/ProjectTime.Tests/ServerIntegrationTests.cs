using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectTime.Server;
using ProjectTime.Server.Data;
using ProjectTime.Server.Services;
using ProjectTime.Shared;

namespace ProjectTime.Tests;

/// <summary>Kör ingest, regler, dagssammanställning och bokning mot en SQLite-databas i minnet.</summary>
public sealed class ServerIntegrationTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly Clock _clock = new(TimeZoneInfo.Utc);
    private readonly ActivityIngestService _ingest;
    private readonly ReportService _reports;
    private readonly CatalogService _catalog;
    private readonly TimeEntryService _entries;

    private sealed class Factory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _o;
        public Factory(DbContextOptions<AppDbContext> o) => _o = o;
        public AppDbContext CreateDbContext() => new(_o);
    }

    public ServerIntegrationTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        using (var db = new AppDbContext(options)) db.Database.Migrate();
        _factory = new Factory(options);
        _ingest = new ActivityIngestService(_factory);
        _reports = new ReportService(_factory, _clock);
        _catalog = new CatalogService(_factory, _clock);
        _entries = new TimeEntryService(_factory, _clock, Options.Create(new ServerOptions { BookingMergeGapMinutes = 5 }));
    }

    public void Dispose() => _conn.Dispose();

    private static readonly DateTimeOffset T0 = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
    private static ActivitySegmentDto Seg(Guid id, int fromMin, int toMin, string app, string title, bool idle = false, bool open = false) =>
        new(id, T0.AddMinutes(fromMin), T0.AddMinutes(toMin), app, title, null, idle, open);

    [Fact]
    public async Task Api_key_authenticates_device_and_rejects_wrong_key()
    {
        var (device, key) = await _catalog.CreateDeviceAsync("Laptop");
        Assert.StartsWith("pt_", key);
        var ok = await _ingest.AuthenticateAsync(key, default);
        Assert.Equal(device.Id, ok!.Id);
        Assert.Null(await _ingest.AuthenticateAsync("pt_fel", default));
        Assert.Null(await _ingest.AuthenticateAsync(null, default));
    }

    [Fact]
    public async Task Upsert_is_idempotent_and_extends_open_segments()
    {
        var (device, _) = await _catalog.CreateDeviceAsync("Laptop");
        var id = Guid.NewGuid();
        await _ingest.UpsertAsync(device.Id, new[] { Seg(id, 0, 5, "code", "main.cs", open: true) }, default);
        await _ingest.UpsertAsync(device.Id, new[] { Seg(id, 0, 12, "code", "main.cs", open: false) }, default);

        var day = await _reports.GetDayAsync(new DateOnly(2026, 10, 16), null);
        var a = Assert.Single(day.Activities);
        Assert.Equal(TimeSpan.FromMinutes(12), a.Duration);
        Assert.False(a.IsOpen);
    }

    [Fact]
    public async Task Rules_assign_projects_on_ingest_and_reapply()
    {
        var (device, _) = await _catalog.CreateDeviceAsync("Laptop");
        var acme = await _catalog.SaveProjectAsync(new Project { Name = "Acme" });
        await _catalog.SaveRuleAsync(new Rule { ProjectId = acme.Id, Field = RuleField.Title, Match = RuleMatch.Contains, Pattern = "acme" });

        await _ingest.UpsertAsync(device.Id, new[]
        {
            Seg(Guid.NewGuid(), 0, 30, "code", "acme/api – main.cs"),
            Seg(Guid.NewGuid(), 30, 45, "browser", "Nyheter"),
            Seg(Guid.NewGuid(), 45, 60, "", "", idle: true),
        }, default);

        var day = await _reports.GetDayAsync(new DateOnly(2026, 10, 16), null);
        Assert.Equal(TimeSpan.FromMinutes(45), day.Active);
        Assert.Equal(TimeSpan.FromMinutes(15), day.Idle);
        Assert.Equal(TimeSpan.FromMinutes(15), day.Unassigned);
        Assert.Equal(TimeSpan.FromMinutes(30), day.Projects.Single(p => p.Project.Id == acme.Id).Duration);

        // Ny regel i efterhand fångar det otilldelade; manuell tilldelning skrivs inte över.
        var news = await _catalog.SaveProjectAsync(new Project { Name = "Omvärld" });
        await _catalog.SaveRuleAsync(new Rule { ProjectId = news.Id, Field = RuleField.App, Match = RuleMatch.Equals, Pattern = "browser" });
        Assert.Equal(1, await _catalog.ReapplyRulesAsync(onlyUnassigned: true));

        await _catalog.AssignAsync(new DateOnly(2026, 10, 16), null, "browser", "Nyheter", acme.Id);
        Assert.Equal(0, await _catalog.ReapplyRulesAsync(onlyUnassigned: false));
        day = await _reports.GetDayAsync(new DateOnly(2026, 10, 16), null);
        Assert.Equal(TimeSpan.FromMinutes(45), day.Projects.Single(p => p.Project.Id == acme.Id).Duration);
    }

    [Fact]
    public async Task Two_devices_at_once_are_not_double_counted()
    {
        var (a, _) = await _catalog.CreateDeviceAsync("A");
        var (b, _) = await _catalog.CreateDeviceAsync("B");
        await _ingest.UpsertAsync(a.Id, new[] { Seg(Guid.NewGuid(), 0, 60, "code", "x") }, default);
        await _ingest.UpsertAsync(b.Id, new[] { Seg(Guid.NewGuid(), 30, 90, "mail", "y") }, default);

        var day = await _reports.GetDayAsync(new DateOnly(2026, 10, 16), null);
        Assert.Equal(TimeSpan.FromMinutes(90), day.Active);
        Assert.Equal(2, day.Devices.Count);
        Assert.Equal(TimeSpan.FromMinutes(60), day.Devices.Single(d => d.DeviceName == "A").Active);

        var onlyA = await _reports.GetDayAsync(new DateOnly(2026, 10, 16), a.Id);
        Assert.Equal(TimeSpan.FromMinutes(60), onlyA.Active);
    }

    [Fact]
    public async Task Booking_suggestions_merge_gaps_and_skip_already_booked()
    {
        var (device, _) = await _catalog.CreateDeviceAsync("Laptop");
        var acme = await _catalog.SaveProjectAsync(new Project { Name = "Acme" });
        await _catalog.SaveRuleAsync(new Rule { ProjectId = acme.Id, Field = RuleField.App, Match = RuleMatch.Equals, Pattern = "code" });
        await _ingest.UpsertAsync(device.Id, new[]
        {
            Seg(Guid.NewGuid(), 0, 20, "code", "a"),
            Seg(Guid.NewGuid(), 23, 40, "code", "b"),   // 3 min lucka => slås ihop
            Seg(Guid.NewGuid(), 60, 90, "code", "c"),   // 20 min lucka => egen post
        }, default);

        var day = new DateOnly(2026, 10, 16);
        var s = await _entries.SuggestAsync(day, null);
        Assert.Equal(2, s.Count);
        Assert.Equal(TimeSpan.FromMinutes(40), s[0].Duration);
        Assert.Equal(TimeSpan.FromMinutes(30), s[1].Duration);

        await _entries.BookAsync(new[] { s[0] });
        s = await _entries.SuggestAsync(day, null);
        var rest = Assert.Single(s);
        Assert.Equal(T0.AddMinutes(60).UtcDateTime, rest.StartUtc);

        var range = await _reports.GetRangeAsync(day, day, null);
        Assert.Equal(TimeSpan.FromMinutes(40), range.Booked);
        Assert.Equal(TimeSpan.FromMinutes(67), range.Active);
    }

    [Fact]
    public async Task Range_summary_handles_unassigned_activities()
    {
        var (device, _) = await _catalog.CreateDeviceAsync("Laptop");
        var acme = await _catalog.SaveProjectAsync(new Project { Name = "Acme" });
        await _catalog.SaveRuleAsync(new Rule { ProjectId = acme.Id, Field = RuleField.App, Match = RuleMatch.Equals, Pattern = "code" });
        await _ingest.UpsertAsync(device.Id, new[]
        {
            Seg(Guid.NewGuid(), 0, 30, "code", "a"),
            Seg(Guid.NewGuid(), 30, 50, "browser", "nyheter"),
        }, default);

        var day = new DateOnly(2026, 10, 16);
        var r = await _reports.GetRangeAsync(day.AddDays(-1), day, null);
        Assert.Equal(TimeSpan.FromMinutes(50), r.Active);
        Assert.Equal(2, r.Projects.Count);
        Assert.Contains(r.ProjectOrder, p => p.Id is null);
        Assert.Equal(TimeSpan.FromMinutes(20), r.ByDayAndProject[(day, null)]);
        Assert.Equal(TimeSpan.Zero, r.BookedFor(null));
        Assert.Equal(TimeSpan.Zero, r.ByDay.GetValueOrDefault(day.AddDays(-1)));
    }

    [Fact]
    public async Task Zero_length_segments_are_dropped()
    {
        var (device, _) = await _catalog.CreateDeviceAsync("Laptop");
        var id = Guid.NewGuid();
        await _ingest.UpsertAsync(device.Id, new[] { Seg(id, 0, 5, "code", "x", open: true) }, default);
        await _ingest.UpsertAsync(device.Id, new[] { Seg(id, 0, 0, "code", "x") }, default);
        var day = await _reports.GetDayAsync(new DateOnly(2026, 10, 16), null);
        Assert.True(day.IsEmpty);
    }
}
