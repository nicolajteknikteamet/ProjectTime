using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ProjectTime.Server.Data;
using ProjectTime.Server.Services;

namespace ProjectTime.Server.Api;

public static class ExportEndpoints
{
    public static async Task<IResult> ExportTimeEntries(DateOnly from, DateOnly to, TimeEntryService entries, Clock clock)
    {
        var rows = await entries.GetRangeAsync(from, to);
        var sb = new StringBuilder();
        sb.AppendLine("Datum;Start;Slut;Timmar;Projekt;Kund;Anteckning");
        foreach (var e in rows)
        {
            var s = clock.ToLocal(e.StartUtc);
            var t = clock.ToLocal(e.EndUtc);
            sb.AppendLine(string.Join(';',
                s.ToString("yyyy-MM-dd"), s.ToString("HH:mm"), t.ToString("HH:mm"),
                Clock.Hours(e.Duration), Csv(e.Project?.Name), Csv(e.Project?.Client), Csv(e.Note)));
        }
        return Csv(sb, $"tid_{from:yyyyMMdd}_{to:yyyyMMdd}.csv");
    }

    public static async Task<IResult> ExportActivities(DateOnly from, DateOnly to, IDbContextFactory<AppDbContext> factory, Clock clock)
    {
        var lo = clock.DayBounds(from).StartUtc;
        var hi = clock.DayBounds(to).EndUtc;
        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.Activities.AsNoTracking().Include(a => a.Device).Include(a => a.Project)
            .Where(a => a.EndUtc > lo && a.StartUtc < hi).OrderBy(a => a.StartUtc).ToListAsync();
        var sb = new StringBuilder();
        sb.AppendLine("Datum;Start;Slut;Sekunder;Enhet;Program;Fönstertitel;Projekt;Inaktiv");
        foreach (var a in rows)
        {
            var s = clock.ToLocal(a.StartUtc);
            var t = clock.ToLocal(a.EndUtc);
            sb.AppendLine(string.Join(';',
                s.ToString("yyyy-MM-dd"), s.ToString("HH:mm:ss"), t.ToString("HH:mm:ss"),
                ((int)a.Duration.TotalSeconds).ToString(CultureInfo.InvariantCulture),
                Csv(a.Device?.Name), Csv(a.AppName), Csv(a.WindowTitle), Csv(a.Project?.Name), a.IsIdle ? "ja" : "nej"));
        }
        return Csv(sb, $"aktiviteter_{from:yyyyMMdd}_{to:yyyyMMdd}.csv");
    }

    private static string Csv(string? v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        var needs = v.Contains(';') || v.Contains('"') || v.Contains('\n');
        return needs ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    private static IResult Csv(StringBuilder sb, string fileName)
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return Results.File(bytes, "text/csv; charset=utf-8", fileName);
    }
}
