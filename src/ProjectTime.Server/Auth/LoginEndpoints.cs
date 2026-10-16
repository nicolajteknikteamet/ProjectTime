using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace ProjectTime.Server.Auth;

/// <summary>Enkel lösenordsinloggning för webbgränssnittet (en användare, cookie-session).</summary>
public static class LoginEndpoints
{
    public static IEndpointRouteBuilder MapLogin(this IEndpointRouteBuilder app)
    {
        app.MapGet("/login", (HttpContext ctx, string? error) =>
            Results.Content(Page(error is not null), "text/html; charset=utf-8")).AllowAnonymous();

        app.MapPost("/login", async (HttpContext ctx, IOptions<ServerOptions> options) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var password = form["password"].ToString();
            var expected = options.Value.Password ?? "";
            var ok = expected.Length > 0 && CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(expected));
            if (!ok)
            {
                await Task.Delay(Random.Shared.Next(300, 800));
                return Results.Redirect("/login?error=1");
            }
            var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "admin") }, CookieAuthenticationDefaults.AuthenticationScheme);
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30) });
            return Results.Redirect("/");
        }).AllowAnonymous().DisableAntiforgery();

        app.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        }).DisableAntiforgery();

        return app;
    }

    private static string Page(bool error) => $$"""
        <!doctype html>
        <html lang="sv"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>ProjectTime – logga in</title><link rel="stylesheet" href="/app.css"></head>
        <body class="login-body">
        <form method="post" action="/login" class="login-card">
          <h1>ProjectTime</h1>
          <p class="muted">Ange lösenordet för att se din tid.</p>
          {{(error ? "<p class=\"error\">Fel lösenord.</p>" : "")}}
          <label>Lösenord<input type="password" name="password" autofocus required autocomplete="current-password"></label>
          <button type="submit" class="btn primary">Logga in</button>
        </form></body></html>
        """;
}
