using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Swankers.Web.Presenter;

/// <summary>
/// Presenter sign-in. The stage page chats as the server's Foundry identity and approves with
/// the demo admin key, so anonymous visitors must not reach it (Codex Phase 6 P1). One shared
/// key from Key Vault, checked in constant time, then a cookie session; every component
/// endpoint requires that session (Program.cs). /healthz stays anonymous.
/// </summary>
public static class PresenterLogin
{
    public const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;
    public const string Role = "presenter";
    public const string CookieName = "swankers-presenter";

    public static IEndpointRouteBuilder MapPresenterLogin(this IEndpointRouteBuilder app)
    {
        app.MapGet("/login", (HttpContext http, IAntiforgery antiforgery, bool? failed) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(http);
            return Results.Content(Page(tokens.FormFieldName!, tokens.RequestToken!, failed == true), "text/html; charset=utf-8");
        }).AllowAnonymous();

        // Form binding makes the antiforgery middleware validate the token from the page above.
        app.MapPost("/login", async (HttpContext http, IOptions<PresenterOptions> options, [FromForm] string? key) =>
        {
            if (!Matches(key, options.Value.PresenterKey))
            {
                return Results.Redirect("/login?failed=true");
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "presenter"), new Claim(ClaimTypes.Role, Role)], Scheme);
            await http.SignInAsync(Scheme, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
            return Results.Redirect("/");
        }).AllowAnonymous();

        app.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(Scheme);
            return Results.Redirect("/login");
        });

        return app;
    }

    /// <summary>Constant-time comparison; an empty configured key never matches.</summary>
    public static bool Matches(string? presented, string configured)
        => configured.Length > 0
           && presented is not null
           && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(configured));

    private static string Page(string tokenField, string token, bool failed) => $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            <title>Swankers Coach: presenter sign-in</title>
            <link rel="stylesheet" href="app.css" />
            <style>
                .login { max-width: 420px; margin: 12vh auto; padding: 1.6rem; background: var(--panel); border: 1px solid var(--line); border-radius: 12px; }
                .login h1 { font-size: 1.4rem; margin-bottom: 0.4rem; }
                .login p { color: var(--muted); }
                .login input { width: 100%; padding: 0.7rem 0.9rem; margin: 0.8rem 0; border-radius: 10px; border: 1px solid var(--line); background: var(--bg); color: var(--text); font-size: 1rem; }
                .login button { width: 100%; }
            </style>
        </head>
        <body>
            <main class="login">
                <h1>🏈 Swankers Coach</h1>
                <p>This page acts on the league as Coach. Presenter key required.</p>
                {{(failed ? "<p class=\"error\">That key did not match.</p>" : "")}}
                <form method="post" action="/login">
                    <input type="hidden" name="{{tokenField}}" value="{{token}}" />
                    <input type="password" name="key" placeholder="Presenter key" autocomplete="current-password" autofocus required />
                    <button type="submit">Enter the stage</button>
                </form>
            </main>
        </body>
        </html>
        """;
}
