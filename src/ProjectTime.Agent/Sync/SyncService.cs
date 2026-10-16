using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectTime.Agent.Platform;
using ProjectTime.Agent.Storage;
using ProjectTime.Shared;

namespace ProjectTime.Agent.Sync;

/// <summary>Skickar buffrade segment till servern i batchar och tål att servern är onåbar.</summary>
public sealed class SyncService : BackgroundService
{
    private readonly LocalStore _store;
    private readonly IHttpClientFactory _httpFactory;
    private readonly AgentOptions _options;
    private readonly ILogger<SyncService> _logger;
    private static readonly string Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public SyncService(LocalStore store, IHttpClientFactory httpFactory, IOptions<AgentOptions> options, ILogger<SyncService> logger)
    {
        _store = store;
        _httpFactory = httpFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogError("Ingen API-nyckel konfigurerad (Agent:ApiKey). Spårningen buffras lokalt men synkas inte.");
        }

        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.SyncIntervalSeconds));
        var failures = 0;
        var lastPurge = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = failures == 0 ? interval : TimeSpan.FromSeconds(Math.Min(300, interval.TotalSeconds * Math.Pow(2, Math.Min(failures, 5))));
            try { await Task.Delay(delay, stoppingToken); } catch (OperationCanceledException) { break; }

            if (string.IsNullOrWhiteSpace(_options.ApiKey)) continue;

            try
            {
                var ok = await SyncOnceAsync(stoppingToken);
                if (ok) { if (failures > 0) _logger.LogInformation("Kontakten med servern är tillbaka."); failures = 0; }
                else failures++;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                failures++;
                if (failures == 1 || failures % 10 == 0)
                    _logger.LogWarning("Synk misslyckades ({Count} i rad, {Pending} väntar): {Message}", failures, _store.CountUnsynced(), ex.Message);
            }

            if (DateTimeOffset.UtcNow - lastPurge > TimeSpan.FromHours(6))
            {
                lastPurge = DateTimeOffset.UtcNow;
                var purged = _store.Purge(_options.LocalRetentionDays);
                if (purged > 0) _logger.LogDebug("Rensade {Count} gamla segment ur bufferten", purged);
            }
        }

        // Sista försök att tömma bufferten vid avslut.
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            if (!string.IsNullOrWhiteSpace(_options.ApiKey)) await SyncOnceAsync(cts.Token);
        }
        catch { /* bufferten ligger kvar lokalt */ }
    }

    private async Task<bool> SyncOnceAsync(CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("server");
        var platform = WindowProviderFactory.PlatformName();

        while (true)
        {
            var pending = _store.GetUnsynced(AgentApi.MaxBatchSize);
            if (pending.Count == 0)
            {
                var hb = await client.PostAsJsonAsync(AgentApi.HeartbeatPath, new HeartbeatRequest(_options.DeviceName, platform, Version), ct);
                return Check(hb);
            }

            var request = new ActivityBatchRequest(_options.DeviceName, platform, Version, pending.Select(p => p.Dto).ToList());
            var response = await client.PostAsJsonAsync(AgentApi.ActivitiesPath, request, ct);
            if (!Check(response)) return false;

            _store.MarkSynced(pending);
            _logger.LogDebug("Synkade {Count} segment", pending.Count);
            if (pending.Count < AgentApi.MaxBatchSize) return true;
        }
    }

    private bool Check(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return true;
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            _logger.LogError("Servern avvisade API-nyckeln (401). Kontrollera Agent:ApiKey.");
        else
            _logger.LogWarning("Servern svarade {Status}", (int)response.StatusCode);
        return false;
    }
}
