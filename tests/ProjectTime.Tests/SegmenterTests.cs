using ProjectTime.Agent.Tracking;

namespace ProjectTime.Tests;

public class SegmenterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(3);

    private static ActiveWindowInfo Win(string app, string title = "x") => new(app, title, null);

    [Fact]
    public void Same_window_extends_current_segment()
    {
        var s = new Segmenter(IdleAfter, Interval);
        Assert.Empty(s.Tick(T0, Win("code"), TimeSpan.Zero));
        Assert.Empty(s.Tick(T0.AddSeconds(2), Win("code"), TimeSpan.Zero));
        Assert.Empty(s.Tick(T0.AddSeconds(4), Win("code"), TimeSpan.Zero));

        Assert.NotNull(s.Current);
        Assert.Equal(T0, s.Current!.StartUtc);
        Assert.Equal(T0.AddSeconds(4), s.Current.EndUtc);
        Assert.True(s.Current.IsOpen);
    }

    [Fact]
    public void Window_change_closes_previous_segment_without_gap()
    {
        var s = new Segmenter(IdleAfter, Interval);
        s.Tick(T0, Win("code"), TimeSpan.Zero);
        s.Tick(T0.AddSeconds(2), Win("code"), TimeSpan.Zero);
        var closed = s.Tick(T0.AddSeconds(4), Win("browser"), TimeSpan.Zero);

        var c = Assert.Single(closed);
        Assert.Equal("code", c.AppName);
        Assert.Equal(T0, c.StartUtc);
        Assert.Equal(T0.AddSeconds(4), c.EndUtc);
        Assert.False(c.IsOpen);
        Assert.Equal("browser", s.Current!.AppName);
        Assert.Equal(T0.AddSeconds(4), s.Current.StartUtc);
    }

    [Fact]
    public void Idle_threshold_cuts_active_segment_retroactively()
    {
        var s = new Segmenter(IdleAfter, Interval);
        var t = T0;
        // Aktiv i en minut, sedan orörd. Inaktivitet märks först efter tröskeln.
        for (; t < T0.AddMinutes(1); t += Interval) s.Tick(t, Win("code"), TimeSpan.Zero);
        var idleStart = t;
        for (; t < idleStart + IdleAfter; t += Interval) Assert.Empty(s.Tick(t, Win("code"), t - idleStart));

        var closed = s.Tick(t, Win("code"), t - idleStart);
        var c = Assert.Single(closed);
        Assert.False(c.IsIdle);
        Assert.Equal(idleStart, c.EndUtc); // klipptes tillbaka till när aktiviteten upphörde
        Assert.True(s.Current!.IsIdle);
        Assert.Equal(idleStart, s.Current.StartUtc);
    }

    [Fact]
    public void Activity_after_idle_closes_idle_segment()
    {
        var s = new Segmenter(IdleAfter, Interval);
        s.Tick(T0, Win("code"), TimeSpan.Zero);
        s.Tick(T0.AddMinutes(5), Win("code"), TimeSpan.FromMinutes(5)); // gap > tröskel: stänger och skapar inaktiv
        var closed = s.Tick(T0.AddMinutes(5).AddSeconds(2), Win("mail"), TimeSpan.Zero);

        Assert.Contains(closed, c => c.IsIdle);
        Assert.Equal("mail", s.Current!.AppName);
        Assert.False(s.Current.IsIdle);
    }

    [Fact]
    public void Long_gap_between_ticks_closes_segment_at_last_tick()
    {
        var s = new Segmenter(IdleAfter, Interval);
        s.Tick(T0, Win("code"), TimeSpan.Zero);
        s.Tick(T0.AddSeconds(2), Win("code"), TimeSpan.Zero);
        // Datorn sov i en timme; när den vaknar är inmatningen "färsk" (användaren väckte den).
        var closed = s.Tick(T0.AddHours(1), Win("code"), TimeSpan.Zero);

        var c = Assert.Single(closed);
        Assert.Equal(T0.AddSeconds(2), c.EndUtc);
        Assert.Equal(T0.AddHours(1), s.Current!.StartUtc);
    }

    [Fact]
    public void Null_window_closes_current_and_tracks_nothing()
    {
        var s = new Segmenter(IdleAfter, Interval);
        s.Tick(T0, Win("code"), TimeSpan.Zero);
        var closed = s.Tick(T0.AddSeconds(2), null, TimeSpan.Zero);
        Assert.Single(closed);
        Assert.Null(s.Current);
        Assert.Empty(s.Tick(T0.AddSeconds(4), null, TimeSpan.Zero));
    }

    [Fact]
    public void Flush_closes_open_segment()
    {
        var s = new Segmenter(IdleAfter, Interval);
        s.Tick(T0, Win("code"), TimeSpan.Zero);
        var f = s.Flush(T0.AddSeconds(3));
        Assert.NotNull(f);
        Assert.False(f!.IsOpen);
        Assert.Equal(T0.AddSeconds(3), f.EndUtc);
        Assert.Null(s.Current);
    }
}
