namespace ProjectTime.Agent.Tracking;

/// <summary>
/// Omvandlar periodiska avläsningar av aktivt fönster till sammanhängande segment.
/// Ren logik utan I/O så att den kan testas.
/// </summary>
public sealed class Segmenter
{
    private readonly TimeSpan _idleThreshold;
    private readonly TimeSpan _gapThreshold;
    private DateTimeOffset? _lastTick;
    private DateTimeOffset? _gapClosedAt;

    public Segmenter(TimeSpan idleThreshold, TimeSpan sampleInterval)
    {
        _idleThreshold = idleThreshold;
        // Om det gått mycket längre än ett intervall mellan två avläsningar har datorn troligen sovit.
        var gap = TimeSpan.FromTicks(sampleInterval.Ticks * 4);
        _gapThreshold = gap < TimeSpan.FromSeconds(15) ? TimeSpan.FromSeconds(15) : gap;
    }

    /// <summary>Segmentet som just nu byggs upp (kan vara null, t.ex. vid låst skärm).</summary>
    public Segment? Current { get; private set; }

    /// <summary>
    /// Registrerar en avläsning. Returnerar segment som avslutades i och med denna avläsning.
    /// <paramref name="window"/> är null när inget fönster kan läsas av.
    /// </summary>
    public IReadOnlyList<Segment> Tick(DateTimeOffset now, ActiveWindowInfo? window, TimeSpan idle)
    {
        var closed = new List<Segment>(2);

        // 1. Sömn/viloläge: avsluta pågående segment vid senaste kända tidpunkt.
        if (_lastTick is { } last && now - last > _gapThreshold && Current is not null)
        {
            Close(Current, last, closed);
            Current = null;
            _gapClosedAt = last;
        }

        var isIdleNow = idle >= _idleThreshold;

        if (isIdleNow)
        {
            var idleStart = now - idle;

            if (Current is { IsIdle: false } active)
            {
                // Användaren slutade arbeta vid idleStart – klipp det aktiva segmentet där.
                var cut = idleStart < active.StartUtc ? active.StartUtc : idleStart;
                if (cut < active.EndUtc) active.EndUtc = cut;
                Close(active, active.EndUtc, closed);
                Current = NewIdle(active.EndUtc, now);
            }
            else if (Current is null)
            {
                // Efter ett sömnavbrott får inaktiviteten inte överlappa det segment som redan avslutats.
                var start = _gapClosedAt is { } g && idleStart < g ? g : idleStart;
                Current = NewIdle(start, now);
            }
            else
            {
                Current.EndUtc = now;
            }
        }
        else
        {
            if (Current is { IsIdle: true } idleSeg)
            {
                Close(idleSeg, now, closed);
                Current = null;
            }

            if (window is null)
            {
                if (Current is not null)
                {
                    Close(Current, now, closed);
                    Current = null;
                }
            }
            else if (Current is not null && Current.SameActivityAs(window))
            {
                Current.EndUtc = now;
            }
            else
            {
                if (Current is not null) Close(Current, now, closed);
                Current = new Segment
                {
                    StartUtc = now,
                    EndUtc = now,
                    AppName = window.AppName,
                    WindowTitle = window.WindowTitle,
                    ProcessPath = window.ProcessPath,
                    IsIdle = false,
                };
            }
        }

        _lastTick = now;
        return closed;
    }

    /// <summary>Avslutar pågående segment, t.ex. när agenten stängs av.</summary>
    public Segment? Flush(DateTimeOffset now)
    {
        if (Current is null) return null;
        var s = Current;
        if (now > s.EndUtc) s.EndUtc = now;
        s.IsOpen = false;
        Current = null;
        return s;
    }

    private static Segment NewIdle(DateTimeOffset start, DateTimeOffset end) => new()
    {
        StartUtc = start,
        EndUtc = end,
        AppName = "",
        WindowTitle = "",
        IsIdle = true,
    };

    private static void Close(Segment s, DateTimeOffset end, List<Segment> closed)
    {
        if (end > s.EndUtc) s.EndUtc = end;
        s.IsOpen = false;
        if (s.EndUtc > s.StartUtc) closed.Add(s);
    }
}
