using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Lumyte.Diagnostics.Transport;
using Lumyte.Diagnostics.Transport.MagicOnion;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Lumyte.Diagnostics.Server;

/// <summary>Builds a runnable loopback diagnostic server with HTTP and StreamingHub endpoints.</summary>
public static class DiagnosticServerApplication
{
    /// <summary>Creates an application with explicit enrollment and operator credentials.</summary>
    /// <param name="args">The host arguments.</param>
    /// <param name="configure">The credentials, capabilities and listener ports.</param>
    /// <returns>The application; callers start and dispose it.</returns>
    public static WebApplication Create(string[] args, Action<DiagnosticServerOptions> configure)
    {
        var options = new DiagnosticServerOptions();
        configure(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.GameToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.OperatorToken);
        if (options.GameToken == options.OperatorToken || options.HttpPort is < 0 or > 65535 || options.MagicOnionPort is < 0 or > 65535
            || options.GamePermissions.Any(permission => !Enum.IsDefined(permission)) || options.OperatorPermissions.Any(permission => !Enum.IsDefined(permission)))
        {
            throw new ArgumentException("Invalid server configuration.", nameof(configure));
        }

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ApplicationName = typeof(DiagnosticHub).Assembly.FullName });
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.Limits.MaxRequestBodySize = 4 * 1024 * 1024;
            server.Listen(IPAddress.Loopback, options.HttpPort, listen => listen.Protocols = HttpProtocols.Http1);
            server.Listen(IPAddress.Loopback, options.MagicOnionPort, listen => listen.Protocols = HttpProtocols.Http2);
        });
        builder.Services.AddCors(cors => cors.AddPolicy("Diagnostics", policy =>
        {
            if (options.AllowedOrigins.Length > 0)
            {
                policy.WithOrigins(options.AllowedOrigins).WithHeaders("Authorization", "Content-Type", "X-Diagnostics-Session").WithMethods("GET", "POST", "DELETE");
            }
        }));
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<DiagnosticSessionRegistry>();
        builder.Services.AddHostedService<SessionExpiryService>();
        builder.Services.AddAntiforgery(antiforgery =>
        {
            antiforgery.HeaderName = "X-Diagnostics-CSRF";
            antiforgery.Cookie.Name = "Lumyte.Diagnostics.Csrf";
            antiforgery.Cookie.Path = "/api/ui";
            antiforgery.Cookie.SameSite = SameSiteMode.Strict;
        });
        builder.Services.AddSingleton<DiagnosticUiStreams>();
        builder.Services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = 429;
            limiter.AddFixedWindowLimiter("UiLogin", window =>
            {
                window.PermitLimit = 10;
                window.Window = TimeSpan.FromMinutes(1);
                window.QueueLimit = 0;
            });
        });
        builder.Services.AddAuthentication("Diagnostics")
            .AddScheme<AuthenticationSchemeOptions, DiagnosticAuthenticationHandler>("Diagnostics", _ => { })
            .AddCookie(DiagnosticBrowserUi.Scheme, cookie =>
            {
                cookie.Cookie.Name = "Lumyte.Diagnostics.Operator";
                cookie.Cookie.Path = "/api/ui";
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = SameSiteMode.Strict;
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                cookie.ExpireTimeSpan = TimeSpan.FromMinutes(30);
                cookie.SlidingExpiration = false;
                cookie.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = 401;
                    return Task.CompletedTask;
                };
                cookie.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = 403;
                    return Task.CompletedTask;
                };
            });
        builder.Services.AddAuthorization(authorization =>
        {
            authorization.AddPolicy(DiagnosticBrowserUi.Scheme, policy => policy.AddAuthenticationSchemes(DiagnosticBrowserUi.Scheme).RequireAuthenticatedUser().RequireClaim("diagnostics.role", "operator"));
            authorization.AddPolicy("Game", policy => policy.RequireAuthenticatedUser().RequireClaim("diagnostics.role", "game"));
            authorization.AddPolicy("Operator", policy => policy.RequireAuthenticatedUser().RequireClaim("diagnostics.role", "operator"));
        });
        builder.Services.AddGrpc(grpc =>
        {
            grpc.MaxReceiveMessageSize = 4 * 1024 * 1024;
            grpc.MaxSendMessageSize = 4 * 1024 * 1024;
        });
        builder.Services.AddMagicOnion(magic =>
        {
            magic.MessageSerializer = DiagnosticMessagePack.Provider;
            magic.IsReturnExceptionStackTraceInErrorDetail = false;
            magic.StreamingHubResponseQueueMaxLength = 256;
            magic.EnableStreamingHubHeartbeat = true;
        });
        WebApplication app = builder.Build();
        app.UseCors("Diagnostics");
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is ArgumentException or JsonException or KeyNotFoundException or UnauthorizedAccessException or InvalidOperationException)
            {
                if (context.Response.HasStarted)
                {
                    throw;
                }

                context.Response.StatusCode = exception switch
                {
                    UnauthorizedAccessException => 403,
                    KeyNotFoundException => 404,
                    InvalidOperationException => 409,
                    _ => 400,
                };
            }
        });
        app.MapMagicOnionService().RequireAuthorization("Game");
        MapHttp(app);
        DiagnosticBrowserUi.Map(app);
        return app;
    }

    private static void MapHttp(WebApplication app)
    {
        app.MapPost("/diagnostics/v1/sessions", async (HttpContext context, DiagnosticSessionRegistry sessions) =>
        {
            ClientHello hello = await ReadAsync(context, DiagnosticJson.Context.ClientHello).ConfigureAwait(false);
            await WriteAsync(context, sessions.Open(hello), DiagnosticJson.Context.SessionWelcome).ConfigureAwait(false);
        }).RequireAuthorization("Game");
        app.MapGet("/diagnostics/v1/sessions", async (HttpContext context, DiagnosticSessionRegistry sessions) =>
            await WriteAsync(context, sessions.List(), DiagnosticJson.Context.SessionSnapshotArray).ConfigureAwait(false)).RequireAuthorization("Operator");
        app.MapGet("/diagnostics/v1/sessions/{id:guid}/commands", async (Guid id, HttpContext context, DiagnosticSessionRegistry sessions) =>
        {
            sessions.Authenticate(id, context.Request.Headers["X-Diagnostics-Session"].ToString());
            DiagnosticCommand[] commands = await sessions.PollAsync(id, context.RequestAborted).ConfigureAwait(false);
            await WriteAsync(context, commands, DiagnosticJson.Context.DiagnosticCommandArray).ConfigureAwait(false);
        }).RequireAuthorization("Game");
        app.MapPost("/diagnostics/v1/sessions/{id:guid}/messages", async (Guid id, HttpContext context, DiagnosticSessionRegistry sessions) =>
        {
            sessions.Authenticate(id, context.Request.Headers["X-Diagnostics-Session"].ToString());
            DiagnosticMessage message = await ReadAsync(context, DiagnosticJson.Context.DiagnosticMessage).ConfigureAwait(false);
            await WriteAsync(context, sessions.Publish(id, message), DiagnosticJson.Context.PublishReceipt).ConfigureAwait(false);
        }).RequireAuthorization("Game");
        app.MapDelete("/diagnostics/v1/sessions/{id:guid}", (Guid id, HttpContext context, DiagnosticSessionRegistry sessions) =>
        {
            sessions.Authenticate(id, context.Request.Headers["X-Diagnostics-Session"].ToString());
            sessions.Close(id);
            return Results.NoContent();
        }).RequireAuthorization("Game");
        app.MapDelete("/diagnostics/v1/sessions/{id:guid}/connection", (Guid id, DiagnosticSessionRegistry sessions) =>
        {
            sessions.Close(id);
            return Results.NoContent();
        }).RequireAuthorization("Operator");
        app.MapPost("/diagnostics/v1/sessions/{id:guid}/operations", async (Guid id, HttpContext context, DiagnosticSessionRegistry sessions) =>
        {
            OperationInvocation invocation = await ReadAsync(context, DiagnosticJson.Context.OperationInvocation).ConfigureAwait(false);
            DiagnosticOperationResult result = await sessions.InvokeAsync(id, invocation, context.User.Identity!.Name!, context.RequestAborted).ConfigureAwait(false);
            await WriteAsync(context, result, DiagnosticJson.Context.DiagnosticOperationResult).ConfigureAwait(false);
        }).RequireAuthorization("Operator");
        app.MapGet("/diagnostics/v1/sessions/{id:guid}/telemetry", async (Guid id, HttpContext context, DiagnosticSessionRegistry sessions) =>
            await WriteAsync(context, sessions.Telemetry(id), DiagnosticJson.Context.DiagnosticEventArray).ConfigureAwait(false)).RequireAuthorization("Operator");
    }

    private static async Task<T> ReadAsync<T>(HttpContext context, JsonTypeInfo<T> type)
        => await JsonSerializer.DeserializeAsync(context.Request.Body, type, context.RequestAborted).ConfigureAwait(false) ?? throw new ArgumentException("Empty request body.");

    private static Task WriteAsync<T>(HttpContext context, T value, JsonTypeInfo<T> type)
    {
        context.Response.ContentType = "application/json";
        return JsonSerializer.SerializeAsync(context.Response.Body, value, type, context.RequestAborted);
    }
}
