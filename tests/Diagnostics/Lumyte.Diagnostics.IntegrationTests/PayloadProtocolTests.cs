using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Lumyte.Diagnostics.Server;

namespace Lumyte.Diagnostics.IntegrationTests;

/// <summary>Checks schema-sized command payloads and the unchanged telemetry bounds.</summary>
public sealed class PayloadProtocolTests
{
    /// <summary>Checks tape-sized and settings-sized fields cross both transports in both directions.</summary>
    /// <param name="magicOnion">Whether the game uses the StreamingHub transport.</param>
    /// <param name="length">The declared and transmitted string length.</param>
    /// <returns>The round-trip check.</returns>
    [Theory]
    [InlineData(false, 32768)]
    [InlineData(true, 32768)]
    [InlineData(false, 1048576)]
    [InlineData(true, 1048576)]
    public async Task DeclaredPayloadRoundTripsAsync(bool magicOnion, int length)
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
        var services = new ServiceCollection();
        if (magicOnion)
        {
            services.AddMagicOnionDiagnosticTransport(options =>
            {
                options.Endpoint = endpoints[1];
                options.GameToken = gameToken;
            });
        }
        else
        {
            services.AddHttpDiagnosticTransport(options =>
            {
                options.BaseAddress = endpoints[0];
                options.GameToken = gameToken;
            });
        }

        using ServiceProvider provider = services.BuildServiceProvider();
        DiagnosticField[] fields = [new("document", DiagnosticValueKind.String, MaxLength: length)];
        var hello = new ClientHello(Guid.NewGuid(), 2, [new(new("payload", "Payload", 1), [new("echo", "Echo", DiagnosticPermission.Observe, fields, fields)])]);
        await using IDiagnosticConnection connection = await provider.GetRequiredService<IDiagnosticTransportFactory>().OpenAsync(hello, default);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using IAsyncEnumerator<DiagnosticCommand> commands = connection.ReadCommandsAsync(deadline.Token).GetAsyncEnumerator();
        Task<bool> received = commands.MoveNextAsync().AsTask();
        string payload = new string('x', length - 3) + "日本語";
        var invocation = new OperationInvocation(Guid.NewGuid(), "payload", "echo", new() { ["document"] = DiagnosticValue.From(payload) });
        using var client = new HttpClient { BaseAddress = endpoints[0] };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", operatorToken);
        using var body = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(invocation, DiagnosticJson.Context.OperationInvocation));
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        Task<HttpResponseMessage> responseTask = client.PostAsync($"diagnostics/v1/sessions/{connection.Welcome.SessionId}/operations", body, deadline.Token);
        Assert.True(await received.WaitAsync(deadline.Token));
        Assert.Equal(payload, commands.Current.Arguments["document"].String);
        var result = DiagnosticOperationResult.Success(commands.Current.Arguments);
        var message = new DiagnosticMessage(Guid.NewGuid(), connection.Welcome.SessionId, DiagnosticMessageKind.CommandResult, invocation.RequestId, result, []);
        Assert.True((await connection.PublishAsync(message, deadline.Token)).Accepted);
        using HttpResponseMessage response = await responseTask;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        DiagnosticOperationResult returned = JsonSerializer.Deserialize(await response.Content.ReadAsByteArrayAsync(deadline.Token), DiagnosticJson.Context.DiagnosticOperationResult)!;
        Assert.Equal(payload, returned.Values!["document"].String);
    }

    /// <summary>Checks fields without a declared larger bound retain the default limit.</summary>
    /// <returns>The validation check.</returns>
    [Fact]
    public async Task DefaultStringLimitAndDeclaredResultLimitAreEnforcedAsync()
    {
        var registry = new DiagnosticSessionRegistry(new(), TimeProvider.System);
        DiagnosticField[] fields = [new("document", DiagnosticValueKind.String)];
        Guid session = registry.Open(new(Guid.NewGuid(), 1, [new(new("payload", "Payload", 1), [new("echo", "Echo", DiagnosticPermission.Observe, fields, fields)])])).SessionId;
        var invocation = new OperationInvocation(Guid.NewGuid(), "payload", "echo", new() { ["document"] = DiagnosticValue.From(new string('x', 4097)) });
        await Assert.ThrowsAsync<ArgumentException>(() => registry.InvokeAsync(session, invocation, "actor", default));
        Assert.Equal(0, Assert.Single(registry.List()).PendingCommands);
        invocation.Arguments["document"] = DiagnosticValue.From(new string('x', 4096));
        Task<DiagnosticOperationResult> pending = registry.InvokeAsync(session, invocation, "actor", default);
        var invalid = new DiagnosticMessage(Guid.NewGuid(), session, DiagnosticMessageKind.CommandResult, invocation.RequestId, DiagnosticOperationResult.Success(new Dictionary<string, DiagnosticValue> { ["document"] = DiagnosticValue.From(new string('x', 4097)) }), []);
        Assert.Throws<ArgumentException>(() => registry.Publish(session, invalid));
        Assert.False(pending.IsCompleted);
        Assert.True(registry.Publish(session, invalid with { Result = DiagnosticOperationResult.Success(invocation.Arguments) }).Accepted);
        Assert.Equal(4096, (await pending).Values!["document"].String!.Length);
    }

    /// <summary>Checks schemas still reject undeclared, missing, mistyped and out-of-range fields.</summary>
    /// <param name="invalid">The malformed field case.</param>
    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("kind")]
    [InlineData("range")]
    [InlineData("scalar")]
    public void SchemaValidationRemainsClosed(string invalid)
    {
        DiagnosticField[] fields = [new("value", DiagnosticValueKind.Int64, Minimum: 0, Maximum: 10)];
        var operation = new OperationDescriptor("echo", "Echo", DiagnosticPermission.Observe, fields, fields);
        var values = new Dictionary<string, DiagnosticValue> { ["value"] = DiagnosticValue.From(1L) };
        switch (invalid)
        {
            case "missing":
                values.Clear();
                break;
            case "unknown":
                values.Add("extra", DiagnosticValue.From(1L));
                break;
            case "kind":
                values["value"] = DiagnosticValue.From("1");
                break;
            case "range":
                values["value"] = DiagnosticValue.From(11L);
                break;
            case "scalar":
                values["value"] = new(DiagnosticValueKind.Int64, true, 1, 0, null);
                break;
        }

        var invocation = new OperationInvocation(Guid.NewGuid(), "payload", "echo", values);
        Assert.Throws<ArgumentException>(() => DiagnosticProtocol.ValidateArguments(invocation, operation));
        Assert.Throws<ArgumentException>(() => DiagnosticProtocol.ValidateResult(DiagnosticOperationResult.Success(values), operation));
    }

    /// <summary>Checks telemetry remains capped at 4096 characters after command limits increase.</summary>
    [Fact]
    public void TelemetryStringLimitIsUnchanged()
    {
        var registry = new DiagnosticSessionRegistry(new(), TimeProvider.System);
        Guid session = registry.Open(new(Guid.NewGuid(), 1, [new(new("payload", "Payload", 1), [])])).SessionId;
        var item = new DiagnosticEvent("log", 0, "message", DiagnosticValue.From(new string('x', 4096)), null, null, null, 0, new Dictionary<string, DiagnosticValue>());
        var message = new DiagnosticMessage(Guid.NewGuid(), session, DiagnosticMessageKind.Telemetry, null, null, [item]);
        Assert.True(registry.Publish(session, message).Accepted);
        Assert.Throws<ArgumentException>(() => registry.Publish(session, message with { MessageId = Guid.NewGuid(), Events = [item with { Value = DiagnosticValue.From(new string('x', 4097)) }] }));
        Assert.Throws<ArgumentException>(() => registry.Publish(session, message with { MessageId = Guid.NewGuid(), Events = [item with { Fields = new Dictionary<string, DiagnosticValue> { ["extra"] = DiagnosticValue.From(new string('x', 4097)) } }] }));
    }

    /// <summary>Checks oversized schema declarations and encoded requests retain finite bounds.</summary>
    /// <returns>The rejection check.</returns>
    [Fact]
    public async Task SchemaAndEncodedByteBudgetsAreBoundedAsync()
    {
        var registry = new DiagnosticSessionRegistry(new(), TimeProvider.System);
        DiagnosticField[] oversized = [new("document", DiagnosticValueKind.String, MaxLength: 1048577)];
        Assert.Throws<ArgumentException>(() => registry.Open(new(Guid.NewGuid(), 2, [new(new("payload", "Payload", 1), [new("echo", "Echo", DiagnosticPermission.Observe, oversized, [])])])));
        DiagnosticField[] fields = [new("document", DiagnosticValueKind.String, MaxLength: 1048576)];
        Guid session = registry.Open(new(Guid.NewGuid(), 2, [new(new("payload", "Payload", 1), [new("echo", "Echo", DiagnosticPermission.Observe, fields, [])])])).SessionId;
        var invocation = new OperationInvocation(Guid.NewGuid(), "payload", "echo", new() { ["document"] = DiagnosticValue.From(new string('\u0001', 1048576)) });
        await Assert.ThrowsAsync<ArgumentException>(() => registry.InvokeAsync(session, invocation, "actor", default));
        Assert.Equal(0, Assert.Single(registry.List()).PendingCommands);
    }

    /// <summary>Checks extended schemas require the protocol version that defines their semantics.</summary>
    /// <param name="traffic">Whether to exercise the scheduling extension instead of the size extension.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtendedCatalogRequiresVersionTwo(bool traffic)
    {
        DiagnosticField[] fields = traffic ? [] : [new("document", DiagnosticValueKind.String, MaxLength: 32768)];
        var operation = new OperationDescriptor("update", "Update", DiagnosticPermission.Observe, fields, [], Traffic: traffic ? DiagnosticOperationTraffic.Stream : DiagnosticOperationTraffic.Standard);
        var hello = new ClientHello(Guid.NewGuid(), 1, [new(new("payload", "Payload", 1), [operation])]);
        Assert.Throws<ArgumentException>(() => DiagnosticProtocol.Validate(hello));
        DiagnosticProtocol.Validate(hello with { ProtocolVersion = 2 });
    }
}
