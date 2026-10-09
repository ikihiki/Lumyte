using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Lumyte.Diagnostics.Server;
using Lumyte.Diagnostics.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Diagnostics.IntegrationTests;

/// <summary>Checks the server-hosted browser boundary over real Kestrel sockets.</summary>
public sealed class BrowserUiTests
{
    /// <summary>Checks embedded assets, CSP, cookie isolation, CSRF and logout.</summary>
    /// <returns>The integration test.</returns>
    [Fact]
    public async Task BrowserAuthenticationAsync()
    {
        await using BrowserHost host = await BrowserHost.StartAsync();
        using HttpClient browser = host.Client();
        using (HttpResponseMessage html = await browser.GetAsync("/"))
        {
            Assert.Equal(HttpStatusCode.OK, html.StatusCode);
            Assert.Contains("Lumyte Diagnostics", await html.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Contains("script-src 'self'", Assert.Single(html.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
            Assert.DoesNotContain(host.OperatorToken, await html.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        foreach (string path in new[] { "/assets/diagnostics.css", "/_framework/blazor.web.js" })
        {
            using HttpResponseMessage asset = await browser.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        }

        using (HttpResponseMessage unknown = await browser.GetAsync("/assets/missing.js"))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }

        using (HttpResponseMessage denied = await browser.PostAsync("/_blazor/negotiate?negotiateVersion=1", null))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        }

        string anonymousToken = await BootstrapAsync(browser, authenticated: false);
        using (var body = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = host.OperatorToken }))
        using (HttpResponseMessage denied = await browser.PostAsync("/api/ui/login", body))
        {
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        }

        browser.DefaultRequestHeaders.Add("X-Diagnostics-CSRF", anonymousToken);
        using (var body = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = host.GameToken }))
        using (HttpResponseMessage denied = await browser.PostAsync("/api/ui/login", body))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        }

        string stolenCookie = string.Empty;
        string csrf = await LoginAsync(browser, host.OperatorToken, cookie => stolenCookie = cookie);
        browser.DefaultRequestHeaders.Remove("X-Diagnostics-CSRF");
        foreach (string path in new[] { "/resources", $"/games/{Guid.NewGuid()}/animation" })
        {
            using HttpResponseMessage page = await browser.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.DoesNotContain("id=\"login-form\"", await page.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        using (HttpResponseMessage bearerDenied = await browser.GetAsync("/diagnostics/v1/sessions"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, bearerDenied.StatusCode);
        }

        using (HttpResponseMessage denied = await browser.PostAsync("/api/ui/logout", null))
        {
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        }

        browser.DefaultRequestHeaders.Add("Origin", "https://diagnostics.example");
        using (HttpResponseMessage crossOrigin = await browser.PostAsync("/_blazor/negotiate?negotiateVersion=1", null))
        {
            Assert.Equal(HttpStatusCode.Forbidden, crossOrigin.StatusCode);
            Assert.False(crossOrigin.Headers.Contains("Access-Control-Allow-Origin"));
        }

        browser.DefaultRequestHeaders.Remove("Origin");
        browser.DefaultRequestHeaders.Add("X-Diagnostics-CSRF", csrf);
        using (HttpResponseMessage loggedOut = await browser.PostAsync("/api/ui/logout", null))
        {
            Assert.Equal(HttpStatusCode.NoContent, loggedOut.StatusCode);
        }

        await BootstrapAsync(browser, authenticated: false);
        using HttpResponseMessage deniedAfterLogout = await browser.PostAsync("/_blazor/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, deniedAfterLogout.StatusCode);
        using HttpClient replay = host.Client();
        replay.DefaultRequestHeaders.Add("Cookie", stolenCookie);
        using HttpResponseMessage revokedCookie = await replay.PostAsync("/_blazor/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedCookie.StatusCode);
    }

    /// <summary>Checks browser control and correlated telemetry for both game transports.</summary>
    /// <param name="magicOnion">Whether the game uses StreamingHub.</param>
    /// <returns>The integration test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrowserControlAsync(bool magicOnion)
    {
        await using BrowserHost host = await BrowserHost.StartAsync();
        using HttpClient browser = host.Client();
        string csrf = await LoginAsync(browser, host.OperatorToken);
        await using var game = new RemoteGame(magicOnion ? host.Grpc : host.Http, host.GameToken, magicOnion);
        Guid id = await game.ReadyAsync();
        using IServiceScope scope = host.Services.CreateScope();
        DiagnosticDashboardSession dashboard = scope.ServiceProvider.GetRequiredService<DiagnosticDashboardSession>();
        await dashboard.OnConnectionUpAsync(null!, CancellationToken.None);
        Assert.Throws<UnauthorizedAccessException>(() => dashboard.Attach(new ClaimsPrincipal(new ClaimsIdentity())));
        dashboard.Attach(host.Services.GetRequiredService<DiagnosticBrowserTickets>().Create());
        SessionSnapshot resource = Assert.Single(dashboard.Resources());
        Assert.Contains(resource.Catalog.SelectMany(item => item.Operations), item => item.Id == "override-button");
        var invocation = new OperationInvocation(Guid.NewGuid(), "input", "override-button", new()
        {
            ["button"] = DiagnosticValue.From("Jump"),
            ["pressed"] = DiagnosticValue.From(true),
            ["duration-ms"] = DiagnosticValue.From(5000L),
        });
        DiagnosticOperationResult result = await dashboard.InvokeAsync(id, invocation, CancellationToken.None);
        Assert.Equal("success", result.Status);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            long version = dashboard.Version;
            DiagnosticEvent[] events = dashboard.Events(id);
            if (events.Length >= 3)
            {
                Assert.Equal(9007199254740993L, Assert.Single(events, item => item.Kind == "metric").Value.Int64);
                Assert.Equal(Assert.Single(events, item => item.Kind == "log").TraceId, Assert.Single(events, item => item.Kind == "span").TraceId);
                break;
            }

            await dashboard.WaitForChangeAsync(version, timeout.Token);
        }

        dashboard.Close(id);
        await game.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(game.Pressed);
    }

    /// <summary>Checks that browser APIs ignore bearer credentials and cross-origin configuration.</summary>
    /// <returns>The integration test.</returns>
    [Fact]
    public async Task BrowserBoundaryAsync()
    {
        await using BrowserHost host = await BrowserHost.StartAsync();
        using HttpClient browser = host.Client();
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.OperatorToken);
        using (HttpResponseMessage denied = await browser.PostAsync("/_blazor/negotiate?negotiateVersion=1", null))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        }

        browser.DefaultRequestHeaders.Add("Origin", "https://diagnostics.example");
        using HttpResponseMessage response = await browser.GetAsync("/api/ui/bootstrap");
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Checks bounded reads, target isolation and the global Circuit capacity.</summary>
    /// <returns>The integration test.</returns>
    [Fact]
    public async Task BoundedCircuitsAsync()
    {
        await using BrowserHost host = await BrowserHost.StartAsync();
        DiagnosticSessionRegistry registry = host.Registry;
        var catalog = new DiagnosticSubsystemCatalog(new("sample", "Sample", 1), []);
        SessionWelcome target = registry.Open(new(Guid.NewGuid(), 1, [catalog]));
        SessionWelcome other = registry.Open(new(Guid.NewGuid(), 1, [catalog]));
        for (int batch = 0; batch < 2; batch++)
        {
            DiagnosticEvent[] events = Enumerable.Range(batch * 128, 128).Select(index => new DiagnosticEvent("metric", index, "event-" + index, DiagnosticValue.From((long)index), null, null, null, 0, new Dictionary<string, DiagnosticValue>())).ToArray();
            registry.Publish(target.SessionId, new(Guid.NewGuid(), target.SessionId, DiagnosticMessageKind.Telemetry, null, null, events));
        }

        registry.Publish(other.SessionId, new(Guid.NewGuid(), other.SessionId, DiagnosticMessageKind.Telemetry, null, null, [new("log", 0, "other-game", DiagnosticValue.From("secret"), null, null, null, 0, new Dictionary<string, DiagnosticValue>())]));
        var scopes = new List<IServiceScope>();
        try
        {
            for (int index = 0; index < 16; index++)
            {
                IServiceScope scope = host.Services.CreateScope();
                scopes.Add(scope);
                await scope.ServiceProvider.GetRequiredService<DiagnosticDashboardSession>().OnCircuitOpenedAsync(null!, CancellationToken.None);
            }

            using IServiceScope rejected = host.Services.CreateScope();
            await Assert.ThrowsAsync<InvalidOperationException>(() => rejected.ServiceProvider.GetRequiredService<DiagnosticDashboardSession>().OnCircuitOpenedAsync(null!, CancellationToken.None));
            DiagnosticDashboardSession dashboard = scopes[0].ServiceProvider.GetRequiredService<DiagnosticDashboardSession>();
            await dashboard.OnConnectionUpAsync(null!, CancellationToken.None);
            dashboard.Attach(host.Services.GetRequiredService<DiagnosticBrowserTickets>().Create());
            DiagnosticEvent[] tail = dashboard.Events(target.SessionId);
            Assert.Equal(200, tail.Length);
            Assert.Equal("event-56", tail[0].Name);
            Assert.Equal("event-255", tail[^1].Name);
            Assert.DoesNotContain(tail, item => item.Name == "other-game");
            await dashboard.OnConnectionDownAsync(null!, CancellationToken.None);
            Assert.Throws<InvalidOperationException>(() => dashboard.Events(target.SessionId));
        }
        finally
        {
            foreach (IServiceScope scope in scopes)
            {
                scope.Dispose();
            }
        }

        using IServiceScope released = host.Services.CreateScope();
        await released.ServiceProvider.GetRequiredService<DiagnosticDashboardSession>().OnCircuitOpenedAsync(null!, CancellationToken.None);
    }

    /// <summary>Checks that repeated login attempts have a finite budget.</summary>
    /// <returns>The integration test.</returns>
    [Fact]
    public async Task LoginAttemptsAreBoundedAsync()
    {
        await using BrowserHost host = await BrowserHost.StartAsync();
        using HttpClient browser = host.Client();
        string csrf = await BootstrapAsync(browser, authenticated: false);
        browser.DefaultRequestHeaders.Add("X-Diagnostics-CSRF", csrf);
        for (int index = 0; index < 11; index++)
        {
            using var body = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = "invalid" });
            using HttpResponseMessage response = await browser.PostAsync("/api/ui/login", body);
            Assert.Equal(index < 10 ? HttpStatusCode.Unauthorized : HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    /// <summary>Checks subscription cancellation, reconnect, logout and target removal.</summary>
    /// <returns>The integration test.</returns>
    [Fact]
    public async Task CircuitLifetimeAsync()
    {
        await using BrowserHost host = await BrowserHost.StartAsync();
        using IServiceScope scope = host.Services.CreateScope();
        DiagnosticDashboardSession dashboard = scope.ServiceProvider.GetRequiredService<DiagnosticDashboardSession>();
        DiagnosticBrowserTickets tickets = host.Services.GetRequiredService<DiagnosticBrowserTickets>();
        ClaimsPrincipal user = tickets.Create();
        dashboard.Attach(user);
        await dashboard.OnConnectionUpAsync(null!, CancellationToken.None);
        SessionWelcome target = host.Registry.Open(new(Guid.NewGuid(), 1, [new(new("sample", "Sample", 1), [])]));
        long version = dashboard.Version;
        Task changed = dashboard.WaitForChangeAsync(version, CancellationToken.None);
        host.Registry.Publish(target.SessionId, new(Guid.NewGuid(), target.SessionId, DiagnosticMessageKind.Telemetry, null, null, [new("metric", 1, "later", DiagnosticValue.From(42L), null, null, null, 0, new Dictionary<string, DiagnosticValue>())]));
        await changed.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("later", Assert.Single(dashboard.Events(target.SessionId)).Name);
        Task disconnect = dashboard.WaitForChangeAsync(dashboard.Version, CancellationToken.None);
        await dashboard.OnConnectionDownAsync(null!, CancellationToken.None);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => disconnect);
        await dashboard.OnConnectionUpAsync(null!, CancellationToken.None);
        Assert.Equal("later", Assert.Single(dashboard.Events(target.SessionId)).Name);
        host.Registry.Close(target.SessionId);
        Assert.Empty(dashboard.Resources());
        Task logout = dashboard.WaitForChangeAsync(dashboard.Version, CancellationToken.None);
        tickets.Revoke(user);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => logout);
        Assert.Throws<UnauthorizedAccessException>(() => dashboard.Resources());
        Assert.Throws<UnauthorizedAccessException>(() => dashboard.Close(target.SessionId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => dashboard.InvokeAsync(target.SessionId, new(Guid.NewGuid(), "sample", "any", new()), CancellationToken.None));
    }

    private static async Task<string> BootstrapAsync(HttpClient client, bool authenticated)
    {
        using HttpResponseMessage response = await client.GetAsync("/api/ui/bootstrap");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var state = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(authenticated, state.RootElement.GetProperty("authenticated").GetBoolean());
        return state.RootElement.GetProperty("requestToken").GetString()!;
    }

    private static async Task<string> LoginAsync(HttpClient client, string token, Action<string>? captureCookie = null)
    {
        string csrf = await BootstrapAsync(client, authenticated: false);
        client.DefaultRequestHeaders.Remove("X-Diagnostics-CSRF");
        client.DefaultRequestHeaders.Add("X-Diagnostics-CSRF", csrf);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token });
        using HttpResponseMessage response = await client.PostAsync("/api/ui/login", content);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        string cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("Lumyte.Diagnostics.Operator=", StringComparison.Ordinal));
        captureCookie?.Invoke(cookie.Split(';')[0]);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        csrf = await BootstrapAsync(client, authenticated: true);
        client.DefaultRequestHeaders.Remove("X-Diagnostics-CSRF");
        client.DefaultRequestHeaders.Add("X-Diagnostics-CSRF", csrf);
        return csrf;
    }

    private sealed class BrowserHost(WebApplication app, Uri http, Uri grpc, string gameToken, string operatorToken) : IAsyncDisposable
    {
        public Uri Http { get; } = http;

        public Uri Grpc { get; } = grpc;

        public string GameToken { get; } = gameToken;

        public string OperatorToken { get; } = operatorToken;

        public IServiceProvider Services => app.Services;

        public DiagnosticSessionRegistry Registry => app.Services.GetRequiredService<DiagnosticSessionRegistry>();

        public static async Task<BrowserHost> StartAsync()
        {
            string gameToken = Guid.NewGuid().ToString("N");
            string operatorToken = Guid.NewGuid().ToString("N");
            WebApplication app = DiagnosticServerApplication.Create([], options =>
            {
                options.HttpPort = 0;
                options.MagicOnionPort = 0;
                options.GameToken = gameToken;
                options.OperatorToken = operatorToken;
                options.AllowedOrigins = ["https://diagnostics.example"];
            });
            await app.StartAsync();
            Uri[] endpoints = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Select(address => new Uri(address)).ToArray();
            return new(app, endpoints[0], endpoints[1], gameToken, operatorToken);
        }

        public HttpClient Client() => new() { BaseAddress = Http, Timeout = TimeSpan.FromSeconds(15) };

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
