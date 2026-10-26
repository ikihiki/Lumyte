using Lumyte.Diagnostics.Server;
using Lumyte.Diagnostics.Transport;
using Xunit;

namespace Lumyte.Diagnostics.IntegrationTests;

/// <summary>Checks cached data ownership and bounded receipt retention.</summary>
public sealed class RegistryTests
{
    /// <summary>Checks callers cannot change catalogs, permissions, telemetry, queued commands or cached results.</summary>
    /// <returns>The asynchronous operation check.</returns>
    [Fact]
    public async Task PublicSnapshotsDoNotExposeRetainedStateAsync()
    {
        var registry = new DiagnosticSessionRegistry(new(), TimeProvider.System);
        DiagnosticField[] fields = [new DiagnosticField("amount", DiagnosticValueKind.Int64)];
        var hello = new ClientHello(Guid.NewGuid(), 1, [new(new("sample", "Sample", 1), [new("echo", "Echo", DiagnosticPermission.Observe, fields, fields)])]);
        SessionWelcome welcome = registry.Open(hello);
        fields[0] = new("changed", DiagnosticValueKind.Boolean);
        Array.Fill(welcome.Permissions, DiagnosticPermission.OverrideInput);
        SessionSnapshot snapshot = Assert.Single(registry.List());
        Assert.Equal("amount", snapshot.Catalog[0].Operations[0].Arguments[0].Id);
        snapshot.Catalog[0].Operations[0].Arguments[0] = new("changed", DiagnosticValueKind.Boolean);
        Assert.Equal("amount", Assert.Single(registry.List()).Catalog[0].Operations[0].Arguments[0].Id);

        var arguments = new Dictionary<string, DiagnosticValue> { ["amount"] = DiagnosticValue.From(42L) };
        var invocation = new OperationInvocation(Guid.NewGuid(), "sample", "echo", arguments);
        Task<DiagnosticOperationResult> pending = registry.InvokeAsync(welcome.SessionId, invocation, "actor", default);
        arguments["amount"] = DiagnosticValue.From(99L);
        DiagnosticCommand first = Assert.Single(await registry.PollAsync(welcome.SessionId, default));
        Assert.Equal(42, first.Arguments["amount"].Int64);
        first.Arguments["amount"] = DiagnosticValue.From(88L);
        Assert.Equal(42, Assert.Single(await registry.PollAsync(welcome.SessionId, default)).Arguments["amount"].Int64);
        var resultFields = new Dictionary<string, DiagnosticValue> { ["amount"] = DiagnosticValue.From(42L) };
        Assert.True(registry.Publish(welcome.SessionId, new(Guid.NewGuid(), welcome.SessionId, DiagnosticMessageKind.CommandResult, invocation.RequestId, DiagnosticOperationResult.Success(resultFields), [])).Accepted);
        resultFields["amount"] = DiagnosticValue.From(77L);
        DiagnosticOperationResult result = await pending;
        Assert.Equal(42, result.Values!["amount"].Int64);
        Assert.IsType<Dictionary<string, DiagnosticValue>>(result.Values)["amount"] = DiagnosticValue.From(66L);
        arguments["amount"] = DiagnosticValue.From(42L);
        Assert.Equal(42, (await registry.InvokeAsync(welcome.SessionId, invocation, "actor", default)).Values!["amount"].Int64);

        var tags = new Dictionary<string, DiagnosticValue> { ["name"] = DiagnosticValue.From("before"), ["count"] = DiagnosticValue.From(1L) };
        var message = new DiagnosticMessage(Guid.NewGuid(), welcome.SessionId, DiagnosticMessageKind.Telemetry, null, null, [new("metric", 0, "value", DiagnosticValue.From(1L), null, null, null, 0, tags)]);
        registry.Publish(welcome.SessionId, message);
        DiagnosticEvent reordered = message.Events[0] with { Fields = tags.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal) };
        Assert.True(registry.Publish(welcome.SessionId, message with { Events = [reordered] }).Accepted);
        Assert.Equal(1, Assert.Single(registry.List()).TelemetryReceived);
        tags["name"] = DiagnosticValue.From("after");
        DiagnosticEvent observed = Assert.Single(registry.Telemetry(welcome.SessionId));
        Assert.Equal("before", observed.Fields["name"].String);
        Assert.IsType<Dictionary<string, DiagnosticValue>>(observed.Fields)["name"] = DiagnosticValue.From("changed");
        Assert.Equal("before", Assert.Single(registry.Telemetry(welcome.SessionId)).Fields["name"].String);
    }

    /// <summary>Checks cache eviction follows receipt arrival order after dictionary slots are reused.</summary>
    [Fact]
    public void ReceiptCacheEvictsOldestMessages()
    {
        var registry = new DiagnosticSessionRegistry(new(), TimeProvider.System);
        Guid session = registry.Open(new(Guid.NewGuid(), 1, [new(new("sample", "Sample", 1), [])])).SessionId;
        Guid[] ids = Enumerable.Range(0, 1026).Select(_ => Guid.NewGuid()).ToArray();
        foreach (Guid id in ids)
        {
            Assert.True(registry.Publish(session, new(id, session, DiagnosticMessageKind.Heartbeat, null, null, [])).Accepted);
        }

        var conflict = new DiagnosticMessage(ids[^2], session, DiagnosticMessageKind.Telemetry, null, null, []);
        Assert.Equal("message-id-conflict", registry.Publish(session, conflict).ErrorCode);
        Assert.True(registry.Publish(session, conflict with { MessageId = ids[0] }).Accepted);
    }

    /// <summary>Checks pushed callbacks cannot mutate pending redelivery state.</summary>
    /// <returns>The asynchronous operation check.</returns>
    [Fact]
    public async Task PushReceivesOwnedArgumentsAsync()
    {
        var registry = new DiagnosticSessionRegistry(new(), TimeProvider.System);
        DiagnosticCommand? delivered = null;
        Guid session = registry.Open(new(Guid.NewGuid(), 1, [new(new("sample", "Sample", 1), [new("echo", "Echo", DiagnosticPermission.Observe, [], [])])]), command => delivered = command).SessionId;
        var invocation = new OperationInvocation(Guid.NewGuid(), "sample", "echo", []);
        Task<DiagnosticOperationResult> pending = registry.InvokeAsync(session, invocation, "actor", default);
        Assert.NotNull(delivered);
        delivered.Arguments.Add("unexpected", DiagnosticValue.From(true));
        Task<DiagnosticOperationResult> duplicate = registry.InvokeAsync(session, invocation, "actor", default);
        registry.Close(session);
        Assert.Equal("target-gone", (await pending).Code);
        Assert.Equal("target-gone", (await duplicate).Code);
    }
}
