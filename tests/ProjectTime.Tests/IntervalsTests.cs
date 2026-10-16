using ProjectTime.Server.Services;
using static ProjectTime.Server.Services.Intervals;

namespace ProjectTime.Tests;

public class IntervalsTests
{
    private static readonly DateTime T0 = new(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc);
    private static Span S(int fromMin, int toMin) => new(T0.AddMinutes(fromMin), T0.AddMinutes(toMin));

    [Fact]
    public void Union_does_not_double_count_overlap()
    {
        // Två datorer aktiva samtidigt 08:00–08:30 och 08:15–08:45 => 45 min, inte 60.
        var total = UnionDuration(new[] { S(0, 30), S(15, 45) });
        Assert.Equal(TimeSpan.FromMinutes(45), total);
    }

    [Fact]
    public void Merge_bridges_small_gaps_only()
    {
        var merged = Merge(new[] { S(0, 10), S(12, 20), S(40, 50) }, TimeSpan.FromMinutes(5));
        Assert.Equal(2, merged.Count);
        Assert.Equal(S(0, 20), merged[0]);
        Assert.Equal(S(40, 50), merged[1]);
    }

    [Fact]
    public void Clip_returns_null_when_outside()
    {
        Assert.Null(Clip(T0, T0.AddMinutes(10), T0.AddMinutes(20), T0.AddMinutes(30)));
        var c = Clip(T0, T0.AddMinutes(30), T0.AddMinutes(10), T0.AddMinutes(20));
        Assert.Equal(S(10, 20), c);
    }
}
