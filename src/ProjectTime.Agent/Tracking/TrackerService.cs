using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectTime.Agent.Storage;

namespace ProjectTime.Agent.Tracking;

/// <summary>Läser av aktivt fönster periodiskt och skriver segment till den lokala bufferten.</summary>
public sealed class TrackerService : BackgroundService
{
    private readonly IActiveWindowProvider _provider;
    private readonly LocalStore _store;
    private readonly AgentOptions _options;
    private readonly ILogger<TrackerService> _logger;
    private readonly HashSet<string> _excluded;

    public TrackerService(IActiveWindowProvider provider, LocalStore store, IOptions<AgentOptions> options, ILogger<TrackerService> logger)
    {
        _provider = provider;
        _store = store;
        _options = options.Value;
        _logger = logger;
        _excluded = new HashSet<string>(_options.ExcludedApps, StringComparer.OrdinalIgnoreCase);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.SampleIntervalSeconds));
        var segmenter = new Segmenter(TimeSpan.FromSeconds(Math.Max(10, _options.IdleThresholdSeconds)), interval);
        _logger.LogInformation("Spårning startad: intervall {Interval}s, inaktiv efter {Idle}s, titlar={Titles}",
            interval.TotalSeconds, _options.IdleThresholdSeconds, _options.TrackWindowTitles);

        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { Sample(segmenter); }
                catch (Exception ex) { _logger.LogError(ex, "Fel vid avläsning"); }
            }
        }
        catch (OperationCanceledException) { /* avslut */ }

        var last = segmenter.Flush(DateTimeOffset.UtcNow);
        if (last is not null) _store.Upsert(last);
    }

    private void Sample(Segmenter segmenter)
    {
        var now = DateTimeOffset.UtcNow;
        var window = _provider.GetActiveWindow();
        var idle = _provider.GetIdleTime();

        if (window is not null)
        {
            if (_excluded.Contains(window.AppName))
                window = new ActiveWindowInfo(window.AppName, "", null);
            else if (!_options.TrackWindowTitles)
                window = window with { WindowTitle = "" };
        }

        var previous = segmenter.Current;
        var closed = segmenter.Tick(now, window, idle);

        foreach (var s in closed) _store.Upsert(s);

        // Ett segment som försvann utan att avslutas (nollängd efter inaktivitetsklippning) ska bort ur bufferten.
        if (previous is not null && !ReferenceEquals(previous, segmenter.Current) && !closed.Contains(previous))
            _store.Delete(previous.ClientId);

        if (segmenter.Current is { } cur) _store.Upsert(cur);
    }
}
