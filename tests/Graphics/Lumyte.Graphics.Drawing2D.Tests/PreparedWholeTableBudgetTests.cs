using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Prepared atlas draws admit the complete registered table, not only each root.</summary>
public sealed class PreparedWholeTableBudgetTests
{
    /// <summary>Graph-created ranges are counted only after their actual allocations exist.</summary>
    [Fact]
    public void LateGraphRangesPartitionBeforePublishingReferences()
    {
        var device = new ProfilingDevice
        {
            Caps = new() { ArgumentTableBindingLimits = new() { BufferSlots = 1024, BuffersPerDraw = 3 } },
        };
        using var graph = new Graph(device);
        RenderGraphBuffer<uint> buffer = graph.CreateBuffer(new BufferDesc<uint> { Count = 32, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead });
        BarrierScope read = new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead);
        graph.AddPass("produce", _ => { }).UseBuffer(buffer, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        var references = new IGpuRef<uint>?[8];
        for (int i = 0; i < references.Length; i++)
        {
            int index = i;
            ShaderResources resources = graph.CreatePreparedPayload();
            graph.ConnectPreparedBuffer(resources, buffer, (ulong)i * 2, 2, reference => references[index] = reference);
            graph.AddPass($"consume-{i}", _ => { }).UseBuffer(buffer, read).KeepAlive().AttachPreparedPayload(resources);
        }

        using CompiledRenderGraph compiled = graph.Compile();
        Assert.Empty(device.Tables);
        Assert.All(references, reference => Assert.Null(reference));
        using var commands = new ProfilingCommands();
        compiled.Record(commands);

        // Each pass also registers the declared full buffer range in its core.
        Assert.Equal(new uint[] { 3, 3, 3, 3 }, device.Tables.Select(table => table.BufferCapacity));
        Assert.All(references, reference => Assert.Equal(2ul, reference!.Count));
        Assert.Same(ProfilingDevice.Owner(references[0]!), ProfilingDevice.Owner(references[1]!));
        Assert.NotSame(ProfilingDevice.Owner(references[1]!), ProfilingDevice.Owner(references[2]!));
        Assert.NotSame(references[0], references[1]);
        commands.Dispose();
    }

    /// <summary>Many image commands share an atlas but own distinct instruction and geometry ranges.</summary>
    /// <returns>The asynchronous preparation completion.</returns>
    [Fact]
    public async Task SharedAtlasCommandsPartitionAllRegisteredBuffersAsync()
    {
        var device = new ProfilingDevice
        {
            Caps = new()
            {
                ShaderTarget = ShaderTarget.Wgsl,
                MaxBufferSize = 1UL << 30,
                ArgumentTableBindingLimits = new()
                {
                    BufferSlots = 1024,
                    BuffersPerDraw = 7,
                    TexturesPerDraw = 16,
                    SamplersPerDraw = 16,
                },
            },
        };
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var target = new DispatchTexture();
        using var atlas = new DispatchTexture();
        using IGraphicsTextureView targetView = target.CreateView();
        using IGraphicsTextureView atlasView = atlas.CreateView();
        using var sampler = new DispatchSampler();
        var image = new Image2D(atlasView, sampler);
        var canvas = new Canvas2D();
        canvas.Begin();
        for (int i = 0; i < 24; i++)
        {
            canvas.Save();
            canvas.ClipRect(new(0, 0, 16, 16));
            canvas.DrawImage(image, new(0, 0, 8, 8), new(i % 8, i % 8, 8, 8));
            canvas.Restore();
        }

        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = targetView });
        using var commands = new ProfilingCommands();
        prepared.Record(commands);
        DrawingArguments[] roots = commands.Events.OfType<DrawingArguments>().ToArray();
        Assert.Equal(24, roots.Length);
        Assert.Equal(24, device.Tables.Count);
        Assert.All(device.Tables, table =>
        {
            Assert.Equal(2u, table.BufferCapacity);
            Assert.True(table.BufferCapacity <= device.Caps.ArgumentTableBindingLimits.BuffersPerDraw);
            Assert.Equal(2u, table.TextureCapacity);
            Assert.Equal(1u, table.SamplerCapacity);
        });
        Assert.Equal(24, roots.Select(root => ProfilingDevice.Owner(root.Instructions)).Distinct().Count());
        Assert.All(roots, root =>
        {
            Assert.Same(ProfilingDevice.Owner(root.Instructions), ProfilingDevice.Owner(root.Data));
            Assert.Equal((ulong)root.Count, root.Instructions.Count);
            Assert.True(root.Data.Count > 0);
        });
        Assert.All(device.Instructions, instruction =>
            Assert.Same(ProfilingDevice.Owner(instruction.Texture), ProfilingDevice.Owner(instruction.Sampler)));
    }
}
