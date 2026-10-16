using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectTime.Server;
using ProjectTime.Server.Api;
using ProjectTime.Server.Auth;
using ProjectTime.Server.Components;
using ProjectTime.Server.Data;
using ProjectTime.Server.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables("PROJECTTIME_");

var options = builder.Configuration.GetSection(ServerOptions.SectionName).Get<ServerOptions>() ?? new ServerOptions();

// I Azure App Service är bara %HOME% (D:\home resp. /home) beständigt mellan omstarter.
// Om ingen katalog angetts uttryckligen läggs databasen där.
var home = Environment.GetEnvironmentVariable("HOME");
var inAppService = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME")) && !string.IsNullOrEmpty(home);
if (inAppService && string.IsNullOrWhiteSpace(builder.Configuration["ProjectTime:DataDirectory"]))
    options.DataDirectory = Path.Combine(home!, "data");
var dataDir = Path.GetFullPath(options.DataDirectory);
Directory.CreateDirectory(dataDir);

// Lyssna på 5080 lokalt om inget annat angetts; plattformar som App Service på Linux anger porten via PORT.
if (string.IsNullOrEmpty(builder.Configuration["urls"]) && string.IsNullOrEmpty(builder.Configuration["HTTP_PORTS"]))
    builder.WebHost.UseUrls($"http://0.0.0.0:{Environment.GetEnvironmentVariable("PORT") ?? "5080"}");

builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection(ServerOptions.SectionName));
var timeZone = options.ResolveTimeZone();
builder.Services.AddSingleton(new Clock(timeZone));

builder.Services.AddDbContextFactory<AppDbContext>(o =>
    o.UseSqlite($"Data Source={Path.Combine(dataDir, "projecttime.db")}"));

builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));

builder.Services.AddScoped<ActivityIngestService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<TimeEntryService>();

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var requireLogin = !string.IsNullOrEmpty(options.Password);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.Cookie.Name = "projecttime.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromDays(30);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

// Skapa/uppdatera databasschemat vid start.
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.MigrateAsync();

    // EF Core slår på WAL när databasen skapas. WAL fungerar inte på nätverksfilsystem (App Service lagrar %HOME% på en SMB-resurs),
    // så där används klassisk rollback-journal i stället.
    var journal = (options.SqliteJournalMode ?? (inAppService ? "DELETE" : "WAL")).ToUpperInvariant();
    if (journal is "DELETE" or "WAL" or "TRUNCATE" or "PERSIST")
        await db.Database.ExecuteSqlRawAsync($"PRAGMA journal_mode={journal};");
    else
        app.Logger.LogWarning("Okänt SqliteJournalMode '{Mode}' ignoreras.", journal);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapAgentApi();
app.MapLogin();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
var exports = app.MapGroup("/export");
exports.MapGet("/tid.csv", ExportEndpoints.ExportTimeEntries);
exports.MapGet("/aktiviteter.csv", ExportEndpoints.ExportActivities);

var components = app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
if (requireLogin)
{
    components.RequireAuthorization();
    exports.RequireAuthorization();
}
else
{
    app.Logger.LogWarning("Inget lösenord satt (ProjectTime:Password). Webbgränssnittet är öppet för alla som når servern.");
}

if (!string.IsNullOrWhiteSpace(options.TimeZone) && !string.Equals(timeZone.Id, options.TimeZone, StringComparison.OrdinalIgnoreCase) && !timeZone.HasSameRules(TimeZoneInfo.Utc))
    app.Logger.LogWarning("Tidszonen {Configured} kunde inte tolkas exakt; använder {Actual}.", options.TimeZone, timeZone.Id);
app.Logger.LogInformation("ProjectTime Server startar. Data: {Dir}, tidszon: {Tz}", dataDir, timeZone.Id);
app.Run();

public partial class Program { }
