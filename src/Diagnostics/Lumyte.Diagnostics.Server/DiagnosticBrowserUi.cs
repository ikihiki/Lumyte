using System.Security.Claims;
using System.Text.Json;
using Lumyte.Diagnostics.Transport;
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
            if (context.Request.Path == "/" || context.Request.Path.StartsWithSegments("/assets") || context.Request.Path.StartsWithSegments("/api/ui"))
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
            }

            await next(context).ConfigureAwait(false);
        });
        app.MapGet("/", () => Asset("index.html", "text/html; charset=utf-8")).WithMetadata(new DisableCorsAttribute());
        app.MapGet("/assets/diagnostics.css", () => Asset("diagnostics.css", "text/css; charset=utf-8")).WithMetadata(new DisableCorsAttribute());
        app.MapGet("/assets/diagnostics.js", () => Asset("diagnostics.js", "text/javascript; charset=utf-8")).WithMetadata(new DisableCorsAttribute());
        RouteGroupBuilder group = app.MapGroup("/api/ui").WithMetadata(new DisableCorsAttribute());
        group.MapGet("/bootstrap", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            AuthenticateResult authentication = await context.AuthenticateAsync(Scheme).ConfigureAwait(false);
            context.User = authentication.Principal ?? new ClaimsPrincipal(new ClaimsIdentity());
            string token = antiforgery.GetAndStoreTokens(context).RequestToken!;
            return Results.Json(new DiagnosticUiBootstrap(authentication.Succeeded, token), DiagnosticUiJsonContext.Protocol.DiagnosticUiBootstrap);
        });
        group.MapPost("/login", LoginAsync).AddEndpointFilter<DiagnosticUiMutationFilter>().RequireRateLimiting("UiLogin");
        group.MapPost("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(Scheme).ConfigureAwait(false);
            return Results.NoContent();
        }).RequireAuthorization(Scheme).AddEndpointFilter<DiagnosticUiMutationFilter>();
        group.MapGet("/sessions", (DiagnosticSessionRegistry sessions) => Results.Json(sessions.List(), DiagnosticJson.Context.SessionSnapshotArray)).RequireAuthorization(Scheme);
        group.MapPost("/sessions/{id:guid}/operations", async (Guid id, HttpContext context, DiagnosticSessionRegistry sessions) =>
        {
            OperationInvocation? invocation = await JsonSerializer.DeserializeAsync(context.Request.Body, DiagnosticJson.Context.OperationInvocation, context.RequestAborted).ConfigureAwait(false);
            if (invocation == null)
            {
                return Results.BadRequest();
            }

            DiagnosticOperationResult result = await sessions.InvokeAsync(id, invocation, context.User.Identity!.Name!, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, DiagnosticJson.Context.DiagnosticOperationResult);
        }).RequireAuthorization(Scheme).AddEndpointFilter<DiagnosticUiMutationFilter>();
        group.MapDelete("/sessions/{id:guid}/connection", (Guid id, DiagnosticSessionRegistry sessions) =>
        {
            sessions.Close(id);
            return Results.NoContent();
        }).RequireAuthorization(Scheme).AddEndpointFilter<DiagnosticUiMutationFilter>();
        group.MapGet("/events", EventsAsync).RequireAuthorization(Scheme);
    }

    private static IResult Asset(string name, string contentType)
        => Results.Stream(typeof(DiagnosticBrowserUi).Assembly.GetManifestResourceStream("Lumyte.Diagnostics.Server.Web." + name)!, contentType);

    private static async Task<IResult> LoginAsync(HttpContext context, DiagnosticServerOptions options)
    {
        if (!context.Request.HasFormContentType)
        {
            return Results.BadRequest();
        }

        IFormCollection form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
        string token = form["token"].ToString();
        if (token.Length > 512 || !DiagnosticAuthenticationHandler.Match(token, options.OperatorToken))
        {
            return Results.Unauthorized();
        }

        var identity = new ClaimsIdentity([new(ClaimTypes.Name, "diagnostic-operator"), new("diagnostics.role", "operator")], Scheme);
        await context.SignInAsync(Scheme, new ClaimsPrincipal(identity), new AuthenticationProperties
        {
            IsPersistent = false,
            AllowRefresh = false,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30),
        }).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task EventsAsync(HttpContext context, DiagnosticSessionRegistry sessions, DiagnosticUiStreams streams)
    {
        string selected = context.Request.Query["sessionId"].ToString();
        Guid? selectedId = null;
        if (selected.Length > 0)
        {
            if (!Guid.TryParse(selected, out Guid id))
            {
                context.Response.StatusCode = 400;
                return;
            }

            selectedId = id;
        }

        if (!await streams.Slots.WaitAsync(0, context.RequestAborted).ConfigureAwait(false))
        {
            context.Response.StatusCode = 429;
            return;
        }

        try
        {
            AuthenticateResult authentication = await context.AuthenticateAsync(Scheme).ConfigureAwait(false);
            TimeSpan remaining = authentication.Properties!.ExpiresUtc!.Value - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                context.Response.StatusCode = 401;
                return;
            }

            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, context.RequestServices.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
            lifetime.CancelAfter(remaining < TimeSpan.FromMinutes(2) ? remaining : TimeSpan.FromMinutes(2));
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers["X-Accel-Buffering"] = "no";
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            DiagnosticUiSession[]? previous = null;
            do
            {
                DiagnosticUiSession[] current = sessions.UiSessions();
                if (previous == null || !current.SequenceEqual(previous))
                {
                    Guid? active = current.Any(item => item.SessionId == selectedId) ? selectedId : null;
                    DiagnosticEvent[] events = active.HasValue ? sessions.UiTelemetry(active.Value) : [];
                    var state = new DiagnosticUiState(current, active, events);
                    await context.Response.WriteAsync("event: state\ndata: ", lifetime.Token).ConfigureAwait(false);
                    await JsonSerializer.SerializeAsync(context.Response.Body, state, DiagnosticUiJsonContext.Protocol.DiagnosticUiState, lifetime.Token).ConfigureAwait(false);
                    await context.Response.WriteAsync("\n\n", lifetime.Token).ConfigureAwait(false);
                    previous = current;
                }
                else
                {
                    await context.Response.WriteAsync(": heartbeat\n\n", lifetime.Token).ConfigureAwait(false);
                }

                await context.Response.Body.FlushAsync(lifetime.Token).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(lifetime.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Socket cancellation, cookie expiry and shutdown all end this bounded stream.
        }
        finally
        {
            streams.Slots.Release();
        }
    }
}
