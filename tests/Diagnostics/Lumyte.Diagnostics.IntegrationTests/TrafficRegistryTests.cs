using Lumyte.Diagnostics.Server;
using Lumyte.Diagnostics.Transport;
using Xunit;

namespace Lumyte.Diagnostics.IntegrationTests;

/// <summary>Checks bounded traffic isolation and stream retry lifetimes.</summary>
public sealed class TrafficRegistryTests
{
    /// <summary>Checks sustained 60 Hz traffic does not consume the ordinary command cache.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task SustainedStreamRetainsControlCapacityAsync()
    {
        var clock = new ManualClock();
        var registry = new DiagnosticSessionRegistry(new(), clock);
        Guid session = registry.Open(Hello()).SessionId;
        for (int index = 0; index < 2100; index++)
        {
            var invocation = new OperationInvocation(Guid.NewGuid(), "input", "send", [], TimeoutMilliseconds: 1000);
            Task<DiagnosticOperationResult> pending = registry.InvokeAsync(session, invocation, "actor", default);
            DiagnosticCommand command = Assert.Single(await registry.PollAsync(session, default));
            Assert.Equal(invocation.RequestId, command.RequestId);
            Complete(registry, session, command);
            Assert.Equal("success", (await pending).Status);
            Assert.Equal("success", (await registry.InvokeAsync(session, invocation, "actor", default)).Status);
            clock.Advance(TimeSpan.FromMilliseconds(17));
        }

        Task<DiagnosticOperationResult> stop = registry.InvokeAsync(session, new(Guid.NewGuid(), "input", "stop", []), "actor", default);
        Complete(registry, session, Assert.Single(await registry.PollAsync(session, default)));
        Assert.Equal("success", (await stop).Status);
    }

    /// <summary>Checks ordinary command saturation cannot reject or delay a control operation in polling.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task ControlIsAcceptedAndPolledFirstWhenOrdinaryQueueIsFullAsync()
    {
        var registry = new DiagnosticSessionRegistry(new(), TimeProvider.System);
        Guid session = registry.Open(Hello()).SessionId;
        var pending = new List<Task<DiagnosticOperationResult>>();
        for (int index = 0; index < 64; index++)
        {
            pending.Add(registry.InvokeAsync(session, new(Guid.NewGuid(), "input", "edit", []), "actor", default));
        }

        DiagnosticOperationResult rejected = await registry.InvokeAsync(session, new(Guid.NewGuid(), "input", "edit", []), "actor", default);
        Assert.Equal("busy", rejected.Code);
        Task<DiagnosticOperationResult> stop = registry.InvokeAsync(session, new(Guid.NewGuid(), "input", "stop", []), "actor", default);
        DiagnosticCommand command = Assert.Single(await registry.PollAsync(session, default));
        Assert.Equal("stop", command.OperationId);
        Complete(registry, session, command);
        Assert.Equal("success", (await stop).Status);
        registry.Close(session);
        Assert.All(await Task.WhenAll(pending), result => Assert.Equal("target-gone", result.Code));
    }

    /// <summary>Checks v2 polling delivers new work before retrying unacknowledged commands.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task PollingV2AllowsConcurrentDeliveryAndRetriesLostResponsesAsync()
    {
        var clock = new ManualClock();
        var registry = new DiagnosticSessionRegistry(new(), clock);
        Guid session = registry.Open(Hello()).SessionId;
        var first = new OperationInvocation(Guid.NewGuid(), "input", "send", [], TimeoutMilliseconds: 1000);
        OperationInvocation second = first with { RequestId = Guid.NewGuid() };
        Task<DiagnosticOperationResult> firstResult = registry.InvokeAsync(session, first, "actor", default);
        Task<DiagnosticOperationResult> secondResult = registry.InvokeAsync(session, second, "actor", default);

        Assert.Equal(first.RequestId, Assert.Single(await registry.PollAsync(session, default)).RequestId);
        Assert.Equal(second.RequestId, Assert.Single(await registry.PollAsync(session, default)).RequestId);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(first.RequestId, Assert.Single(await registry.PollAsync(session, default)).RequestId);

        registry.Close(session);
        Assert.Equal("target-gone", (await firstResult).Code);
        Assert.Equal("target-gone", (await secondResult).Code);
    }

    /// <summary>Checks stream retention is explicitly bounded and request IDs cannot move between traffic classes.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task RejectsLongStreamDeadlinesAndCrossClassRequestReuseAsync()
    {
        var registry = new DiagnosticSessionRegistry(new(), TimeProvider.System);
        Guid session = registry.Open(Hello()).SessionId;
        DiagnosticOperationResult invalid = await registry.InvokeAsync(session, new(Guid.NewGuid(), "input", "send", []), "actor", default);
        Assert.Equal("invalid-timeout", invalid.Code);
        var invocation = new OperationInvocation(Guid.NewGuid(), "input", "send", [], TimeoutMilliseconds: 1000);
        Task<DiagnosticOperationResult> pending = registry.InvokeAsync(session, invocation, "actor", default);
        DiagnosticOperationResult conflict = await registry.InvokeAsync(session, invocation with { OperationId = "stop" }, "actor", default);
        Assert.Equal("request-id-conflict", conflict.Code);
        registry.Close(session);
        Assert.Equal("target-gone", (await pending).Code);
    }

    private static ClientHello Hello() => new(Guid.NewGuid(), 2, [new(new("input", "Input", 1), [
        new("edit", "Edit", DiagnosticPermission.Observe, [], []),
        new("send", "Send", DiagnosticPermission.Observe, [], [], Traffic: DiagnosticOperationTraffic.Stream),
        new("stop", "Stop", DiagnosticPermission.Observe, [], [], Traffic: DiagnosticOperationTraffic.Control),
    ])]);

    private static void Complete(DiagnosticSessionRegistry registry, Guid session, DiagnosticCommand command)
        => Assert.True(registry.Publish(session, new(Guid.NewGuid(), session, DiagnosticMessageKind.CommandResult, command.RequestId, DiagnosticOperationResult.Success(new Dictionary<string, DiagnosticValue>()), [])).Accepted);

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_ticks);

        public void Advance(TimeSpan duration) => _ticks += duration.Ticks;
    }
}
