using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.ImageEffects;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks the pure drawing dependency boundary and delayed effect callback ownership.</summary>
public sealed class CanvasSeparationTests
{
    /// <summary>Checks pure drawing loads neither RenderGraph nor ImageEffects and exposes no layer operations.</summary>
    [Fact]
    public void PureDrawingHasNoGraphDependenciesOrLayerApi()
    {
        string?[] dependencies = typeof(Canvas2D).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToArray();
        Assert.DoesNotContain("Lumyte.Graphics.RenderGraph", dependencies);
        Assert.DoesNotContain("Lumyte.Graphics.ImageEffects", dependencies);
        Assert.Null(typeof(Canvas2D).GetMethod("BeginLayer"));
        Assert.Null(typeof(Canvas2D).GetMethod("DrawBackdropEffect"));
        Assert.NotNull(typeof(RenderGraphCanvas2D).GetMethod("BeginLayer"));
        Assert.NotEqual(typeof(DrawList2D), typeof(RenderGraphDrawList2D));
    }

    /// <summary>Checks callback invocation is delayed until Append2D and failure releases all callback resources.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task EffectCallbackUsesParentAndFailureReleasesResourcesAsync()
    {
        var device = new DispatchDevice();
        var parent = new Graph(device);
        var resource = new TrackedResource();
        var strategies = new DispatchRenderers();
        int calls = 0;
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        canvas.BeginLayer(new()
        {
            ContentBounds = new(2, 3, 4, 5),
            Effects = graph =>
            {
                calls++;
                Assert.Same(parent, graph.Graph);
                Assert.Equal(new EffectPixelRect(2, 3, 4, 5), graph.Source.Bounds);
                _ = graph.AppendEffect(new OwnedEffect(resource), graph.Source);
                throw new InvalidOperationException("The user callback failed.");
            },
        });
        canvas.FillRect(new(2, 3, 4, 5), new Color2D(1, 0, 0, 1));
        canvas.EndLayer();
        RenderGraphDrawList2D list = canvas.End();
        Assert.Equal(0, calls);
        var desc = new TextureDesc { Width = 16, Height = 16, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled };
        var target = new RenderTargetBinding2D { Texture = parent.CreateTexture(desc), Description = desc };
        using var renderer = new RenderGraphRenderer2D(strategies.CreateTable(), list, target, new DrawingGraphBindings2D(parent));
        await Assert.ThrowsAsync<InvalidOperationException>(() => parent.Append2D(renderer).AsTask());
        Assert.Equal(1, calls);
        Assert.Equal(1, resource.Disposals);
        Assert.Equal(1, Assert.Single(strategies.Batches).DisposeCount);
    }

    private sealed record OwnedEffect(TrackedResource Resource) : ImageEffectDefinition
    {
        /// <inheritdoc/>
        public override int InputCount => 1;

        /// <inheritdoc/>
        public override EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs) => inputs[0];

        /// <inheritdoc/>
        public override EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output) => output;

        /// <inheritdoc/>
        public override EffectImage AppendPasses(EffectPreparationContext context, Graph graph, ReadOnlySpan<EffectImage> inputs)
        {
            context.Own(Resource);
            return inputs[0];
        }
    }

    private sealed class TrackedResource : IDisposable
    {
        internal int Disposals { get; private set; }

        public void Dispose() => Disposals++;
    }
}
