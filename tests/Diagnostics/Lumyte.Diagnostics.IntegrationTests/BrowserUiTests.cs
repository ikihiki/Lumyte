using System.Net;
using System.Net.Http.Headers;
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

        foreach (string path in new[] { "/assets/diagnostics.css", "/assets/diagnostics.js", "/assets/telemetry-model.js" })
        {
            using HttpResponseMessage asset = await browser.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        }

        using (HttpResponseMessage unknown = await browser.GetAsync("/assets/missing.js"))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }

        using (HttpResponseMessage denied = await browser.GetAsync("/api/ui/sessions"))
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

        string csrf = await LoginAsync(browser, host.OperatorToken);
        browser.DefaultRequestHeaders.Remove("X-Diagnostics-CSRF");
        using (HttpResponseMessage bearerDenied = await browser.GetAsync("/diagnostics/v1/sessions"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, bearerDenied.StatusCode);
        }

        using (HttpResponseMessage denied = await browser.PostAsync("/api/ui/logout", null))
        {
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        }

        browser.DefaultRequestHeaders.Add("X-Diagnostics-CSRF", csrf);
        using (HttpResponseMessage loggedOut = await browser.PostAsync("/api/ui/logout", null))
        {
            Assert.Equal(HttpStatusCode.NoContent, loggedOut.StatusCode);
        }

        await BootstrapAsync(browser, authenticated: false);
        using HttpResponseMessage deniedAfterLogout = await browser.GetAsync("/api/ui/events");
        Assert.Equal(HttpStatusCode.Unauthorized, deniedAfterLogout.StatusCode);
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
        using (HttpResponseMessage catalog = await browser.GetAsync("/api/ui/sessions"))
        {
            Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
            string json = await catalog.Content.ReadAsStringAsync();
            Assert.Contains("override-button", json, StringComparison.Ordinal);
            Assert.DoesNotContain("sessionSecret", json, StringComparison.Ordinal);
        }

        var invocation = new OperationInvocation(Guid.NewGuid(), "input", "override-button", new()
        {
            ["button"] = DiagnosticValue.From("Jump"),
            ["pressed"] = DiagnosticValue.From(true),
            ["duration-ms"] = DiagnosticValue.From(5000L),
        });
        browser.DefaultRequestHeaders.Remove("X-Diagnostics-CSRF");
        using (HttpResponseMessage denied = await InvokeAsync(browser, id, invocation))
        {
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            Assert.False(game.Pressed);
        }

        browser.DefaultRequestHeaders.Add("X-Diagnostics-CSRF", csrf);
        using (HttpResponseMessage response = await InvokeAsync(browser, id, invocation))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            DiagnosticOperationResult result = JsonSerializer.Deserialize(await response.Content.ReadAsByteArrayAsync(), DiagnosticJson.Context.DiagnosticOperationResult)!;
            Assert.Equal("success", result.Status);
        }

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using (HttpResponseMessage response = await browser.GetAsync($"/api/ui/events?sessionId={id}", HttpCompletionOption.ResponseHeadersRead, cancellation.Token))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellation.Token));
            while (true)
            {
                string line = (await reader.ReadLineAsync(cancellation.Token))!;
                if (!line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    continue;
                }

                using var state = JsonDocument.Parse(line[6..]);
                JsonElement events = state.RootElement.GetProperty("events");
                if (events.GetArrayLength() < 3)
                {
                    continue;
                }

                Assert.Equal(id, state.RootElement.GetProperty("selectedSessionId").GetGuid());
                JsonElement metric = events.EnumerateArray().Single(item => item.GetProperty("kind").GetString() == "metric");
                Assert.Equal("9007199254740993", metric.GetProperty("value").GetProperty("int64").GetString());
                JsonElement log = events.EnumerateArray().Single(item => item.GetProperty("kind").GetString() == "log");
                JsonElement trace = events.EnumerateArray().Single(item => item.GetProperty("kind").GetString() == "span");
                Assert.Equal(log.GetProperty("traceId").GetString(), trace.GetProperty("traceId").GetString());
                break;
            }
        }

        browser.DefaultRequestHeaders.Remove("X-Diagnostics-CSRF");
        using (HttpResponseMessage denied = await browser.DeleteAsync($"/api/ui/sessions/{id}/connection"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        }

        browser.DefaultRequestHeaders.Add("X-Diagnostics-CSRF", csrf);
        using HttpResponseMessage closed = await browser.DeleteAsync($"/api/ui/sessions/{id}/connection");
        Assert.Equal(HttpStatusCode.NoContent, closed.StatusCode);
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
        using (HttpResponseMessage denied = await browser.GetAsync("/api/ui/sessions"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        }

        browser.DefaultRequestHeaders.Add("Origin", "https://diagnostics.example");
        using HttpResponseMessage response = await browser.GetAsync("/api/ui/bootstrap");
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Checks tail bounds, target isolation and the global stream capacity.</summary>
    /// <returns>The integration test.</returns>
    [Fact]
    public async Task BoundedStreamsAsync()
    {
        await using BrowserHost host = await BrowserHost.StartAsync();
        using HttpClient browser = host.Client();
        await LoginAsync(browser, host.OperatorToken);
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
        var responses = new List<HttpResponseMessage>();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            for (int index = 0; index < 16; index++)
            {
                HttpResponseMessage response = await browser.GetAsync($"/api/ui/events?sessionId={target.SessionId}", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                responses.Add(response);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            using HttpResponseMessage rejected = await browser.GetAsync("/api/ui/events", timeout.Token);
            Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
            using var reader = new StreamReader(await responses[0].Content.ReadAsStreamAsync(timeout.Token));
            Assert.Equal("event: state", await reader.ReadLineAsync(timeout.Token));
            string line = (await reader.ReadLineAsync(timeout.Token))!;
            using var state = JsonDocument.Parse(line[6..]);
            JsonElement events = state.RootElement.GetProperty("events");
            Assert.Equal(200, events.GetArrayLength());
            Assert.Equal("event-56", events[0].GetProperty("name").GetString());
            Assert.Equal("event-255", events[199].GetProperty("name").GetString());
            Assert.DoesNotContain("other-game", line, StringComparison.Ordinal);
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }
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

    /// <summary>Checks notification subscriptions observe later data and target removal.</summary>
    /// <returns>The integration test.</returns>
    [Fact]
    public async Task StreamObservesLaterChangesAsync()
    {
        await using BrowserHost host = await BrowserHost.StartAsync();
        using HttpClient browser = host.Client();
        await LoginAsync(browser, host.OperatorToken);
        DiagnosticSessionRegistry registry = host.Registry;
        SessionWelcome target = registry.Open(new(Guid.NewGuid(), 1, [new(new("sample", "Sample", 1), [])]));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using HttpResponseMessage response = await browser.GetAsync($"/api/ui/events?sessionId={target.SessionId}", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token));
        using (JsonDocument initial = await ReadStateAsync(reader, timeout.Token))
        {
            Assert.Equal(0, initial.RootElement.GetProperty("events").GetArrayLength());
        }

        registry.Publish(target.SessionId, new(Guid.NewGuid(), target.SessionId, DiagnosticMessageKind.Telemetry, null, null, [new("metric", 1, "later", DiagnosticValue.From(42L), null, null, null, 0, new Dictionary<string, DiagnosticValue>())]));
        using (JsonDocument updated = await ReadStateAsync(reader, timeout.Token))
        {
            Assert.Equal("later", updated.RootElement.GetProperty("events")[0].GetProperty("name").GetString());
        }

        registry.Close(target.SessionId);
        using JsonDocument closed = await ReadStateAsync(reader, timeout.Token);
        Assert.Equal(JsonValueKind.Null, closed.RootElement.GetProperty("selectedSessionId").ValueKind);
        Assert.Equal(0, closed.RootElement.GetProperty("sessions").GetArrayLength());
    }

    private static async Task<JsonDocument> ReadStateAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        while (true)
        {
            string line = (await reader.ReadLineAsync(cancellationToken))!;
            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                return JsonDocument.Parse(line[6..]);
            }
        }
    }

    private static async Task<string> BootstrapAsync(HttpClient client, bool authenticated)
    {
        using HttpResponseMessage response = await client.GetAsync("/api/ui/bootstrap");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var state = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(authenticated, state.RootElement.GetProperty("authenticated").GetBoolean());
        return state.RootElement.GetProperty("requestToken").GetString()!;
    }

    private static async Task<string> LoginAsync(HttpClient client, string token)
    {
        string csrf = await BootstrapAsync(client, authenticated: false);
        client.DefaultRequestHeaders.Remove("X-Diagnostics-CSRF");
        client.DefaultRequestHeaders.Add("X-Diagnostics-CSRF", csrf);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token });
        using HttpResponseMessage response = await client.PostAsync("/api/ui/login", content);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        string cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("Lumyte.Diagnostics.Operator=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/ui", cookie, StringComparison.OrdinalIgnoreCase);
        csrf = await BootstrapAsync(client, authenticated: true);
        client.DefaultRequestHeaders.Remove("X-Diagnostics-CSRF");
        client.DefaultRequestHeaders.Add("X-Diagnostics-CSRF", csrf);
        return csrf;
    }

    private static async Task<HttpResponseMessage> InvokeAsync(HttpClient client, Guid id, OperationInvocation invocation)
    {
        using var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(invocation, DiagnosticJson.Context.OperationInvocation));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await client.PostAsync($"/api/ui/sessions/{id}/operations", content);
    }

    private sealed class BrowserHost(WebApplication app, Uri http, Uri grpc, string gameToken, string operatorToken) : IAsyncDisposable
    {
        public Uri Http { get; } = http;

        public Uri Grpc { get; } = grpc;

        public string GameToken { get; } = gameToken;

        public string OperatorToken { get; } = operatorToken;

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
