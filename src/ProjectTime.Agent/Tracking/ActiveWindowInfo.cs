namespace ProjectTime.Agent.Tracking;

/// <summary>Ögonblicksbild av det fönster som har fokus.</summary>
public sealed record ActiveWindowInfo(string AppName, string WindowTitle, string? ProcessPath);

public interface IActiveWindowProvider
{
    /// <summary>Returnerar det aktiva fönstret eller null om det inte går att läsa av (låst skärm, inget fönster, saknade verktyg).</summary>
    ActiveWindowInfo? GetActiveWindow();

    /// <summary>Tid sedan senaste tangentbords-/musaktivitet.</summary>
    TimeSpan GetIdleTime();
}
