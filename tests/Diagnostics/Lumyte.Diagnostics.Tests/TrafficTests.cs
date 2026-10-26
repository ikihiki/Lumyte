using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Diagnostics.Tests;

/// <summary>Verifies reserved scheduling, trusted traffic classes and bounded deduplication.</summary>
public sealed class TrafficTests
{
    /// <summary>Control commands precede a fair alternation of stream and ordinary requests.</summary>
    /// <returns>The completed test.</returns>
    [Fact]
    public async Task ControlRunsFirstAndOtherTrafficAlternatesAsync()
    {
        var clock = new ManualClock();
        using ServiceProvider services = Build(clock);
        using IServiceScope scope = services.CreateScope();
        IDiagnosticPump<ExecutionPoint> pump = scope.ServiceProvider.GetRequiredService<IDiagnosticPump<ExecutionPoint>>();
        pump.Activate();
        var tasks = new List<Task<DiagnosticOperationResult>>();
        foreach (string operation in new[] { "standard", "standard", "stream", "stream", "control" })
        {
            tasks.Add(pump.SubmitAsync(Request(operation, clock)));
        }

        pump.Pump(default, new(TimeSpan.FromSeconds(1), 3));
        pump.Pump(default, new(TimeSpan.FromSeconds(1), 2));
        Assert.All(await Task.WhenAll(tasks), result => Assert.Equal("success", result.Status));
        Assert.Equal(["control", "stream", "standard", "stream", "standard"], scope.ServiceProvider.GetRequiredService<TrafficContributor>().Executed);
    }

    /// <summary>Each pending lane has its own capacity and stopping completes all accepted work.</summary>
    /// <param name="operation">The lane to fill.</param>
    /// <param name="capacity">Its pending capacity.</param>
    /// <returns>The completed test.</returns>
    [Theory]
    [InlineData("standard", 192)]
    [InlineData("stream", 48)]
    [InlineData("control", 16)]
    public async Task PendingCapacityIsReservedPerLaneAsync(string operation, int capacity)
    {
        var clock = new ManualClock();
        using ServiceProvider services = Build(clock);
        using IServiceScope scope = services.CreateScope();
        IDiagnosticPump<ExecutionPoint> pump = scope.ServiceProvider.GetRequiredService<IDiagnosticPump<ExecutionPoint>>();
        pump.Activate();
        var accepted = new List<Task<DiagnosticOperationResult>>();
        for (int i = 0; i < capacity; i++)
        {
            accepted.Add(pump.SubmitAsync(Request(operation, clock)));
        }

        Assert.All(accepted, task => Assert.False(task.IsCompleted));
        Assert.Equal("busy", (await pump.SubmitAsync(Request(operation, clock))).Code);
        foreach (string other in new[] { "standard", "stream", "control" }.Where(item => item != operation))
        {
            Task<DiagnosticOperationResult> pending = pump.SubmitAsync(Request(other, clock));
            Assert.False(pending.IsCompleted);
            accepted.Add(pending);
        }

        pump.Deactivate();
        Assert.All(await Task.WhenAll(accepted), result => Assert.Equal("target-gone", result.Code));
        pump.Activate();
        Task<DiagnosticOperationResult> after = pump.SubmitAsync(Request(operation, clock));
        pump.Pump(default, new(TimeSpan.FromSeconds(1), 1));
        Assert.Equal("success", (await after).Status);
    }

    /// <summary>The trusted catalog enforces stream expiry while other operations accept longer deadlines.</summary>
    /// <returns>The completed test.</returns>
    [Fact]
    public async Task StreamDeadlineComesFromRegisteredMetadataAsync()
    {
        var clock = new ManualClock();
        using ServiceProvider services = Build(clock);
        using IServiceScope scope = services.CreateScope();
        IDiagnosticPump<ExecutionPoint> pump = scope.ServiceProvider.GetRequiredService<IDiagnosticPump<ExecutionPoint>>();
        pump.Activate();
        Assert.Equal(DiagnosticOperationTraffic.Stream, pump.Catalog[0].Operations.Single(item => item.Id == "stream").Traffic);
        Assert.Equal("invalid-timeout", (await pump.SubmitAsync(Request("stream", clock) with { DeadlineTimestamp = 1001 })).Code);
        Task<DiagnosticOperationResult> standard = pump.SubmitAsync(Request("standard", clock) with { DeadlineTimestamp = 1001 });
        Assert.False(standard.IsCompleted);
        pump.Pump(default, new(TimeSpan.FromSeconds(1), 1));
        Assert.Equal("success", (await standard).Status);
    }

    /// <summary>Completed stream requests retain their identities until expiry even at capacity.</summary>
    /// <returns>The completed test.</returns>
    [Fact]
    public async Task RetainedCapacityCannotEvictUnexpiredRequestsAsync()
    {
        var clock = new ManualClock();
        using ServiceProvider services = Build(clock);
        using IServiceScope scope = services.CreateScope();
        IDiagnosticPump<ExecutionPoint> pump = scope.ServiceProvider.GetRequiredService<IDiagnosticPump<ExecutionPoint>>();
        pump.Activate();
        DiagnosticRequest original = Request("stream", clock);
        Task<DiagnosticOperationResult> first = pump.SubmitAsync(original);
        pump.Pump(default, new(TimeSpan.FromSeconds(1), 1));
        for (int i = 1; i < 1024; i++)
        {
            Task<DiagnosticOperationResult> pending = pump.SubmitAsync(Request("stream", clock));
            pump.Pump(default, new(TimeSpan.FromSeconds(1), 1));
            Assert.Equal("success", (await pending).Status);
        }

        Assert.Equal("busy", (await pump.SubmitAsync(Request("stream", clock))).Code);
        Assert.Same(first, pump.SubmitAsync(original));
        Assert.Equal("request-id-conflict", (await pump.SubmitAsync(original with { OperationId = "control" })).Code);
        Task<DiagnosticOperationResult> control = pump.SubmitAsync(Request("control", clock));
        Task<DiagnosticOperationResult> standard = pump.SubmitAsync(Request("standard", clock));
        pump.Pump(default, new(TimeSpan.FromSeconds(1), 2));
        Assert.Equal("success", (await control).Status);
        Assert.Equal("success", (await standard).Status);
        clock.Timestamp = 999;
        Assert.Same(first, pump.SubmitAsync(original));
        clock.Timestamp = 1000;
        Task<DiagnosticOperationResult> replacement = pump.SubmitAsync(original with { DeadlineTimestamp = 2000 });
        Assert.NotSame(first, replacement);
        pump.Pump(default, new(TimeSpan.FromSeconds(1), 1));
        Assert.Equal("success", (await replacement).Status);
    }

    /// <summary>A continuous input stream does not inherit the ordinary thirty-second result-retention limit.</summary>
    /// <returns>The completed test.</returns>
    [Fact]
    public async Task StreamContinuesAcrossManyDeadlineWindowsAsync()
    {
        var clock = new ManualClock();
        using ServiceProvider services = Build(clock);
        using IServiceScope scope = services.CreateScope();
        IDiagnosticPump<ExecutionPoint> pump = scope.ServiceProvider.GetRequiredService<IDiagnosticPump<ExecutionPoint>>();
        pump.Activate();
        for (int frame = 0; frame < 2400; frame++)
        {
            clock.Timestamp = frame * 17;
            Task<DiagnosticOperationResult> pending = pump.SubmitAsync(Request("stream", clock));
            pump.Pump(default, new(TimeSpan.FromSeconds(1), 1));
            Assert.Equal("success", (await pending).Status);
        }

        Assert.Equal(2400, scope.ServiceProvider.GetRequiredService<TrafficContributor>().Executed.Count);
    }

    /// <summary>Expiry of deduplication storage cannot replace the result of an already running handler.</summary>
    /// <returns>The completed test.</returns>
    [Fact]
    public async Task ExpiringRunningRequestPreservesItsResultAsync()
    {
        var clock = new ManualClock();
        using ServiceProvider services = Build(clock);
        using IServiceScope scope = services.CreateScope();
        IDiagnosticPump<ExecutionPoint> pump = scope.ServiceProvider.GetRequiredService<IDiagnosticPump<ExecutionPoint>>();
        TrafficContributor contributor = scope.ServiceProvider.GetRequiredService<TrafficContributor>();
        pump.Activate();
        Task<DiagnosticOperationResult> first = pump.SubmitAsync(Request("stream", clock));
        Task<DiagnosticOperationResult>? next = null;
        contributor.BeforeReturn = () =>
        {
            contributor.BeforeReturn = null;
            clock.Timestamp = 1000;
            next = pump.SubmitAsync(Request("control", clock));
            Assert.False(first.IsCompleted);
        };
        pump.Pump(default, new(TimeSpan.FromSeconds(1), 2));
        Assert.Equal("success", (await first).Status);
        Assert.NotNull(next);
        Assert.Equal("success", (await next).Status);
    }

    /// <summary>A running request keeps its identity after expiry without blocking reclamation of queued expired work.</summary>
    /// <returns>The completed test.</returns>
    [Fact]
    public async Task ExpiringRunningRequestRejectsReuseUntilCompletionAsync()
    {
        var clock = new ManualClock();
        using ServiceProvider services = Build(clock);
        using IServiceScope scope = services.CreateScope();
        IDiagnosticPump<ExecutionPoint> pump = scope.ServiceProvider.GetRequiredService<IDiagnosticPump<ExecutionPoint>>();
        TrafficContributor contributor = scope.ServiceProvider.GetRequiredService<TrafficContributor>();
        pump.Activate();
        DiagnosticRequest request = Request("stream", clock);
        Task<DiagnosticOperationResult> first = pump.SubmitAsync(request);
        Task<DiagnosticOperationResult> expired = pump.SubmitAsync(Request("standard", clock) with { DeadlineTimestamp = 1100 });
        Task<DiagnosticOperationResult>? conflict = null;
        contributor.BeforeReturn = () =>
        {
            contributor.BeforeReturn = null;
            clock.Timestamp = 1200;
            conflict = pump.SubmitAsync(request with { DeadlineTimestamp = 2200 });
            Assert.False(first.IsCompleted);
            Assert.True(expired.IsCompleted);
        };
        pump.Pump(default, new(TimeSpan.FromSeconds(1), 2));
        Assert.Equal("success", (await first).Status);
        Assert.NotNull(conflict);
        Assert.Equal("request-id-conflict", (await conflict).Code);
        Assert.Equal("expired", (await expired).Code);
        Assert.Equal(["stream"], contributor.Executed);
        Task<DiagnosticOperationResult> replacement = pump.SubmitAsync(request with { DeadlineTimestamp = 2200 });
        pump.Pump(default, new(TimeSpan.FromSeconds(1), 1));
        Assert.Equal("success", (await replacement).Status);
        Assert.Equal(["stream", "stream"], contributor.Executed);
    }

    /// <summary>Disposal terminates queued requests in all lanes, including pruned expired requests.</summary>
    /// <returns>The completed test.</returns>
    [Fact]
    public async Task DisposeCompletesAllLanesAsync()
    {
        var clock = new ManualClock();
        using ServiceProvider services = Build(clock);
        IServiceScope scope = services.CreateScope();
        IDiagnosticPump<ExecutionPoint> pump = scope.ServiceProvider.GetRequiredService<IDiagnosticPump<ExecutionPoint>>();
        pump.Activate();
        Task<DiagnosticOperationResult> expired = pump.SubmitAsync(Request("stream", clock));
        clock.Timestamp = 1000;
        Task<DiagnosticOperationResult>[] pending = new[] { "standard", "stream", "control" }
            .Select(operation => pump.SubmitAsync(Request(operation, clock))).ToArray();
        scope.Dispose();
        Assert.Equal("expired", (await expired).Code);
        Assert.All(await Task.WhenAll(pending), result => Assert.Equal("target-gone", result.Code));
        Assert.Equal("target-gone", (await pump.SubmitAsync(Request("control", clock))).Code);
    }

    /// <summary>Manual registrations reject undefined scheduling values.</summary>
    [Fact]
    public void UnknownTrafficCannotBeRegistered()
    {
        var builder = new DiagnosticBuilder();
        Assert.Throws<ArgumentException>(() => builder.Operation(
            new("bad", "Bad", DiagnosticPermission.Observe, [], [], Traffic: (DiagnosticOperationTraffic)3),
            (_, _) => DiagnosticOperationResult.Success(new Dictionary<string, DiagnosticValue>())));
    }

    private static ServiceProvider Build(ManualClock clock)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddScoped(_ => new TrafficContributor());
        services.AddDiagnosticExecutionPoint<ExecutionPoint>("point");
        services.AddDiagnosticSubsystem<TrafficContributor, ExecutionPoint>(new("traffic", "Traffic", 1));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static DiagnosticRequest Request(string operation, ManualClock clock) => new(
        "traffic",
        operation,
        new(Guid.NewGuid(), Guid.NewGuid(), default, "actor", null, default),
        new Dictionary<string, DiagnosticValue>(),
        new HashSet<DiagnosticPermission> { DiagnosticPermission.Observe },
        clock.Timestamp + 1000);

    private sealed class ExecutionPoint;

    private sealed class TrafficContributor : IDiagnosticContributor
    {
        public List<string> Executed { get; } = [];

        public Action? BeforeReturn { get; set; }

        public void Configure(DiagnosticBuilder builder)
        {
            foreach (DiagnosticOperationTraffic traffic in Enum.GetValues<DiagnosticOperationTraffic>())
            {
                string id = traffic.ToString().ToLowerInvariant();
                builder.Operation(new(id, id, DiagnosticPermission.Observe, [], [], Traffic: traffic), (_, _) =>
                {
                    Executed.Add(id);
                    BeforeReturn?.Invoke();
                    return DiagnosticOperationResult.Success(new Dictionary<string, DiagnosticValue>());
                });
            }
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        public long Timestamp { get; set; }

        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => Timestamp;
    }
}
