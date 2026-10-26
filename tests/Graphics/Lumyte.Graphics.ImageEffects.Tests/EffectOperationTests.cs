using System.Reflection;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.ImageEffects.Tests;

/// <summary>Checks self-contained user effects and direct pass appending without renderer replacement.</summary>
public sealed class EffectOperationTests
{
    /// <summary>Checks every pass extension rejects a missing RenderGraph before touching the graph.</summary>
    [Fact]
    public void AppendExtensionsRejectNullGraph()
    {
        Graph graph = null!;
        Assert.Throws<ArgumentNullException>(() => graph.AppendGaussianBlur(default, 0, 0));
        Assert.Throws<ArgumentNullException>(() => graph.AppendOpacity(default, 1));
        Assert.Throws<ArgumentNullException>(() => graph.AppendColorMatrix(default, new(new float[20])));
        Assert.Throws<ArgumentNullException>(() => graph.AppendComposite(default, default, EffectCompositeMode.Plus));
    }

    /// <summary>Checks pass extensions return graph-local outputs instead of new effect graphs.</summary>
    [Fact]
    public void AppendGaussianBlurUsesParametersAndSource()
    {
        using var graph = new Graph(Device());
        EffectImage source = Source(graph);
        EffectImage owner = graph.AppendGaussianBlur(source, 0, 0);
        Assert.Equal(source, owner);
        using CompiledRenderGraph plan = graph.Compile();
        Assert.Empty(plan.PassNames);
    }

    /// <summary>Checks graph extensions require the graph's own device, even for an input-free generator.</summary>
    [Fact]
    public void EffectPassesRequireRenderGraphDevice()
    {
        using var graph = new Graph();
        Assert.Null(graph.Device);
        Assert.Throws<InvalidOperationException>(() => graph.AppendEffect(new Generator(new(0, 0, 2, 3))));
        using CompiledRenderGraph plan = graph.Compile();
        Assert.Empty(plan.PassNames);
    }

    /// <summary>Checks copied input snapshots, input counts and null validation.</summary>
    [Fact]
    public void GraphCopiesInputsAndValidatesEffects()
    {
        var effect = new Translation(3, -4);
        var source = ImageEffectGraph.Source();
        ImageEffectGraph[] inputs = [source];
        var snapshot = ImageEffectGraph.Create(effect, inputs);
        inputs[0] = ImageEffectGraph.Source();
        EffectOperationNode node = Assert.IsType<EffectOperationNode>(snapshot.Root);
        Assert.Same(effect, node.Definition);
        Assert.Same(source.Root, Assert.Single(node.Inputs));
        Assert.Throws<NotSupportedException>(() => ((IList<ImageEffectNode>)node.Inputs)[0] = inputs[0].Root);
        Assert.Throws<ArgumentNullException>(() => ImageEffectGraph.Create(null!, source));
        Assert.Throws<ArgumentNullException>(() => ImageEffectGraph.Create(effect, (ImageEffectGraph[])null!));
        Assert.Throws<ArgumentNullException>(() => ImageEffectGraph.Create(effect, [null!]));
        Assert.Throws<ArgumentException>(() => ImageEffectGraph.Create(effect));
    }

    /// <summary>Checks inverse region propagation through blur and two translated branches.</summary>
    [Fact]
    public void RegionsComposeAcrossDistinctEffects()
    {
        var blurred = ImageEffectGraph.Create(new GaussianBlurEffect(1, 2), ImageEffectGraph.Source());
        var left = ImageEffectGraph.Create(new Translation(-10, 0), blurred);
        var right = ImageEffectGraph.Create(new Translation(10, 0), blurred);
        var effect = ImageEffectGraph.Create(new CompositeEffect(EffectCompositeMode.SrcOver), left, right);
        Assert.Equal(new EffectPixelRect(-13, -6, 27, 13), effect.GetRequiredSourceBounds(new(0, 0, 1, 1)));
    }

    /// <summary>Checks direct appending adds multiple passes and owns resources until disposal.</summary>
    [Fact]
    public void DirectEffectOwnsResourcesAndAddsMultiplePasses()
    {
        var resource = new TrackedResource();
        var effect = new Translation(3, -4, resource, AddPasses: true);
        using var graph = new Graph(Device());
        EffectImage source = Source(graph);
        EffectImage owner = graph.AppendEffect(effect, source);
        Assert.Equal(source.Bounds.Offset(3, -4), owner.Bounds);
        using (CompiledRenderGraph plan = graph.Compile())
        {
            Assert.Equal(new[] { "effect.first", "effect.second" }, plan.PassNames);
            Assert.Equal(0, resource.Disposals);
        }

        graph.Dispose();
        graph.Dispose();
        Assert.Equal(1, resource.Disposals);
    }

    /// <summary>Checks direct and graph routes both validate output contracts and release resources.</summary>
    /// <param name="direct">Whether to append directly.</param>
    /// <param name="wrongBounds">Whether to violate bounds rather than color.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void InvalidOutputsReleaseResources(bool direct, bool wrongBounds)
    {
        var resource = new TrackedResource();
        var effect = new InvalidEffect(resource, wrongBounds);
        using var graph = new Graph(Device());
        EffectImage source = Source(graph);
        Assert.Throws<InvalidOperationException>(() => direct
            ? graph.AppendEffect(effect, source)
            : graph.AppendEffects(ImageEffectGraph.Create(effect, ImageEffectGraph.Source()), source));
        Assert.Equal(0, resource.Disposals);
        Assert.Throws<ObjectDisposedException>(() => graph.Compile());
        graph.Dispose();
        Assert.Equal(1, resource.Disposals);
    }

    /// <summary>Checks direct input validation precedes pass emission.</summary>
    [Fact]
    public void DirectAppendValidatesInputsBeforeExecution()
    {
        using var graph = new Graph(Device());
        var effect = new Translation(0, 0);
        Assert.Throws<ArgumentNullException>(() => ((Graph)null!).AppendEffect(effect, Source(graph)));
        Assert.Throws<ArgumentNullException>(() => graph.AppendEffect(null!, Source(graph)));
        Assert.Throws<ArgumentException>(() => graph.AppendEffect(effect));
        Assert.Throws<ArgumentNullException>(() => graph.AppendEffect(effect, default(EffectImage)));
        EffectImage source = Source(graph);
        Assert.Throws<ArgumentException>(() => graph.AppendComposite(source, source with { ColorSemantic = EffectColorSemantic.SceneLinear }, EffectCompositeMode.Plus));
    }

    /// <summary>Checks generators need no source region and preserve the requested graph color semantic.</summary>
    [Fact]
    public void ZeroInputGeneratorNeedsNoSourcePixels()
    {
        var effect = new Generator(new(-5, 9, 2, 3));
        var snapshot = ImageEffectGraph.Create(effect);
        using var graph = new Graph(Device());
        EffectImage owner = graph.AppendEffects(snapshot, Source(graph) with { ColorSemantic = EffectColorSemantic.SceneLinear });
        Assert.Equal(effect.Bounds, owner.Bounds);
        Assert.Equal(EffectColorSemantic.SceneLinear, owner.ColorSemantic);
        Assert.Equal(new EffectPixelRect(7, 8, 0, 0), snapshot.GetRequiredSourceBounds(new(7, 8, 1, 1)));
        EffectImage direct = graph.AppendEffect(effect);
        Assert.Equal(effect.Bounds, direct.Bounds);
    }

    /// <summary>Checks depth and distinct-node budgets apply to user effects.</summary>
    [Fact]
    public void GraphBudgetsApplyToSelfContainedEffects()
    {
        var snapshot = ImageEffectGraph.Source();
        for (int i = 1; i < 256; i++)
        {
            snapshot = ImageEffectGraph.Create(new Translation(0, 0), snapshot);
        }

        Assert.Throws<ArgumentException>(() => ImageEffectGraph.Create(new Translation(0, 0), snapshot));
        ImageEffectGraph[] children = Enumerable.Range(0, 4096).Select(_ => ImageEffectGraph.Source()).ToArray();
        var wide = ImageEffectGraph.Create(new ManyInputs(children.Length), children);
        Assert.Throws<NotSupportedException>(() => wide.GetRequiredSourceBounds(new(0, 0, 1, 1)));
    }

    /// <summary>Checks callback views share the parent and cannot release fixed resources while the plan is submitted.</summary>
    [Fact]
    public void SubgraphSharesParentAndBorrowsEffectResources()
    {
        using var graph = new Graph(Device());
        EffectImage source = Source(graph);
        var resource = new TrackedResource();
        var scope = new ImageEffectSubgraph(graph, source);
        Assert.Same(graph, scope.Graph);
        Assert.Equal(source, scope.Source);
        EffectImage translated = scope.AppendEffect(new Translation(2, 3, resource, AddPasses: true), scope.Source);
        EffectImage result = scope.AppendGaussianBlur(translated, 0, 0);
        Assert.Equal(source.Bounds.Offset(2, 3), result.Bounds);
        using IGraphicsCommandBuffer commands = DispatchProxy.Create<IGraphicsCommandBuffer, CommandStateProxy>();
        var commandState = (CommandStateProxy)(object)commands;
        using (CompiledRenderGraph plan = graph.Compile())
        {
            Assert.Equal(new[] { "effect.first", "effect.second" }, plan.PassNames);
            Assert.Equal(0, resource.Disposals);
            plan.Record(commands);
            commandState.State = CommandBufferState.Submitted;
            scope.Dispose();
            Assert.Equal(0, resource.Disposals);
            Assert.Throws<InvalidOperationException>(() => plan.Dispose());
            commandState.State = CommandBufferState.Completed;
        }

        scope.Dispose();
        Assert.Equal(1, resource.Disposals);
        Assert.Throws<ObjectDisposedException>(() => scope.AppendOpacity(source, 1));
    }

    private static IGraphicDevice Device() => DispatchProxy.Create<IGraphicDevice, ImageEffectTests.UnusedDeviceProxy>();

    private static EffectImage Source(Graph graph) => new(graph.CreateTexture(new() { Width = 4, Height = 4, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.Sampled | TextureUsage.RenderAttachment }), new(0, 0, 4, 4), EffectColorSemantic.DisplayLinear);

    /// <summary>Tracks command ownership without performing native drawing.</summary>
    public class CommandStateProxy : DispatchProxy
    {
        internal CommandBufferState State { get; set; } = CommandBufferState.Recording;

        /// <inheritdoc/>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "get_State")
            {
                return State;
            }

            if (targetMethod?.Name == nameof(IDisposable.Dispose))
            {
                State = CommandBufferState.Disposed;
                return null;
            }

            throw new NotSupportedException();
        }
    }

    private record Translation(int X, int Y, TrackedResource? Resource = null, bool AddPasses = false) : ImageEffectDefinition
    {
        /// <inheritdoc/>
        public override int InputCount => 1;

        /// <inheritdoc/>
        public override EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs) => inputs[0].Offset(X, Y);

        /// <inheritdoc/>
        public override EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output) => output.OffsetInverse(X, Y);

        /// <inheritdoc/>
        public override EffectImage AppendPasses(EffectPreparationContext context, Graph plan, ReadOnlySpan<EffectImage> inputs)
        {
            if (Resource is not null)
            {
                context.Own(Resource);
            }

            if (AddPasses)
            {
                RenderGraphPass first = plan.AddPass("effect.first", _ => { }).KeepAlive();
                plan.AddPass("effect.second", _ => { }).DependsOn(first).KeepAlive();
            }

            return inputs[0] with { Bounds = inputs[0].Bounds.Offset(X, Y) };
        }
    }

    private sealed record InvalidEffect(TrackedResource Resource, bool WrongBounds) : ImageEffectDefinition
    {
        /// <inheritdoc/>
        public override int InputCount => 1;

        /// <inheritdoc/>
        public override EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs) => inputs[0];

        /// <inheritdoc/>
        public override EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output) => output;

        /// <inheritdoc/>
        public override EffectImage AppendPasses(EffectPreparationContext context, Graph plan, ReadOnlySpan<EffectImage> inputs)
        {
            context.Own(Resource);
            return WrongBounds ? inputs[0] with { Bounds = inputs[0].Bounds.Offset(1, 0) } : inputs[0] with { ColorSemantic = EffectColorSemantic.SceneLinear };
        }
    }

    private sealed record Generator(EffectPixelRect Bounds) : ImageEffectDefinition
    {
        /// <inheritdoc/>
        public override int InputCount => 0;

        /// <inheritdoc/>
        public override EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs) => Bounds;

        /// <inheritdoc/>
        public override EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output) => throw new InvalidOperationException();

        /// <inheritdoc/>
        public override EffectImage AppendPasses(EffectPreparationContext context, Graph plan, ReadOnlySpan<EffectImage> inputs)
        {
            RenderGraphTexture texture = plan.CreateTexture(new() { Width = checked((uint)Bounds.Width), Height = checked((uint)Bounds.Height), Format = TextureFormat.Rgba16Float, Usage = TextureUsage.Sampled | TextureUsage.RenderAttachment });
            return new(texture, Bounds, context.ColorSemantic);
        }
    }

    private sealed record ManyInputs(int Count) : ImageEffectDefinition
    {
        /// <inheritdoc/>
        public override int InputCount => Count;

        /// <inheritdoc/>
        public override EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs) => inputs[0];

        /// <inheritdoc/>
        public override EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output) => output;

        /// <inheritdoc/>
        public override EffectImage AppendPasses(EffectPreparationContext context, Graph plan, ReadOnlySpan<EffectImage> inputs) => inputs[0];
    }

    private sealed class TrackedResource : IDisposable
    {
        internal int Disposals { get; private set; }

        /// <inheritdoc/>
        public void Dispose() => Disposals++;
    }
}
