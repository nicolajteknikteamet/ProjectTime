namespace ProjectTime.Server.Services;

/// <summary>Kategorisk färgpalett (validerad för färgblindhet) som nya projekt tilldelas i fast ordning.</summary>
public static class Palette
{
    public static readonly string[] Categorical =
    {
        "#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#6250d6", "#e34948",
    };

    public const string Unassigned = "#9a9890";
    public const string Idle = "var(--idle)"; // CSS-variabel så att inaktiv tid följer ljust/mörkt tema

    public static string NextUnused(IEnumerable<string> used)
    {
        var set = new HashSet<string>(used, StringComparer.OrdinalIgnoreCase);
        return Categorical.FirstOrDefault(c => !set.Contains(c)) ?? Categorical[set.Count % Categorical.Length];
    }

    /// <summary>Vit eller mörk text beroende på bakgrundens ljushet.</summary>
    public static string InkOn(string hex)
    {
        if (hex.Length < 7) return "#0b0b0b";
        var r = Convert.ToInt32(hex.Substring(1, 2), 16) / 255d;
        var g = Convert.ToInt32(hex.Substring(3, 2), 16) / 255d;
        var b = Convert.ToInt32(hex.Substring(5, 2), 16) / 255d;
        var lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        return lum > 0.55 ? "#0b0b0b" : "#ffffff";
    }
}
