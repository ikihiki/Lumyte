using Lumyte.Diagnostics;
using Lumyte.Diagnostics.RenderGraph;
using Lumyte.Graphics.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Graphics.RenderGraph.Tests;

/// <summary>Verifies bounded captures and aggregate monitoring through the existing diagnostic pump.</summary>
public sealed class RenderGraphDiagnosticsTests
{
    /// <summary>Checks capture details stay accessible as later frames compile and record.</summary>
    [Fact]
    public void PumpRetainsCaptureAcrossFramesWithPermissionsAndStableIndices()
    {
        var clock = new TestClock();
        using ServiceProvider provider = Build(clock);
        using IServiceScope game = provider.CreateScope();
        using IServiceScope other = provider.CreateScope();
        RenderGraphDiagnostics observer = game.ServiceProvider.GetRequiredService<RenderGraphDiagnostics>();
        Assert.NotSame(observer, other.ServiceProvider.GetRequiredService<RenderGraphDiagnostics>());
        IDiagnosticPump<BeforeRender> pump = game.ServiceProvider.GetRequiredService<IDiagnosticPump<BeforeRender>>();
        pump.Activate();
        Assert.All(Assert.Single(pump.Catalog).Operations, operation =>
        {
            Assert.Equal(DiagnosticPermission.Observe, operation.RequiredPermission);
            Assert.False(operation.RequiresRevision);
        });
        Assert.Equal(0, Dispatch(pump, "get-summary").Values!["compiles"].Int64);
        Assert.Equal("forbidden", Dispatch(pump, "request-capture", new() { ["graph-name"] = DiagnosticValue.From("target") }, observe: false).Code);
        string id = Request(pump, "target");
        Assert.Equal("not-ready", Dispatch(pump, "get-pass", Details(id)).Code);
        Compile(observer, "unrelated");
        Assert.Equal("pending", Capture(pump, id).Values!["state"].String);
        var device = new TestDevice();
        var graph = new RenderGraph(device, "target", observer);
        RenderGraphBuffer<uint> temporary = graph.CreateBuffer<uint>(new() { Count = 4, Usage = BufferUsage.CopyDestination | BufferUsage.CopySource }, "temporary");
        using var external = new TestBuffer();
        RenderGraphBuffer<uint> output = graph.ImportBuffer(external, default);
        graph.AddPass("produce", _ => { }).UseBuffer(temporary, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        graph.AddPass("consume", _ => { }).UseBuffer(temporary, new(PipelineStage.Copy, ResourceAccess.CopyRead)).UseBuffer(output, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        graph.AddPass("unused", _ => { });
        using CompiledRenderGraph plan = graph.Compile(frameId: 100);
        Assert.NotNull(plan.Snapshot);
        Assert.Equal(0, device.Buffers);
        using var commands = new RecordingCommands();
        plan.Record(commands);
        commands.Complete();
        plan.Dispose();
        for (int frame = 101; frame < 106; frame++)
        {
            Compile(observer, "target", frame, record: true);
        }

        DiagnosticOperationResult capture = Capture(pump, id);
        Assert.Equal("recorded", capture.Values!["state"].String);
        Assert.Equal("target", capture.Values["name"].String);
        Assert.Equal(100, capture.Values["frame-id"].Int64);
        Assert.Equal(plan.Run.RunId.ToString("D"), capture.Values["run-id"].String);
        Assert.Equal("unused", Dispatch(pump, "get-pass", Details(id, 2)).Values!["name"].String);
        Assert.Equal(-1, Dispatch(pump, "get-pass", Details(id, 2)).Values!["execution-index"].Int64);
        DiagnosticOperationResult resource = Dispatch(pump, "get-resource", Details(id));
        Assert.Equal("temporary", resource.Values!["name"].String);
        Assert.Equal(0, resource.Values["first-pass"].Int64);
        Assert.Equal(1, resource.Values["last-pass"].Int64);
        DiagnosticOperationResult dependency = Dispatch(pump, "get-dependency", Details(id));
        Assert.Equal(0, dependency.Values!["before"].Int64);
        Assert.Equal(1, dependency.Values["after"].Int64);
        DiagnosticOperationResult barrier = Dispatch(pump, "get-barrier", Details(id));
        Assert.Equal(1, barrier.Values!["pass-index"].Int64);
        Assert.Equal(0, barrier.Values["resource-index"].Int64);
        Assert.Equal((long)ResourceAccess.CopyWrite, barrier.Values["before-access"].Int64);
        Assert.Equal((long)ResourceAccess.CopyRead, barrier.Values["after-access"].Int64);
        Assert.Equal("invalid-arguments", Dispatch(pump, "get-pass", Details(id, -1)).Code);
        Assert.Equal("not-found", Dispatch(pump, "get-pass", Details(id, long.MaxValue)).Code);
        Assert.Equal("not-found", Capture(pump, Guid.NewGuid().ToString("D")).Code);
        Assert.Equal("forbidden", Dispatch(pump, "get-capture", new() { ["capture-id"] = DiagnosticValue.From(id) }, observe: false).Code);
        pump.Deactivate();
    }

    /// <summary>Checks compile and interleaved record failures affect only the captured run that failed.</summary>
    [Fact]
    public void FailuresRemainAssociatedWithTheirOwnCapture()
    {
        using ServiceProvider provider = Build(new TestClock());
        using IServiceScope game = provider.CreateScope();
        RenderGraphDiagnostics observer = game.ServiceProvider.GetRequiredService<RenderGraphDiagnostics>();
        IDiagnosticPump<BeforeRender> pump = game.ServiceProvider.GetRequiredService<IDiagnosticPump<BeforeRender>>();
        pump.Activate();
        string a = Request(pump, "A");
        var first = new RenderGraph(name: "A", observer: observer);
        first.AddPass("record-failure", _ => throw new InvalidOperationException("A failed")).KeepAlive();
        using CompiledRenderGraph plan = first.Compile(frameId: 10);
        string b = Request(pump, "B");
        var second = new RenderGraph(name: "B", observer: observer);
        RenderGraphPass one = second.AddPass("one", _ => { }).KeepAlive();
        RenderGraphPass two = second.AddPass("two", _ => { }).KeepAlive();
        one.DependsOn(two);
        two.DependsOn(one);
        Assert.Throws<InvalidOperationException>(() => second.Compile(frameId: 11));
        Assert.Equal("compiled", Capture(pump, a).Values!["state"].String);
        Assert.Equal("compile-failed", Capture(pump, b).Values!["state"].String);
        Assert.Equal("not-ready", Dispatch(pump, "get-pass", Details(b)).Code);
        using var commands = new RecordingCommands();
        Assert.Throws<InvalidOperationException>(() => plan.Record(commands));
        commands.Dispose();
        Assert.Equal("record-failed", Capture(pump, a).Values!["state"].String);
        Assert.Equal("A", Capture(pump, a).Values!["name"].String);
        Assert.Equal(10, Capture(pump, a).Values!["frame-id"].Int64);
        Assert.Equal("B", Capture(pump, b).Values!["name"].String);
        Assert.Equal("compile-failed", Capture(pump, b).Values!["state"].String);
        Assert.NotEqual(Capture(pump, a).Values!["run-id"].String, Capture(pump, b).Values!["run-id"].String);
        Assert.Equal(2, Dispatch(pump, "get-summary").Values!["failures"].Int64);
        pump.Deactivate();
    }

    /// <summary>Checks a pending request is bounded, and both pending and completed entries expire.</summary>
    [Fact]
    public void CaptureLifetimeIncludesPendingTimeAndCountLimitEvictsOldest()
    {
        var clock = new TestClock();
        using ServiceProvider provider = Build(clock, configure: options =>
        {
            options.MaxCaptures = 2;
            options.MaxSnapshotBytes = 2048;
            options.MaxRetainedBytes = 8192;
            options.Lifetime = TimeSpan.FromSeconds(10);
        });
        using IServiceScope game = provider.CreateScope();
        RenderGraphDiagnostics observer = game.ServiceProvider.GetRequiredService<RenderGraphDiagnostics>();
        IDiagnosticPump<BeforeRender> pump = game.ServiceProvider.GetRequiredService<IDiagnosticPump<BeforeRender>>();
        pump.Activate();
        string pending = Request(pump, "never");
        Assert.Equal("busy", Dispatch(pump, "request-capture", new() { ["graph-name"] = DiagnosticValue.From(string.Empty) }).Code);
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal("not-found", Capture(pump, pending).Code);
        string a = Request(pump);
        Compile(observer, "A", expectCapture: true);
        string b = Request(pump);
        Compile(observer, "B", expectCapture: true);
        string c = Request(pump);
        Compile(observer, "C", expectCapture: true);
        Assert.Equal("not-found", Capture(pump, a).Code);
        Assert.Equal("success", Capture(pump, b).Status);
        Assert.Equal("success", Capture(pump, c).Status);
        Assert.Equal(2, Dispatch(pump, "get-summary").Values!["captures"].Int64);
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal("not-found", Capture(pump, c).Code);
        Assert.Equal(0, Dispatch(pump, "get-summary").Values!["retained-bytes"].Int64);
        pump.Deactivate();
    }

    /// <summary>Checks byte pressure counts capture records and evicts entries before reserving another capture.</summary>
    [Fact]
    public void RetainedByteBudgetIncludesRecordsAndReservations()
    {
        using ServiceProvider provider = Build(new TestClock(), configure: options =>
        {
            options.MaxCaptures = 10;
            options.MaxSnapshotBytes = 2048;
            options.MaxRetainedBytes = 5000;
        });
        using IServiceScope game = provider.CreateScope();
        RenderGraphDiagnostics observer = game.ServiceProvider.GetRequiredService<RenderGraphDiagnostics>();
        IDiagnosticPump<BeforeRender> pump = game.ServiceProvider.GetRequiredService<IDiagnosticPump<BeforeRender>>();
        pump.Activate();
        string first = Request(pump);
        Compile(observer, "A", expectCapture: true);
        string second = Request(pump);
        Assert.Equal("not-found", Capture(pump, first).Code);
        Assert.Equal("pending", Capture(pump, second).Values!["state"].String);
        Compile(observer, "B", expectCapture: true);
        Assert.InRange(Dispatch(pump, "get-summary").Values!["retained-bytes"].Int64, 1, 5000);
        Assert.Equal(1, Dispatch(pump, "get-summary").Values!["captures"].Int64);
        pump.Deactivate();
    }

    /// <summary>Checks an oversized graph remains usable and reports missing details without allocating resources.</summary>
    [Fact]
    public void OversizedCaptureReportsTooLargeWithoutDisruptingTheGraph()
    {
        using ServiceProvider provider = Build(new TestClock(), configure: options => options.MaxSnapshotBytes = 1);
        using IServiceScope game = provider.CreateScope();
        RenderGraphDiagnostics observer = game.ServiceProvider.GetRequiredService<RenderGraphDiagnostics>();
        IDiagnosticPump<BeforeRender> pump = game.ServiceProvider.GetRequiredService<IDiagnosticPump<BeforeRender>>();
        pump.Activate();
        string id = Request(pump);
        var graph = new RenderGraph(name: "large", observer: observer);
        graph.AddPass("draw", _ => { }).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        Assert.Null(plan.Snapshot);
        using var commands = new RecordingCommands();
        plan.Record(commands);
        commands.Complete();
        Assert.Equal("too-large", Capture(pump, id).Values!["state"].String);
        Assert.Equal(1, Capture(pump, id).Values!["live-passes"].Int64);
        Assert.Equal("too-large", Dispatch(pump, "get-pass", Details(id)).Code);
        Assert.Equal(2048, Capture(pump, id).Values!["estimated-bytes"].Int64);
        pump.Deactivate();
    }

    /// <summary>Checks either configuration switch disables observer work and telemetry.</summary>
    /// <param name="diagnosticsEnabled">Whether the existing diagnostic system is enabled.</param>
    /// <param name="adapterEnabled">Whether the render graph adapter is enabled.</param>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void DisabledDiagnosticsDoNotCollectOrCapture(bool diagnosticsEnabled, bool adapterEnabled)
    {
        var clock = new TestClock();
        using ServiceProvider provider = Build(clock, diagnosticsEnabled, adapterEnabled);
        using IServiceScope game = provider.CreateScope();
        RenderGraphDiagnostics observer = game.ServiceProvider.GetRequiredService<RenderGraphDiagnostics>();
        DiagnosticTelemetry telemetry = game.ServiceProvider.GetRequiredService<DiagnosticTelemetry>();
        IDiagnosticPump<BeforeRender> pump = game.ServiceProvider.GetRequiredService<IDiagnosticPump<BeforeRender>>();
        telemetry.Start();
        pump.Activate();
        Assert.False(observer.IsEnabled);
        Compile(observer, "disabled", record: true);
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(0, Dispatch(pump, "get-summary").Values!["compiles"].Int64);
        Assert.Equal("disabled", Dispatch(pump, "request-capture", new() { ["graph-name"] = DiagnosticValue.From(string.Empty) }).Code);
        Assert.False(telemetry.TryRead(out _));
        pump.Deactivate();
    }

    /// <summary>Checks many frames produce one aggregate window without per-operation events or spans.</summary>
    [Fact]
    public void MetricsPublishAggregateWindowsWithExactOperationCounts()
    {
        var clock = new TestClock();
        using ServiceProvider provider = Build(clock);
        using IServiceScope game = provider.CreateScope();
        using IServiceScope other = provider.CreateScope();
        RenderGraphDiagnostics observer = game.ServiceProvider.GetRequiredService<RenderGraphDiagnostics>();
        DiagnosticTelemetry telemetry = game.ServiceProvider.GetRequiredService<DiagnosticTelemetry>();
        DiagnosticTelemetry unrelated = other.ServiceProvider.GetRequiredService<DiagnosticTelemetry>();
        IDiagnosticPump<BeforeRender> pump = game.ServiceProvider.GetRequiredService<IDiagnosticPump<BeforeRender>>();
        telemetry.Start();
        unrelated.Start();
        pump.Activate();
        for (int index = 0; index < 120; index++)
        {
            Compile(observer, "frame", index, record: true);
        }

        Assert.False(telemetry.TryRead(out _));
        clock.Advance(TimeSpan.FromSeconds(1));
        DiagnosticOperationResult summary = Dispatch(pump, "get-summary");
        Assert.Equal(120, summary.Values!["compiles"].Int64);
        Assert.Equal(120, summary.Values["records"].Int64);
        List<DiagnosticEvent> events = Drain(telemetry);
        Assert.Equal(12, events.Count);
        Assert.All(events, item => Assert.Equal("metric", item.Kind));
        Assert.Contains(events, item => item.Name == "rendergraph.operations" && item.Fields["operation"].String == "compile" && item.Value.Int64 == 120);
        Assert.Contains(events, item => item.Name == "rendergraph.operations" && item.Fields["operation"].String == "record" && item.Value.Int64 == 120);
        Assert.Contains(events, item => item.Name == "rendergraph.cpu.total" && item.Fields["operation"].String == "compile" && item.Value.Double == summary.Values["compile-milliseconds"].Double);
        Assert.Contains(events, item => item.Name == "rendergraph.cpu.window.max" && item.Fields["operation"].String == "record" && item.Value.Double == summary.Values["record-maximum-milliseconds"].Double);
        Assert.False(unrelated.TryRead(out _));
        Compile(observer, "next", record: true);
        clock.Advance(TimeSpan.FromMilliseconds(999));
        Dispatch(pump, "get-summary");
        Assert.False(telemetry.TryRead(out _));
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Dispatch(pump, "get-summary");
        Assert.Contains(Drain(telemetry), item => item.Name == "rendergraph.operations" && item.Value.Int64 == 1);
        Dispatch(pump, "get-summary");
        Assert.False(telemetry.TryRead(out _));
        pump.Deactivate();
    }

    /// <summary>Checks capture-only traces and throttled structured errors preserve exact aggregate failures.</summary>
    [Fact]
    public void CaptureSpansAndThrottledErrorsRemainCorrelatedToTheFailedRun()
    {
        var clock = new TestClock();
        using ServiceProvider provider = Build(clock);
        using IServiceScope game = provider.CreateScope();
        RenderGraphDiagnostics observer = game.ServiceProvider.GetRequiredService<RenderGraphDiagnostics>();
        DiagnosticTelemetry telemetry = game.ServiceProvider.GetRequiredService<DiagnosticTelemetry>();
        IDiagnosticPump<BeforeRender> pump = game.ServiceProvider.GetRequiredService<IDiagnosticPump<BeforeRender>>();
        telemetry.Start();
        pump.Activate();
        string id = Request(pump);
        Guid capturedRun = default;
        for (int index = 0; index < 4; index++)
        {
            var graph = new RenderGraph(name: "failure", observer: observer);
            graph.AddPass("throw", _ => throw new InvalidOperationException("Recording failure")).KeepAlive();
            using CompiledRenderGraph plan = graph.Compile(frameId: index);
            if (index == 0)
            {
                capturedRun = plan.Run.RunId;
            }

            using var commands = new RecordingCommands();
            Assert.Throws<InvalidOperationException>(() => plan.Record(commands));
            commands.Dispose();
        }

        List<DiagnosticEvent> events = Drain(telemetry);
        Assert.Equal(2, events.Count(item => item.Kind == "span"));
        DiagnosticEvent failure = Assert.Single(events, item => item.Kind == "log");
        Assert.Equal("Recording failure", failure.Fields["exception.message"].String);
        Assert.Equal(capturedRun.ToString("D"), failure.Fields["RunId"].String);
        Assert.Equal(0, failure.Fields["FrameId"].Int64);
        Assert.NotNull(failure.TraceId);
        Assert.Contains(events, item => item.Kind == "span" && item.Name == "record" && item.Value.String == "Error" && item.TraceId == failure.TraceId);
        Assert.Equal("record-failed", Capture(pump, id).Values!["state"].String);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(4, Dispatch(pump, "get-summary").Values!["failures"].Int64);
        Assert.Contains(Drain(telemetry), item => item.Name == "rendergraph.failures" && item.Value.Int64 == 4);
        pump.Deactivate();
    }

    /// <summary>Checks a caught failure in another graph does not mark the enclosing captured run as failed.</summary>
    [Fact]
    public void NestedUncapturedFailureDoesNotMarkTheCapturedActivityAsFailed()
    {
        using ServiceProvider provider = Build(new TestClock());
        using IServiceScope game = provider.CreateScope();
        RenderGraphDiagnostics observer = game.ServiceProvider.GetRequiredService<RenderGraphDiagnostics>();
        DiagnosticTelemetry telemetry = game.ServiceProvider.GetRequiredService<DiagnosticTelemetry>();
        IDiagnosticPump<BeforeRender> pump = game.ServiceProvider.GetRequiredService<IDiagnosticPump<BeforeRender>>();
        telemetry.Start();
        pump.Activate();
        string id = Request(pump, "outer");
        var nested = new RenderGraph(name: "nested", observer: observer);
        RenderGraphPass one = nested.AddPass("one", _ => { }).KeepAlive();
        RenderGraphPass two = nested.AddPass("two", _ => { }).KeepAlive();
        one.DependsOn(two);
        two.DependsOn(one);
        var graph = new RenderGraph(name: "outer", observer: observer);
        graph.AddPass("catch-nested-failure", _ => Assert.Throws<InvalidOperationException>(() => nested.Compile())).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new RecordingCommands();
        plan.Record(commands);
        commands.Complete();
        Assert.Equal("recorded", Capture(pump, id).Values!["state"].String);
        DiagnosticEvent recording = Assert.Single(Drain(telemetry), item => item.Kind == "span" && item.Name == "record");
        Assert.NotEqual("Error", recording.Value.String);
        pump.Deactivate();
    }

    /// <summary>Checks orderly shutdown publishes a short final window once.</summary>
    [Fact]
    public void DisposalPublishesTheFinalPartialWindowOnce()
    {
        using ServiceProvider provider = Build(new TestClock());
        using IServiceScope game = provider.CreateScope();
        RenderGraphDiagnostics observer = game.ServiceProvider.GetRequiredService<RenderGraphDiagnostics>();
        DiagnosticTelemetry telemetry = game.ServiceProvider.GetRequiredService<DiagnosticTelemetry>();
        telemetry.Start();
        Compile(observer, "last-frame", record: true);
        Assert.False(telemetry.TryRead(out _));
        observer.Dispose();
        Assert.Contains(Drain(telemetry), item => item.Name == "rendergraph.operations" && item.Value.Int64 == 1);
        observer.Dispose();
        Assert.False(telemetry.TryRead(out _));
        Assert.False(observer.IsEnabled);
    }

    /// <summary>Checks later configuration mutations cannot invalidate the observer's bounded storage.</summary>
    [Fact]
    public void ObserverCopiesValidatedOptions()
    {
        using ServiceProvider provider = Build(new TestClock());
        using IServiceScope game = provider.CreateScope();
        RenderGraphDiagnostics observer = game.ServiceProvider.GetRequiredService<RenderGraphDiagnostics>();
        RenderGraphDiagnosticOptions configuration = game.ServiceProvider.GetRequiredService<RenderGraphDiagnosticOptions>();
        configuration.MaxCaptures = 0;
        configuration.MaxSnapshotBytes = 0;
        configuration.MaxRetainedBytes = 0;
        configuration.Lifetime = TimeSpan.Zero;
        IDiagnosticPump<BeforeRender> pump = game.ServiceProvider.GetRequiredService<IDiagnosticPump<BeforeRender>>();
        pump.Activate();
        string id = Request(pump);
        Compile(observer, "valid", expectCapture: true);
        Assert.Equal("compiled", Capture(pump, id).Values!["state"].String);
        pump.Deactivate();
    }

    /// <summary>Checks invalid options are rejected before registering a contributor.</summary>
    [Fact]
    public void RegistrationRejectsInvalidBudgets()
    {
        Assert.Throws<ArgumentException>(() => Build(new TestClock(), configure: options => options.MaxCaptures = 0));
        Assert.Throws<ArgumentException>(() => Build(new TestClock(), configure: options => options.MaxSnapshotBytes = 0));
        Assert.Throws<ArgumentException>(() => Build(new TestClock(), configure: options => options.MaxRetainedBytes = 2048));
        Assert.Throws<ArgumentException>(() => Build(new TestClock(), configure: options => options.Lifetime = TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => Build(new TestClock(), configure: options => options.MetricInterval = TimeSpan.Zero));
    }

    private static ServiceProvider Build(TestClock clock, bool diagnosticsEnabled = true, bool adapterEnabled = true, Action<RenderGraphDiagnosticOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddLumyteDiagnostics(options =>
        {
            options.Enabled = diagnosticsEnabled;
            options.TraceSampleRatio = 1;
            options.AllowedMeterNames = [RenderGraphDiagnostics.TelemetryName];
            options.AllowedActivitySourceNames = [RenderGraphDiagnostics.TelemetryName];
            options.AllowedLogCategoryPrefixes = [RenderGraphDiagnostics.TelemetryName];
        });
        services.AddDiagnosticExecutionPoint<BeforeRender>("graphics.before-render");
        services.AddRenderGraphDiagnostics<BeforeRender>(configure: options =>
        {
            options.Enabled = adapterEnabled;
            configure?.Invoke(options);
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static void Compile(RenderGraphDiagnostics observer, string name, long frameId = 0, bool record = false, bool expectCapture = false)
    {
        var graph = new RenderGraph(name: name, observer: observer);
        graph.AddPass("draw", _ => { }).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile(frameId);
        Assert.Equal(expectCapture, plan.Snapshot is not null);
        if (record)
        {
            using var commands = new RecordingCommands();
            plan.Record(commands);
            commands.Complete();
        }
    }

    private static string Request(IDiagnosticPump<BeforeRender> pump, string name = "")
    {
        DiagnosticOperationResult result = Dispatch(pump, "request-capture", new() { ["graph-name"] = DiagnosticValue.From(name) });
        Assert.Equal("success", result.Status);
        return result.Values!["capture-id"].String!;
    }

    private static DiagnosticOperationResult Capture(IDiagnosticPump<BeforeRender> pump, string id)
        => Dispatch(pump, "get-capture", new() { ["capture-id"] = DiagnosticValue.From(id) });

    private static Dictionary<string, DiagnosticValue> Details(string id, long index = 0)
        => new() { ["capture-id"] = DiagnosticValue.From(id), ["index"] = DiagnosticValue.From(index) };

    private static List<DiagnosticEvent> Drain(DiagnosticTelemetry telemetry)
    {
        var events = new List<DiagnosticEvent>();
        while (telemetry.TryRead(out DiagnosticEvent? item))
        {
            events.Add(item!);
        }

        return events;
    }

    private static DiagnosticOperationResult Dispatch(IDiagnosticPump<BeforeRender> pump, string operation, Dictionary<string, DiagnosticValue>? arguments = null, bool observe = true)
    {
        var request = new DiagnosticRequest("graphics.render-graph", operation, new(Guid.NewGuid(), Guid.NewGuid(), default, "developer", null, default), arguments ?? [], observe ? new HashSet<DiagnosticPermission> { DiagnosticPermission.Observe } : [], long.MaxValue);
        Task<DiagnosticOperationResult> pending = pump.SubmitAsync(request);
        pump.Pump(default, new(TimeSpan.FromSeconds(1), 8));
        Assert.True(pending.IsCompleted);
        return pending.GetAwaiter().GetResult();
    }

    private sealed class BeforeRender;

    private sealed class TestClock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan interval) => _ticks += interval.Ticks;
    }
}
