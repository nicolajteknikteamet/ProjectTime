using Microsoft.Extensions.Options;
using ProjectTime.Agent;
using ProjectTime.Agent.Platform;
using ProjectTime.Agent.Storage;
using ProjectTime.Agent.Sync;
using ProjectTime.Agent.Tracking;
using ProjectTime.Shared;

// Innehållsroten sätts till programkatalogen så att appsettings.json hittas även när agenten startas som tjänst.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });

// Konfiguration läses från appsettings.json, miljövariabler (PROJECTTIME_Agent__ApiKey=...) och kommandorad (--Agent:ApiKey=...).
builder.Configuration.AddEnvironmentVariables("PROJECTTIME_");
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));

builder.Services.AddWindowsService(o => o.ServiceName = "ProjectTime Agent");
builder.Services.AddSystemd();

builder.Services.AddSingleton<IActiveWindowProvider>(WindowProviderFactory.Create);
builder.Services.AddSingleton(sp => new LocalStore(sp.GetRequiredService<IOptions<AgentOptions>>().Value.ResolveDataDirectory()));
builder.Services.AddHttpClient("server", (sp, client) =>
{
    var o = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
    client.BaseAddress = new Uri(o.ServerUrl.TrimEnd('/') + "/");
    client.DefaultRequestHeaders.Add(AgentApi.ApiKeyHeader, o.ApiKey);
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHostedService<TrackerService>();
builder.Services.AddHostedService<SyncService>();

var host = builder.Build();
var opts = host.Services.GetRequiredService<IOptions<AgentOptions>>().Value;
host.Services.GetRequiredService<ILogger<Program>>().LogInformation(
    "ProjectTime Agent startar. Enhet={Device} Server={Server} Buffert={Db}",
    opts.DeviceName, opts.ServerUrl, host.Services.GetRequiredService<LocalStore>().DatabasePath);
host.Run();
