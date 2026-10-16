namespace ProjectTime.Server;

public sealed class ServerOptions
{
    public const string SectionName = "ProjectTime";

    /// <summary>Katalog där databas och krypteringsnycklar lagras.</summary>
    public string DataDirectory { get; set; } = "data";

    /// <summary>Lösenord för webbgränssnittet. Tomt = ingen inloggning (använd bara på ett skyddat nätverk).</summary>
    public string? Password { get; set; }

    /// <summary>IANA- eller Windows-tidszon som dagar och tider visas i. Tomt = serverns lokala tidszon.</summary>
    public string? TimeZone { get; set; }

    /// <summary>Aktiviteter kortare än detta (sekunder) döljs i detaljlistor men räknas i summeringar.</summary>
    public int MinVisibleSeconds { get; set; } = 5;

    /// <summary>Luckor kortare än detta (minuter) slås ihop när tidsposter skapas från aktiviteter.</summary>
    public int BookingMergeGapMinutes { get; set; } = 5;

    public TimeZoneInfo ResolveTimeZone()
    {
        if (string.IsNullOrWhiteSpace(TimeZone)) return TimeZoneInfo.Local;
        try { return TimeZoneInfo.FindSystemTimeZoneById(TimeZone); }
        catch { return TimeZoneInfo.Local; }
    }
}
