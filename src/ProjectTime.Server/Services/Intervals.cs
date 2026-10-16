namespace ProjectTime.Server.Services;

/// <summary>Intervallaritmetik: union, klippning och sammanslagning av tidsintervall.</summary>
public static class Intervals
{
    public readonly record struct Span(DateTime Start, DateTime End)
    {
        public TimeSpan Duration => End - Start;
    }

    /// <summary>Summerar intervall utan att räkna överlapp dubbelt (t.ex. två datorer samtidigt).</summary>
    public static TimeSpan UnionDuration(IEnumerable<Span> spans)
    {
        var total = TimeSpan.Zero;
        foreach (var s in Merge(spans, TimeSpan.Zero)) total += s.Duration;
        return total;
    }

    /// <summary>Slår ihop överlappande intervall och intervall vars lucka är högst <paramref name="maxGap"/>.</summary>
    public static List<Span> Merge(IEnumerable<Span> spans, TimeSpan maxGap)
    {
        var result = new List<Span>();
        foreach (var s in spans.Where(s => s.End > s.Start).OrderBy(s => s.Start))
        {
            if (result.Count > 0 && s.Start - result[^1].End <= maxGap)
            {
                var last = result[^1];
                if (s.End > last.End) result[^1] = last with { End = s.End };
            }
            else result.Add(s);
        }
        return result;
    }

    public static Span? Clip(DateTime start, DateTime end, DateTime lo, DateTime hi)
    {
        var s = start < lo ? lo : start;
        var e = end > hi ? hi : end;
        return e > s ? new Span(s, e) : null;
    }
}
