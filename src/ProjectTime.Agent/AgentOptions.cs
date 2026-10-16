namespace ProjectTime.Agent;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Bas-URL till ProjectTime-servern, t.ex. https://tid.example.com</summary>
    public string ServerUrl { get; set; } = "http://localhost:5080";

    /// <summary>API-nyckel som skapas per enhet i webbgränssnittet (Enheter).</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Namn på den här datorn. Standard är datorns värdnamn.</summary>
    public string DeviceName { get; set; } = Environment.MachineName;

    /// <summary>Hur ofta det aktiva fönstret läses av.</summary>
    public int SampleIntervalSeconds { get; set; } = 2;

    /// <summary>Hur länge tangentbord/mus måste vara orörda innan tiden räknas som inaktiv.</summary>
    public int IdleThresholdSeconds { get; set; } = 180;

    /// <summary>Hur ofta buffrade segment skickas till servern.</summary>
    public int SyncIntervalSeconds { get; set; } = 30;

    /// <summary>Katalog för den lokala bufferten (SQLite). Standard: %LOCALAPPDATA%/ProjectTime eller ~/.local/share/ProjectTime.</summary>
    public string? DataDirectory { get; set; }

    /// <summary>Om fönstertitlar ska registreras. Stäng av för att bara spåra program.</summary>
    public bool TrackWindowTitles { get; set; } = true;

    /// <summary>Program (processnamn, skiftlägesokänsligt) som aldrig ska registreras, t.ex. lösenordshanterare.</summary>
    public List<string> ExcludedApps { get; set; } = new();

    /// <summary>Hur många dagar synkade segment sparas lokalt innan de rensas.</summary>
    public int LocalRetentionDays { get; set; } = 14;

    public string ResolveDataDirectory()
    {
        if (!string.IsNullOrWhiteSpace(DataDirectory)) return DataDirectory;
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(baseDir))
            baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return Path.Combine(baseDir, "ProjectTime");
    }
}
