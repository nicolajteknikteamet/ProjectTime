namespace ProjectTime.Server.Services;

/// <summary>Hjälpfunktioner för tidszon och formatering i gränssnittet.</summary>
public sealed class Clock
{
    public Clock(TimeZoneInfo zone) => Zone = zone;

    public TimeZoneInfo Zone { get; }

    public DateOnly Today => DateOnly.FromDateTime(ToLocal(DateTime.UtcNow));

    public DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    public DateTime ToUtc(DateTime local) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);

    /// <summary>UTC-gränserna [start, slut) för en lokal dag.</summary>
    public (DateTime StartUtc, DateTime EndUtc) DayBounds(DateOnly day)
    {
        var start = ToUtc(day.ToDateTime(TimeOnly.MinValue));
        var end = ToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue));
        return (start, end);
    }

    public DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));

    public static string Hm(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.TotalMinutes < 1) return $"{(int)t.TotalSeconds} s";
        var h = (int)t.TotalHours;
        var m = t.Minutes;
        if (h == 0) return $"{m} min";
        return m == 0 ? $"{h} h" : $"{h} h {m} min";
    }

    public static string Hours(TimeSpan t) => (t.TotalHours).ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("sv-SE"));

    public string Time(DateTime utc) => ToLocal(utc).ToString("HH:mm");
    public string TimeSec(DateTime utc) => ToLocal(utc).ToString("HH:mm:ss");

    public static string DayName(DateOnly d)
    {
        var sv = System.Globalization.CultureInfo.GetCultureInfo("sv-SE");
        var s = d.ToString("dddd d MMMM yyyy", sv);
        return char.ToUpper(s[0], sv) + s[1..];
    }

    public static string ShortDay(DateOnly d)
    {
        var sv = System.Globalization.CultureInfo.GetCultureInfo("sv-SE");
        return d.ToString("ddd d/M", sv);
    }

    public static DateOnly StartOfWeek(DateOnly d)
    {
        var diff = ((int)d.DayOfWeek + 6) % 7; // måndag = 0
        return d.AddDays(-diff);
    }
}
