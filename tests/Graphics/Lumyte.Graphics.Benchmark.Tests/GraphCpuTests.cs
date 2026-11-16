using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Benchmarks.Testing;
using Lumyte.Graphics.ImageEffects;
using Lumyte.Graphics.ImageEffects.Benchmark.Preparation;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.RenderGraph.Benchmark.Compilation;
using Lumyte.Graphics.RenderGraph.Benchmark.DrawingBridge;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Benchmark.Tests;

/// <summary>Deterministic production graph compilation, command and lifetime oracles without a GPU.</summary>
public sealed class GraphCpuTests
{
    /// <summary>Every live copy executes in stable topological order and dead branches allocate nothing.</summary>
    /// <param name="shape">The dependency and alias shape.</param>
    /// <param name="passCount">The bounded live copy count.</param>
    [Theory]
    [InlineData(GraphShape.Chain, 8)]
    [InlineData(GraphShape.Chain, 32)]
    [InlineData(GraphShape.FanOutJoin, 8)]
    [InlineData(GraphShape.FanOutJoin, 32)]
    [InlineData(GraphShape.ImportedAlias, 8)]
    [InlineData(GraphShape.ImportedAlias, 32)]
    public void GraphRecordsLiveCopiesAndPreservesBorrowedOwnership(GraphShape shape, int passCount)
    {
        using var device = new CpuGraphicsDevice();
        using IGraphicsBuffer<uint> source = device.CreateBuffer<uint>(new() { Count = 64, Usage = BufferUsage.CopySource | BufferUsage.CopyDestination });
        long borrowed = device.Counters.LiveResources;
        var order = new List<int>();
        device.ResetCounters();
        using (Graph graph = GraphWorkloads.Build(device, source, passCount, shape, order))
        using (CompiledRenderGraph plan = graph.Compile())
        {
            Assert.Equal(shape == GraphShape.FanOutJoin ? passCount + 1 : passCount, plan.Statistics.LivePasses);
            Assert.Equal(new[] { "dead" }, plan.CulledPassNames);
            Assert.Equal(0, device.Counters.BufferCreates);
            using (IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new()))
            {
                plan.Record(commands);
                Assert.Throws<InvalidOperationException>(() => plan.Record(commands));
                commands.Finish();
            }

            Assert.Equal(Enumerable.Range(0, shape == GraphShape.FanOutJoin ? passCount + 1 : passCount), order);
            Assert.Equal(passCount, device.Counters.Copies);
            Assert.Equal(shape == GraphShape.ImportedAlias ? 0 : passCount, device.Counters.BufferCreates);
            Assert.True(device.Counters.BarrierCalls > 0);
            Assert.Equal(0, device.Counters.Submissions);
        }

        Assert.Equal(borrowed, device.Counters.LiveResources);
        Assert.Equal(64UL, source.Count);
    }

    /// <summary>Built-in effects prepare and record nonempty stages; shared DAG nodes expand only once.</summary>
    /// <param name="scenario">The built-in effect DAG.</param>
    /// <param name="passes">The exact expected fullscreen pass count.</param>
    [Theory]
    [InlineData(EffectScenario.Blur, 2)]
    [InlineData(EffectScenario.BlurComposite, 4)]
    [InlineData(EffectScenario.SharedBranch, 5)]
    public void BuiltInEffectsRecordFreshStagesAndReleaseAllOwners(EffectScenario scenario, int passes)
    {
        using var device = new CpuGraphicsDevice();
        using IGraphicsTexture source = device.CreateTexture(EffectWorkloads.SourceDescription());
        ImageEffectGraph definition = EffectWorkloads.Definition(scenario);
        for (int iteration = 0; iteration < 2; iteration++)
        {
            device.ResetCounters();
            using (Graph graph = EffectWorkloads.Build(device, source, definition))
            using (CompiledRenderGraph plan = graph.Compile())
            {
                Assert.Equal(passes, plan.Statistics.LivePasses);
                Assert.Empty(plan.CulledPassNames);
                Assert.Equal(passes * 2, device.Counters.ShaderCreates);
                Assert.Equal(passes, device.Counters.PipelineCreates);
                Assert.Equal(0, device.Counters.TextureCreates);
                using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
                plan.Record(commands);
                commands.Finish();
                Assert.Equal(passes, device.Counters.TextureCreates);
                Assert.Equal(passes, device.Counters.RenderPasses);
                Assert.Equal(passes, device.Counters.Draws);
                Assert.Equal(0, device.Counters.UploadedBytes);
                Assert.NotEqual(0UL, device.Counters.Checksum);
                Assert.Equal(0, device.Counters.Submissions);
            }

            Assert.Equal(1, device.Counters.LiveResources);
        }
    }

    /// <summary>Repeated view requests share the plan cache; separate plans own separate temporary allocations.</summary>
    [Fact]
    public void ViewsAreCachedOnlyWithinTheirOwningPlan()
    {
        using var device = new CpuGraphicsDevice();
        using var graph = new Graph(device);
        RenderGraphTexture output = graph.CreateTexture(new() { Width = 8, Height = 8, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment });
        var views = new List<IGraphicsTextureView>();
        graph.AddPass("clear", context =>
        {
            views.Add(context.CreateTextureView(output));
            views.Add(context.CreateTextureView(output));
            IRenderEncoder render = context.Commands.BeginRenderPass(new() { ColorAttachments = [new() { View = views[^1], LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }] });
            render.End();
        }).UseTexture(output, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite));
        graph.MarkOutput(output);
        for (int i = 0; i < 2; i++)
        {
            using (CompiledRenderGraph plan = graph.Compile())
            {
                using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
                plan.Record(commands);
                commands.Finish();
                Assert.Same(views[i * 2], views[(i * 2) + 1]);
            }

            Assert.Equal(0, device.Counters.LiveResources);
        }

        Assert.NotSame(views[0], views[2]);
        Assert.Equal(2, device.Counters.TextureCreates);
        Assert.Equal(2, device.Counters.RenderPasses);
    }

    /// <summary>The public graph bridge really records Drawing2D batches on every fresh invocation.</summary>
    /// <param name="commandCount">The bounded drawing count.</param>
    /// <returns>The asynchronous graph preparation completion.</returns>
    [Theory]
    [InlineData(8)]
    [InlineData(64)]
    public async Task DrawingBridgeRecordsAndCleansUpRepeatedlyAsync(int commandCount)
    {
        var benchmark = new DrawingBridgeBenchmarks { CommandCount = commandCount };
        benchmark.Setup();
        try
        {
            for (int i = 0; i < 2; i++)
            {
                CpuGraphicsCounters counters = await benchmark.AppendCompileAllocateRecordCleanupAsync();
                Assert.True(counters.Draws > 0);
                Assert.True(counters.UploadedBytes > 0);
                Assert.True(counters.TextureCreates > 0);
                Assert.Equal(0, counters.Submissions);
                Assert.Equal(0, counters.LiveResources);
            }
        }
        finally
        {
            benchmark.Cleanup();
        }
    }
}
