using System.Diagnostics;
using Lumyte.Diagnostics;
using Lumyte.Diagnostics.RenderGraph;
using Lumyte.Graphics.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Graphics.RenderGraph.Tests;

/// <summary>Verifies graph inspection and telemetry through the existing diagnostic system.</summary>
public sealed class RenderGraphDiagnosticsTests
{
    /// <summary>Checks the registered pump shares the observer, enforces Observe, and preserves snapshot revisions.</summary>
    [Fact]
    public void PumpExposesDetachedSnapshotsWithPermissionsAndRevisions()
    {
        using ServiceProvider provider = Build();
        using IServiceScope game = provider.CreateScope();
        using IServiceScope other = provider.CreateScope();
        RenderGraphDiagnostics observer = game.ServiceProvider.GetRequiredService<RenderGraphDiagnostics>();
        Assert.NotSame(observer, other.ServiceProvider.GetRequiredService<RenderGraphDiagnostics>());
        IDiagnosticPump<BeforeRender> pump = game.ServiceProvider.GetRequiredService<IDiagnosticPump<BeforeRender>>();
        pump.Activate();
        Assert.Equal("graphics.render-graph", Assert.Single(pump.Catalog).Subsystem.Id);
        Assert.All(pump.Catalog[0].Operations, operation => Assert.Equal(DiagnosticPermission.Observe, operation.RequiredPermission));
        Assert.Equal("not-ready", Dispatch(pump, "get-summary").Code);
        var graph = new RenderGraph(name: "frame", observer: observer);
        graph.AddPass("draw", _ => { }).KeepAlive();
        graph.AddPass("unused", _ => { });
        using CompiledRenderGraph plan = graph.Compile();
        DiagnosticOperationResult summary = Dispatch(pump, "get-summary");
        Assert.Equal("success", summary.Status);
        Assert.Equal("frame", summary.Values!["name"].String);
        Assert.Equal(1, summary.Values["live-passes"].Int64);
        Assert.Equal(1, summary.Values["culled-passes"].Int64);
        var argument = new Dictionary<string, DiagnosticValue> { ["index"] = DiagnosticValue.From(1L) };
        DiagnosticOperationResult pass = Dispatch(pump, "get-pass", argument, summary.Revision);
        Assert.Equal("unused", pass.Values!["name"].String);
        Assert.Equal(-1, pass.Values["execution-index"].Int64);
        Assert.Equal("forbidden", Dispatch(pump, "get-summary", observe: false).Code);
        Assert.Equal("invalid-arguments", Dispatch(pump, "get-pass", new() { ["index"] = DiagnosticValue.From(-1L) }, summary.Revision).Code);
        using var commands = new RecordingCommands();
        plan.Record(commands);
        commands.Complete();
        plan.Dispose();
        Assert.Equal("conflict", Dispatch(pump, "get-pass", argument, summary.Revision).Status);
        summary = Dispatch(pump, "get-summary");
        Assert.Equal("recorded", summary.Values!["status"].String);
        Assert.Equal("not-found", Dispatch(pump, "get-pass", new() { ["index"] = DiagnosticValue.From(long.MaxValue) }, summary.Revision).Code);
        Assert.Equal("success", Dispatch(pump, "get-pass", argument, summary.Revision).Status);
        pump.Deactivate();
    }

    /// <summary>Checks compile, record and failure instrumentation is correlated to one game scope.</summary>
    [Fact]
    public void ExistingTelemetryCollectsCpuMetricsSpansAndStructuredFailures()
    {
        using ServiceProvider provider = Build();
        using IServiceScope game = provider.CreateScope();
        using IServiceScope other = provider.CreateScope();
        DiagnosticTelemetry collector = game.ServiceProvider.GetRequiredService<DiagnosticTelemetry>();
        DiagnosticTelemetry unrelated = other.ServiceProvider.GetRequiredService<DiagnosticTelemetry>();
        collector.Start();
        unrelated.Start();
        RenderGraphDiagnostics observer = game.ServiceProvider.GetRequiredService<RenderGraphDiagnostics>();
        IDiagnosticPump<BeforeRender> pump = game.ServiceProvider.GetRequiredService<IDiagnosticPump<BeforeRender>>();
        pump.Activate();
        var graph = new RenderGraph(observer: observer);
        graph.AddPass("throw", _ => throw new InvalidOperationException("Recording failure")).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new RecordingCommands();
        Assert.Throws<InvalidOperationException>(() => plan.Record(commands));
        commands.Dispose();
        var events = new List<DiagnosticEvent>();
        while (collector.TryRead(out DiagnosticEvent? item))
        {
            events.Add(item!);
        }

        Assert.False(unrelated.TryRead(out _));
        Assert.Contains(events, item => item.Kind == "metric" && item.Name == "rendergraph.cpu.duration" && item.Fields["operation"].String == "compile");
        Assert.Contains(events, item => item.Kind == "metric" && item.Name == "rendergraph.failures" && item.Value.Int64 == 1);
        Assert.Contains(events, item => item.Kind == "span" && item.Name == "compile");
        DiagnosticEvent failure = Assert.Single(events, item => item.Kind == "log" && item.Fields.ContainsKey("exception.message"));
        Assert.Equal("Recording failure", failure.Fields["exception.message"].String);
        Assert.NotNull(failure.TraceId);
        Assert.Contains(events, item => item.Kind == "span" && item.Name == "record" && item.TraceId == failure.TraceId);
        DiagnosticOperationResult summary = Dispatch(pump, "get-summary");
        Assert.Equal("record-failed", summary.Values!["status"].String);
        pump.Deactivate();
    }

    /// <summary>Checks scalar diagnostic responses preserve graph resource, dependency and barrier indices.</summary>
    [Fact]
    public void PumpReportsResourceLifetimeDependenciesAndPlannedBarriers()
    {
        using ServiceProvider provider = Build();
        using IServiceScope game = provider.CreateScope();
        RenderGraphDiagnostics observer = game.ServiceProvider.GetRequiredService<RenderGraphDiagnostics>();
        IDiagnosticPump<BeforeRender> pump = game.ServiceProvider.GetRequiredService<IDiagnosticPump<BeforeRender>>();
        pump.Activate();
        var device = new TestDevice();
        var graph = new RenderGraph(device, observer: observer);
        RenderGraphBuffer<uint> temporary = graph.CreateBuffer<uint>(new() { Count = 4, Usage = BufferUsage.CopyDestination | BufferUsage.CopySource }, "temporary");
        using var external = new TestBuffer();
        RenderGraphBuffer<uint> output = graph.ImportBuffer(external, default);
        graph.AddPass("produce", _ => { }).UseBuffer(temporary, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        graph.AddPass("consume", _ => { }).UseBuffer(temporary, new(PipelineStage.Copy, ResourceAccess.CopyRead)).UseBuffer(output, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        using CompiledRenderGraph plan = graph.Compile();
        long? revision = Dispatch(pump, "get-summary").Revision;
        var argument = new Dictionary<string, DiagnosticValue> { ["index"] = DiagnosticValue.From(0L) };
        DiagnosticOperationResult resource = Dispatch(pump, "get-resource", argument, revision);
        Assert.Equal("temporary", resource.Values!["name"].String);
        Assert.True(resource.Values["transient"].Boolean);
        Assert.Equal(0, resource.Values["first-pass"].Int64);
        Assert.Equal(1, resource.Values["last-pass"].Int64);
        DiagnosticOperationResult dependency = Dispatch(pump, "get-dependency", argument, revision);
        Assert.Equal(0, dependency.Values!["before"].Int64);
        Assert.Equal(1, dependency.Values["after"].Int64);
        DiagnosticOperationResult barrier = Dispatch(pump, "get-barrier", argument, revision);
        Assert.Equal(1, barrier.Values!["pass-index"].Int64);
        Assert.Equal(0, barrier.Values["resource-index"].Int64);
        Assert.Equal((long)ResourceAccess.CopyWrite, barrier.Values["before-access"].Int64);
        Assert.Equal((long)ResourceAccess.CopyRead, barrier.Values["after-access"].Int64);
        Assert.Equal(0, device.Buffers);
        pump.Deactivate();
    }

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLumyteDiagnostics(options =>
        {
            options.Enabled = true;
            options.TraceSampleRatio = 1;
            options.AllowedMeterNames = [RenderGraphDiagnostics.TelemetryName];
            options.AllowedActivitySourceNames = [RenderGraphDiagnostics.TelemetryName];
            options.AllowedLogCategoryPrefixes = [RenderGraphDiagnostics.TelemetryName];
        });
        services.AddDiagnosticExecutionPoint<BeforeRender>("graphics.before-render");
        services.AddRenderGraphDiagnostics<BeforeRender>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static DiagnosticOperationResult Dispatch(IDiagnosticPump<BeforeRender> pump, string operation, Dictionary<string, DiagnosticValue>? arguments = null, long? revision = null, bool observe = true)
    {
        var request = new DiagnosticRequest("graphics.render-graph", operation, new(Guid.NewGuid(), Guid.NewGuid(), default, "developer", revision, default), arguments ?? [], observe ? new HashSet<DiagnosticPermission> { DiagnosticPermission.Observe } : [], Stopwatch.GetTimestamp() + (10 * Stopwatch.Frequency));
        Task<DiagnosticOperationResult> pending = pump.SubmitAsync(request);
        pump.Pump(default, new(TimeSpan.FromSeconds(1), 8));
        Assert.True(pending.IsCompleted);
        return pending.GetAwaiter().GetResult();
    }

    private sealed class BeforeRender;
}
