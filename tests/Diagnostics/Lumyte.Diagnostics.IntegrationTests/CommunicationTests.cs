using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Lumyte.Diagnostics.Server;
using Lumyte.Diagnostics.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Diagnostics.IntegrationTests;

/// <summary>Exercises Kestrel sockets, native MessagePack StreamingHub and JSON HTTP.</summary>
public sealed class CommunicationTests
{
    /// <summary>Checks enrollment, catalog discovery, control, telemetry, isolation and remote disconnect.</summary>
    /// <param name="magicOnion">Whether to use the StreamingHub transport.</param>
    /// <returns>The integration test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemoteRoundTripAsync(bool magicOnion)
    {
        string gameToken = Guid.NewGuid().ToString("N");
        string operatorToken = Guid.NewGuid().ToString("N");
        await using WebApplication server = DiagnosticServerApplication.Create([], options =>
        {
            options.HttpPort = 0;
            options.MagicOnionPort = 0;
            options.GameToken = gameToken;
            options.OperatorToken = operatorToken;
        });
        await server.StartAsync();
        Uri[] endpoints = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Select(address => new Uri(address)).ToArray();
        Assert.Equal(2, endpoints.Length);
        using var client = new HttpClient { BaseAddress = endpoints[0], Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", operatorToken);
        using var unauthenticated = new HttpClient { BaseAddress = endpoints[0] };
        using (HttpResponseMessage denied = await unauthenticated.GetAsync("diagnostics/v1/sessions"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        }

        unauthenticated.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", gameToken);
        using (HttpResponseMessage denied = await unauthenticated.GetAsync("diagnostics/v1/sessions"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        Uri address = endpoints[magicOnion ? 1 : 0];
        await using var first = new RemoteGame(address, gameToken, magicOnion);
        await using var second = new RemoteGame(address, gameToken, magicOnion);
        Guid firstId = await first.ReadyAsync();
        Guid secondId = await second.ReadyAsync();
        SessionSnapshot[] sessions = await GetAsync(client, "diagnostics/v1/sessions", DiagnosticJson.Context.SessionSnapshotArray);
        Assert.Equal(2, sessions.Length);
        Assert.NotEqual(firstId, secondId);
        Assert.NotEqual(sessions[0].InstanceId, sessions[1].InstanceId);
        Assert.All(sessions, session => Assert.Equal("override-button", Assert.Single(Assert.Single(session.Catalog).Operations).Id));
        var invocation = new OperationInvocation(Guid.NewGuid(), "input", "override-button", new()
        {
            ["button"] = DiagnosticValue.From("Jump"),
            ["pressed"] = DiagnosticValue.From(true),
            ["duration-ms"] = DiagnosticValue.From(5000L),
        });
        DiagnosticOperationResult result = await InvokeAsync(client, firstId, invocation);
        Assert.Equal("success", result.Status);
        Assert.True(Guid.TryParse(result.Values!["lease-id"].String, out _));
        await WaitAsync(() => first.Pressed);
        Assert.False(second.Pressed);
        invocation = invocation with { Arguments = invocation.Arguments.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value) };
        DiagnosticOperationResult duplicate = await InvokeAsync(client, firstId, invocation);
        Assert.Equal(result.Values["lease-id"], duplicate.Values!["lease-id"]);
        invocation.Arguments["pressed"] = DiagnosticValue.From(false);
        Assert.Equal("request-id-conflict", (await InvokeAsync(client, firstId, invocation)).Code);
        Assert.True(first.Pressed);
        invocation = invocation with { RequestId = Guid.NewGuid() };
        invocation.Arguments["duration-ms"] = DiagnosticValue.From(6000L);
        Assert.Equal("invalid-input", (await InvokeAsync(client, firstId, invocation)).Code);
        invocation = invocation with { RequestId = Guid.NewGuid(), OperationId = "unknown" };
        Assert.Equal("not-found", (await InvokeAsync(client, firstId, invocation)).Code);
        DiagnosticEvent[] events = [];
        for (int attempt = 0; attempt < 100; attempt++)
        {
            events = await GetAsync(client, $"diagnostics/v1/sessions/{firstId}/telemetry", DiagnosticJson.Context.DiagnosticEventArray);
            if (events.Length >= 3)
            {
                break;
            }

            await Task.Delay(20);
        }

        Assert.Equal(3, events.Length);
        Assert.Equal(9007199254740993L, Assert.Single(events, item => item.Kind == "metric").Value.Int64);
        DiagnosticEvent log = Assert.Single(events, item => item.Kind == "log");
        Assert.Equal(9007199254740993L, log.Fields["Count"].Int64);
        Assert.Equal(log.TraceId, Assert.Single(events, item => item.Kind == "span").TraceId);
        Guid firstInstance = Assert.Single(sessions, session => session.SessionId == firstId).InstanceId;
        Assert.All(events, item => Assert.Equal(firstInstance.ToString("D"), item.Fields["lumyte.instance.id"].String));
        using (HttpResponseMessage closed = await client.DeleteAsync($"diagnostics/v1/sessions/{firstId}/connection"))
        {
            Assert.Equal(HttpStatusCode.NoContent, closed.StatusCode);
        }

        await first.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(first.Pressed);
        sessions = await GetAsync(client, "diagnostics/v1/sessions", DiagnosticJson.Context.SessionSnapshotArray);
        Assert.Equal(secondId, Assert.Single(sessions).SessionId);
    }

    private static async Task<DiagnosticOperationResult> InvokeAsync(HttpClient client, Guid sessionId, OperationInvocation invocation)
    {
        using var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(invocation, DiagnosticJson.Context.OperationInvocation));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using HttpResponseMessage response = await client.PostAsync($"diagnostics/v1/sessions/{sessionId}/operations", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonSerializer.Deserialize(await response.Content.ReadAsByteArrayAsync(), DiagnosticJson.Context.DiagnosticOperationResult)!;
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string path, JsonTypeInfo<T> type)
    {
        using HttpResponseMessage response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonSerializer.Deserialize(await response.Content.ReadAsByteArrayAsync(), type)!;
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(5);
        }

        Assert.True(condition());
    }
}
