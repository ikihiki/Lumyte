using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Mathematics.Colors;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Ensures built-in pass splitting accounts for complete paint-graph resource reachability.</summary>
public sealed class GraphPassCapacityTests
{
    /// <summary>Distance-field atlas and child images count toward pass-wide admission before recording.</summary>
    /// <param name="imageChild">Whether distinct images live beneath a shared distance-field atlas node.</param>
    /// <returns>The graph preparation and recording completion.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DistanceFieldPaintSplitsAtAdmittedCommandBoundariesAsync(bool imageChild)
    {
        uint capacity = imageChild ? 3u : 2u;
        var device = new ProfilingDevice { Caps = new() { ArgumentTableBindingLimits = new() { TextureSlots = capacity, TexturesPerDraw = capacity } } };
        using var graph = new Graph(device);
        using var atlasA = new DispatchTexture { Format = TextureFormat.Rgba8Unorm };
        using var atlasB = new DispatchTexture { Format = TextureFormat.Rgba8Unorm };
        using var childA = new DispatchTexture { Format = TextureFormat.Rgba8Unorm };
        using var childB = new DispatchTexture { Format = TextureFormat.Rgba8Unorm };
        using var targetTexture = new DispatchTexture();
        using IGraphicsTextureView a = atlasA.CreateView();
        using IGraphicsTextureView b = atlasB.CreateView();
        using IGraphicsTextureView x = childA.CreateView();
        using IGraphicsTextureView y = childB.CreateView();
        var bindings = new DrawingGraphBindings2D(graph);
        BarrierScope read = new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead);
        foreach (IGraphicsTexture texture in new IGraphicsTexture[] { atlasA, atlasB, childA, childB })
        {
            bindings.BindTexture(texture, graph.ImportTexture(texture, TextureState.Sampled, read));
        }

        var ink = PaintGraph2D.Solid(new SRgba<float>(1, 0, 0, 1));
        var first = PaintGraph2D.DistanceField(new() { View = a, Source = new(0, 0, 16, 16) }, 4, imageChild ? PaintGraph2D.ImagePattern(new() { View = x, Source = new(0, 0, 16, 16) }) : ink);
        var second = PaintGraph2D.DistanceField(new() { View = imageChild ? a : b, Source = new(0, 0, 16, 16) }, 4, imageChild ? PaintGraph2D.ImagePattern(new() { View = y, Source = new(0, 0, 16, 16) }) : ink);
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        canvas.FillCircle(new Vector2(4, 8), 3, first);
        canvas.FillCircle(new Vector2(12, 8), 3, second);
        RenderGraphTexture target = graph.ImportTexture(targetTexture, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite));
        using var renderer = new RenderGraphRenderer2D(
            DrawingCommandRenderers2D.CreateDefault(),
            canvas.End(),
            new() { Texture = target, Description = new() { Width = 16, Height = 16, Format = TextureFormat.Rgba8Srgb, Usage = targetTexture.Usage }, LoadOp = AttachmentLoadOp.Clear },
            bindings);
        using PreparedDrawingPasses2D prepared = await graph.Append2D(renderer);
        using CompiledRenderGraph plan = graph.Compile();
        Assert.Equal(2, plan.PassNames.Count(name => name == "drawing2d.batch"));
        using var commands = new ProfilingCommands();
        plan.Record(commands);
        Assert.Equal(2, device.Tables.Count);
        Assert.All(device.Tables, table => Assert.Equal(capacity, table.TextureCapacity));
        commands.Dispose();
    }

    /// <summary>Fixed descriptor floors and helper/color reservations participate in append-time segmentation.</summary>
    /// <returns>The graph preparation and recording completion.</returns>
    [Fact]
    public async Task DescriptorFloorsAndReservationsLimitTheWholePassAsync()
    {
        var device = new ProfilingDevice
        {
            Caps = new()
            {
                ArgumentTableBindingLimits = new()
                {
                    MinimumTextureBindings = 4,
                    MinimumSamplerBindings = 2,
                    ReservedBindings = 1,
                    ColorAttachmentBindingWeight = 1,
                    CombinedBindings = 10,
                },
            },
        };
        using var graph = new Graph(device);
        using var targetTexture = new DispatchTexture();
        RenderGraphTexture target = graph.ImportTexture(targetTexture, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite));
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        canvas.FillCircle(new Vector2(4, 8), 3, new SRgba<float>(1, 0, 0, 1));
        canvas.FillCircle(new Vector2(12, 8), 3, new SRgba<float>(0, 1, 0, 1));
        using var renderer = new RenderGraphRenderer2D(
            DrawingCommandRenderers2D.CreateDefault(),
            canvas.End(),
            new() { Texture = target, Description = new() { Width = 16, Height = 16, Format = TextureFormat.Rgba8Srgb, Usage = targetTexture.Usage }, LoadOp = AttachmentLoadOp.Clear },
            new DrawingGraphBindings2D(graph));
        using PreparedDrawingPasses2D prepared = await graph.Append2D(renderer);
        using CompiledRenderGraph plan = graph.Compile();
        Assert.Equal(2, plan.PassNames.Count(name => name == "drawing2d.batch"));
        using var commands = new ProfilingCommands();
        plan.Record(commands);
        Assert.Single(device.Tables);
        Assert.Equal(4u, device.Tables[0].BufferCapacity);
        commands.Dispose();
    }
}
