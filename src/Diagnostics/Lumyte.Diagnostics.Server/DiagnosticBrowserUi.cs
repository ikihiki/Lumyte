using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Cors;

namespace Lumyte.Diagnostics.Server;

internal static class DiagnosticBrowserUi
{
    internal const string Scheme = "DiagnosticBrowser";

    internal static void Map(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/diagnostics") && !context.Request.Path.StartsWithSegments("/Lumyte.Diagnostics"))
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'";
            }

            if (context.Request.Path.StartsWithSegments("/_blazor"))
            {
                if (context.User.Identity?.IsAuthenticated != true)
                {
                    context.Response.StatusCode = 401;
                    return;
                }

                string origin = context.Request.Headers.Origin.ToString();
                string expected = context.Request.Scheme + "://" + context.Request.Host;
                if (origin.Length > 0 && !string.Equals(origin, expected, StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = 403;
                    return;
                }
            }

            await next(context).ConfigureAwait(false);
        });
        app.MapGet("/assets/diagnostics.css", () => Results.Stream(typeof(DiagnosticBrowserUi).Assembly.GetManifestResourceStream("Lumyte.Diagnostics.Server.Web.diagnostics.css")!, "text/css; charset=utf-8")).WithMetadata(new DisableCorsAttribute());
        RouteGroupBuilder group = app.MapGroup("/api/ui").WithMetadata(new DisableCorsAttribute());
        group.MapGet("/bootstrap", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            AuthenticateResult authentication = await context.AuthenticateAsync(Scheme).ConfigureAwait(false);
            context.User = authentication.Principal ?? new ClaimsPrincipal(new ClaimsIdentity());
            string token = antiforgery.GetAndStoreTokens(context).RequestToken!;
            return Results.Json(new DiagnosticUiBootstrap(authentication.Succeeded, token), DiagnosticUiJsonContext.Protocol.DiagnosticUiBootstrap);
        });
        group.MapPost("/login", LoginAsync).AddEndpointFilter<DiagnosticUiMutationFilter>().RequireRateLimiting("UiLogin");
        group.MapPost("/logout", async (HttpContext context, DiagnosticBrowserTickets tickets) =>
        {
            tickets.Revoke(context.User);
            await context.SignOutAsync(Scheme).ConfigureAwait(false);
            return context.Request.Query.ContainsKey("redirect") ? Results.Redirect("/") : Results.NoContent();
        }).RequireAuthorization(Scheme).AddEndpointFilter<DiagnosticUiMutationFilter>();
    }

    private static async Task<IResult> LoginAsync(HttpContext context, DiagnosticServerOptions options, DiagnosticBrowserTickets tickets)
    {
        if (!context.Request.HasFormContentType)
        {
            return Results.BadRequest();
        }

        IFormCollection form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
        string token = form["token"].ToString();
        if (token.Length > 512 || !DiagnosticAuthenticationHandler.Match(token, options.OperatorToken))
        {
            return context.Request.Query.ContainsKey("redirect") ? Results.Redirect("/?loginError=true") : Results.Unauthorized();
        }

        ClaimsPrincipal user = tickets.Create();
        await context.SignInAsync(Scheme, user, new AuthenticationProperties
        {
            IsPersistent = false,
            AllowRefresh = false,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30),
        }).ConfigureAwait(false);
        return context.Request.Query.ContainsKey("redirect") ? Results.Redirect("/") : Results.NoContent();
    }
}
