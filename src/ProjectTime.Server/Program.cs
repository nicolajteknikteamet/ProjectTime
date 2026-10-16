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
var dataDir = Path.GetFullPath(options.DataDirectory);
Directory.CreateDirectory(dataDir);

builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection(ServerOptions.SectionName));
builder.Services.AddSingleton(new Clock(options.ResolveTimeZone()));

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

app.Logger.LogInformation("ProjectTime Server startar. Data: {Dir}, tidszon: {Tz}", dataDir, options.ResolveTimeZone().Id);
app.Run();

public partial class Program { }
