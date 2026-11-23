using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Pipelines.RenderGraph.Tests;

/// <summary>Checks actual native copies and transactional dependency/lifetime integration.</summary>
public sealed class NativeAdapterTests
{
    /// <summary>The graph retains an independent native plan use after all original owners return.</summary>
    [NativeFact]
    [Trait("Execution", "Gpu")]
    public void NativeCopyUsesExactImportedRangesAndCoarseLifetime()
    {
        using var fixture = new NativeCopyFixture();
        using var graph = new Graph(fixture.Device);
        PipelineGraphBindings bindings = fixture.Bind(graph);
        graph.AddPipelinePass("copy", fixture.Plan, bindings);
        using CompiledRenderGraph compiled = graph.Compile();
        Assert.Equal(1, compiled.Statistics.LivePasses);
        fixture.ReturnOriginalOwners();
        Assert.Equal(2, fixture.Leases.Active);
        fixture.Execute(compiled);
        fixture.VerifyCopiedBytes();
        Assert.Equal(2, fixture.Leases.Active);
        compiled.Dispose();
        Assert.Equal(0, fixture.Leases.Active);
        fixture.VerifyCopiedBytes();
    }

    /// <summary>Missing entries reject before claim or graph mutation and leave the plan reusable.</summary>
    [NativeFact]
    [Trait("Execution", "Gpu")]
    public void MissingBindingsLeaveOriginalPlanReusable()
    {
        using var fixture = new NativeCopyFixture();
        using var graph = new Graph(fixture.Device);
        PipelineGraphBindings valid = fixture.Bind(graph);
        Assert.Throws<ArgumentException>(() => graph.AddPipelinePass("bad", fixture.Plan, new PipelineGraphBindings([], [])));
        graph.AddPipelinePass("copy", fixture.Plan, valid);
        using CompiledRenderGraph compiled = graph.Compile();
        Assert.Equal(1, compiled.Statistics.LivePasses);
        fixture.Execute(compiled);
        fixture.VerifyCopiedBytes();
    }

    /// <summary>A duplicate opaque key cannot stand in for another required slot.</summary>
    [NativeFact]
    [Trait("Execution", "Gpu")]
    public void DuplicateSlotsRejectBeforeClaim()
    {
        using var fixture = new NativeCopyFixture();
        using var graph = new Graph(fixture.Device);
        PipelineGraphBindings valid = fixture.Bind(graph);
        var duplicate = new PipelineGraphBindings([], [valid.Buffers[0], valid.Buffers[0]]);
        Assert.Throws<ArgumentException>(() => graph.AddPipelinePass("bad", fixture.Plan, duplicate));
        graph.AddPipelinePass("copy", fixture.Plan, valid);
        using CompiledRenderGraph compiled = graph.Compile();
        Assert.Equal(1, compiled.Statistics.LivePasses);
        fixture.Execute(compiled);
        fixture.VerifyCopiedBytes();
    }

    /// <summary>Each fallible append boundary rolls graph state and the exclusive recording claim back.</summary>
    [NativeFact]
    [Trait("Execution", "Gpu")]
    public void AppendFailureRollsBackAllThreeBoundaries()
    {
        foreach (int boundary in new[] { 0, 1, 2 })
        {
            using var fixture = new NativeCopyFixture();
            using var graph = new Graph(fixture.Device);
            PipelineGraphBindings bindings = fixture.Bind(graph);
            Assert.Throws<InjectedAppendException>(() => PipelineGraphExtensions.AddPipelinePassCore(graph, "bad", fixture.Plan, bindings, stage =>
            {
                if (stage == boundary)
                {
                    throw new InjectedAppendException();
                }
            }));
            Assert.Equal(2, fixture.Leases.Active);
            graph.AddPipelinePass("copy", fixture.Plan, bindings);
            using CompiledRenderGraph compiled = graph.Compile();
            Assert.Equal(1, compiled.Statistics.LivePasses);
            fixture.Execute(compiled);
            fixture.VerifyCopiedBytes();
        }
    }

    /// <summary>Graph ownership excludes a second graph registration and direct recording.</summary>
    [NativeFact]
    [Trait("Execution", "Gpu")]
    public void CommittedClaimExcludesOtherRecorders()
    {
        using var fixture = new NativeCopyFixture();
        using var graph = new Graph(fixture.Device);
        PipelineGraphBindings bindings = fixture.Bind(graph);
        graph.AddPipelinePass("copy", fixture.Plan, bindings);
        Assert.Throws<InvalidOperationException>(() => graph.AddPipelinePass("duplicate", fixture.Plan, bindings));
        using IGraphicsCommandBuffer direct = fixture.Device.CreateCommandBuffer(new CommandBufferDesc());
        Assert.Throws<InvalidOperationException>(() => fixture.Plan.Record(direct));
        using CompiledRenderGraph compiled = graph.Compile();
        Assert.Equal(1, compiled.Statistics.LivePasses);
        fixture.Execute(compiled);
        fixture.VerifyCopiedBytes();
    }

    /// <summary>Caller mutation cannot add a binding after the immutable holder is constructed.</summary>
    [NativeFact]
    [Trait("Execution", "Gpu")]
    public void BindingListsAreCaptured()
    {
        using var fixture = new NativeCopyFixture();
        using var graph = new Graph(fixture.Device);
        PipelineGraphBindings original = fixture.Bind(graph);
        PipelineGraphBufferBinding[] mutable = original.Buffers.ToArray();
        var bindings = new PipelineGraphBindings([], mutable);
        mutable[1] = mutable[0];
        graph.AddPipelinePass("copy", fixture.Plan, bindings);
        using CompiledRenderGraph compiled = graph.Compile();
        fixture.Execute(compiled);
        fixture.VerifyCopiedBytes();
    }

    /// <summary>The adapter is independent of Drawing2D and exposes no render-pass-management scope.</summary>
    [Fact]
    public void AdapterHasNoDrawing2DDependency()
        => Assert.DoesNotContain(typeof(PipelineGraphBindings).Assembly.GetReferencedAssemblies(), reference => reference.Name!.Contains("Drawing2D", StringComparison.Ordinal));

    private sealed class InjectedAppendException : Exception;
}
