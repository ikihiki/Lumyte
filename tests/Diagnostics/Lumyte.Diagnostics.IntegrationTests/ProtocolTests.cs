using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Lumyte.Diagnostics.Server;
using Lumyte.Diagnostics.Transport;
using Lumyte.Diagnostics.Transport.Http;
using Lumyte.Diagnostics.Transport.MagicOnion;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Diagnostics.IntegrationTests;

/// <summary>Tests protocol rejection, redelivery safety and authentication over sockets.</summary>
public sealed class ProtocolTests
{
    /// <summary>Checks invalid enrollment, deadlines, publication deduplication and session credentials.</summary>
    /// <param name="magicOnion">Whether to use the StreamingHub transport.</param>
    /// <returns>The integration test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticationAndDeadlinesAsync(bool magicOnion)
    {
        string gameToken = Guid.NewGuid().ToString("N");
        string operatorToken = Guid.NewGuid().ToString("N");
        await using WebApplication server = DiagnosticServerApplication.Create([], options =>
        {
            options.HttpPort = 0;
            options.MagicOnionPort = 0;
            options.GameToken = gameToken;
            options.OperatorToken = operatorToken;
            options.AllowedOrigins = ["http://localhost:8080"];
        });
        await server.StartAsync();
        Uri[] endpoints = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Select(address => new Uri(address)).ToArray();
        var hello = new ClientHello(Guid.NewGuid(), 1, [new(new("input", "Input", 1), [new("noop", "No-op", DiagnosticPermission.Observe, [], [])])]);
        using ServiceProvider wrong = Factory(endpoints[magicOnion ? 1 : 0], "wrong-token", magicOnion);
        await Assert.ThrowsAsync<DiagnosticTransportException>(async () => await wrong.GetRequiredService<IDiagnosticTransportFactory>().OpenAsync(hello, default));
        using ServiceProvider services = Factory(endpoints[magicOnion ? 1 : 0], gameToken, magicOnion);
        IDiagnosticTransportFactory factory = services.GetRequiredService<IDiagnosticTransportFactory>();
        await Assert.ThrowsAsync<DiagnosticTransportException>(async () => await factory.OpenAsync(hello with { ProtocolVersion = 999 }, default));
        await using IDiagnosticConnection connection = await factory.OpenAsync(hello, default);
        using var client = new HttpClient { BaseAddress = endpoints[0], Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", operatorToken);
        using (var preflight = new HttpRequestMessage(HttpMethod.Options, "diagnostics/v1/sessions"))
        {
            preflight.Headers.Add("Origin", "http://localhost:8080");
            preflight.Headers.Add("Access-Control-Request-Method", "POST");
            preflight.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
            using HttpResponseMessage response = await client.SendAsync(preflight);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal("http://localhost:8080", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        }

        var invocation = new OperationInvocation(Guid.NewGuid(), "input", "noop", [], TimeoutMilliseconds: 100);
        using (var body = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(invocation, DiagnosticJson.Context.OperationInvocation)))
        {
            body.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using HttpResponseMessage response = await client.PostAsync($"diagnostics/v1/sessions/{connection.Welcome.SessionId}/operations", body);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            DiagnosticOperationResult result = JsonSerializer.Deserialize(await response.Content.ReadAsByteArrayAsync(), DiagnosticJson.Context.DiagnosticOperationResult)!;
            Assert.Equal("expired", result.Code);
        }

        var heartbeat = new DiagnosticMessage(Guid.NewGuid(), connection.Welcome.SessionId, DiagnosticMessageKind.Heartbeat, null, null, []);
        Assert.True((await connection.PublishAsync(heartbeat, default)).Accepted);
        Assert.True((await connection.PublishAsync(heartbeat, default)).Accepted);
        var metric = new DiagnosticEvent("metric", 9007199254740993, "test", DiagnosticValue.From(9007199254740993L), null, null, null, 0, new Dictionary<string, DiagnosticValue>());
        Assert.Equal("message-id-conflict", (await connection.PublishAsync(heartbeat with { Kind = DiagnosticMessageKind.Telemetry, Events = [metric] }, default)).ErrorCode);
        Assert.True((await connection.PublishAsync(heartbeat with { MessageId = Guid.NewGuid(), Kind = DiagnosticMessageKind.Telemetry, Events = [metric] }, default)).Accepted);
        using var otherGame = new HttpClient { BaseAddress = endpoints[0] };
        otherGame.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", gameToken);
        otherGame.DefaultRequestHeaders.Add("X-Diagnostics-Session", "wrong-session-secret");
        using (HttpResponseMessage denied = await otherGame.GetAsync($"diagnostics/v1/sessions/{connection.Welcome.SessionId}/commands"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        using HttpResponseMessage snapshot = await client.GetAsync("diagnostics/v1/sessions");
        string json = await snapshot.Content.ReadAsStringAsync();
        Assert.DoesNotContain(connection.Welcome.SessionSecret, json, StringComparison.Ordinal);
        SessionSnapshot session = Assert.Single(JsonSerializer.Deserialize(json, DiagnosticJson.Context.SessionSnapshotArray)!);
        Assert.Equal(0, session.PendingCommands);
        Assert.Equal(1, session.TelemetryReceived);
    }

    /// <summary>Checks unsafe remote endpoints are rejected before credentials are transmitted.</summary>
    [Fact]
    public void RemoteCleartextIsRejected()
    {
        Assert.Throws<ArgumentException>(() => DiagnosticEndpoint.Validate(new Uri("http://example.com")));
        Assert.Throws<ArgumentException>(() => DiagnosticEndpoint.Validate(new Uri("https://user:password@example.com")));
        DiagnosticEndpoint.Validate(new Uri("http://127.0.0.1:5000"));
        DiagnosticEndpoint.Validate(new Uri("https://example.com"));
    }

    private static ServiceProvider Factory(Uri endpoint, string token, bool magicOnion)
    {
        var services = new ServiceCollection();
        if (magicOnion)
        {
            services.AddMagicOnionDiagnosticTransport(options =>
            {
                options.Endpoint = endpoint;
                options.GameToken = token;
            });
        }
        else
        {
            services.AddHttpDiagnosticTransport(options =>
            {
                options.BaseAddress = endpoint;
                options.GameToken = token;
            });
        }

        return services.BuildServiceProvider();
    }
}
