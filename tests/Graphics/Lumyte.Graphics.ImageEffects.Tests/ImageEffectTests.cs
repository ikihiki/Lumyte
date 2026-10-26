using System.Reflection;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using Xunit;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.ImageEffects.Tests;

/// <summary>Checks immutable effect values, source-region propagation, DAG sharing and ownership.</summary>
public sealed class ImageEffectTests
{
    /// <summary>Checks independent matrix storage and rejection of invalid coefficients.</summary>
    [Fact]
    public void ColorMatrixCopiesAndValidatesCoefficients()
    {
        float[] coefficients = new float[20];
        coefficients[0] = 1;
        var matrix = new ColorMatrix4x5(coefficients);
        coefficients[0] = 8;
        Assert.Equal(1, matrix.Coefficients[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<float>)matrix.Coefficients)[0] = 3);
        Assert.Throws<ArgumentException>(() => new ColorMatrix4x5(new float[19]));
        coefficients[4] = float.NaN;
        Assert.Throws<ArgumentException>(() => new ColorMatrix4x5(coefficients));
    }

    /// <summary>Checks integer geometry validation and overflow behavior.</summary>
    [Fact]
    public void PixelBoundsRejectNegativeSizesAndOverflow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EffectPixelRect(0, 0, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EffectPixelRect(0, 0, 1, -1));
        Assert.Throws<OverflowException>(() => new EffectPixelRect(int.MaxValue, 0, 1, 1));
        Assert.Equal(0, new EffectPixelRect(-5, -5, 0, 0).Width);
    }

    /// <summary>Checks finite sigma, opacity and enum restrictions at graph construction.</summary>
    [Fact]
    public void GraphFactoriesRejectInvalidParameters()
    {
        var source = ImageEffectGraph.Source();
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageEffectGraph.Create(new GaussianBlurEffect(float.PositiveInfinity, 1), source));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageEffectGraph.Create(new GaussianBlurEffect(1, -1), source));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageEffectGraph.Create(new OpacityEffect(float.NaN), source));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageEffectGraph.Create(new OpacityEffect(1.01f), source));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageEffectGraph.Create(new CompositeEffect((EffectCompositeMode)99), source, source));
        Assert.Throws<ArgumentNullException>(() => ImageEffectGraph.Create(new ColorMatrixEffect(null!), source));
    }

    /// <summary>Checks reverse traversal through shared blurred and translated children.</summary>
    [Fact]
    public void RequiredSourceBoundsIncludesKernelAndInverseOffsets()
    {
        var source = ImageEffectGraph.Source();
        var blurred = ImageEffectGraph.Create(new GaussianBlurEffect(2, 3), source);
        var graph = ImageEffectGraph.Create(new CompositeEffect(EffectCompositeMode.SrcOver, 10, -4), blurred, source);
        Assert.Equal(new EffectPixelRect(4, 25, 26, 38), graph.GetRequiredSourceBounds(new(20, 30, 10, 20)));
        Assert.Equal(new EffectPixelRect(3, 5, 0, 1), graph.GetRequiredSourceBounds(new(3, 5, 0, 1)));
    }

    /// <summary>Checks that separately shared effect branches accumulate their requested regions.</summary>
    [Fact]
    public void RequiredSourceBoundsAccumulatesAllSharedParents()
    {
        var source = ImageEffectGraph.Source();
        var shared = ImageEffectGraph.Create(new GaussianBlurEffect(1, 0), source);
        var left = ImageEffectGraph.Create(new CompositeEffect(EffectCompositeMode.Plus, -10, 0), shared, source);
        var right = ImageEffectGraph.Create(new CompositeEffect(EffectCompositeMode.Plus, 10, 0), shared, source);
        var graph = ImageEffectGraph.Create(new CompositeEffect(EffectCompositeMode.SrcOver), left, right);
        Assert.Equal(new EffectPixelRect(-13, 0, 27, 1), graph.GetRequiredSourceBounds(new(0, 0, 1, 1)));
    }

    /// <summary>Checks shared operations compile once per invocation, never once for all invocations.</summary>
    [Fact]
    public void RenderGraphExpansionMemoizesOnlyWithinOneAppend()
    {
        var opacity = new CountingEffect();
        var composite = new ForwardEffect();
        var shared = ImageEffectGraph.Create(opacity, ImageEffectGraph.Source());
        var graph = ImageEffectGraph.Create(composite, shared, shared);
        using var plan = new RenderGraphBuilder(Device());
        EffectImage source = Source(plan);
        EffectImage first = plan.AppendEffects(graph, source);
        Assert.Equal(1, opacity.Count);
        Assert.Equal(source, first);
        EffectImage second = plan.AppendEffects(graph, source);
        Assert.Equal(2, opacity.Count);
    }

    /// <summary>Checks new fixed resources are released if an effect fails while appending.</summary>
    [Fact]
    public void AppendFailureReleasesOwnedResources()
    {
        var resource = new TrackedResource();
        using var plan = new RenderGraphBuilder(Device());
        Assert.Throws<InvalidOperationException>(() => plan.AppendEffects(ImageEffectGraph.Create(new FailingEffect(resource), ImageEffectGraph.Source()), Source(plan)));
        Assert.Equal(0, resource.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => plan.Compile());
        plan.Dispose();
        Assert.Equal(1, resource.DisposeCount);
    }

    /// <summary>Checks zero-sigma blur is identity and allocates no device resources.</summary>
    [Fact]
    public void ZeroSigmaBlurPreservesSourceWithoutDeviceCalls()
    {
        using var plan = new RenderGraphBuilder(Device());
        EffectImage source = Source(plan);
        EffectImage prepared = plan.AppendEffects(ImageEffectGraph.Create(new GaussianBlurEffect(0, 0), ImageEffectGraph.Source()), source);
        Assert.Equal(source, prepared);
    }

    /// <summary>Checks factory depth limits reject adversarial graphs before traversal.</summary>
    [Fact]
    public void GraphDepthBudgetIsEnforced()
    {
        var graph = ImageEffectGraph.Source();
        for (int i = 1; i < 256; i++)
        {
            graph = ImageEffectGraph.Create(new OpacityEffect(1), graph);
        }

        Assert.Throws<ArgumentException>(() => ImageEffectGraph.Create(new OpacityEffect(1), graph));
    }

    /// <summary>Direct and callback appends share one configured state without a manager dependency.</summary>
    /// <returns>Completion of extension construction and shared preparation checks.</returns>
    [Fact]
    public async Task SuppliedShaderArtifactsReachSharedDirectAndSubgraphPreparationAsync()
    {
        var artifact = ShaderArtifact.PackTarget(ShaderTarget.Wgsl, ShaderStage.Vertex, "main", "test", [1], "{}");
        var shaders = new ImageEffectShaders(artifact, artifact);
        using RenderGraphExtensions extensions = await new RenderGraphExtensionsBuilder().AddImageEffects(shaders).BuildAsync();
        using var plan = new RenderGraphBuilder(Device(), extensions: extensions);
        var effect = new ShaderInspectionEffect(shaders);
        EffectImage source = Source(plan);
        Assert.Equal(source, plan.AppendEffect(effect, source));
        ImageEffectsGraphContext shared = plan.GetExtension<ImageEffectsGraphContext>();
        using var callback = new ImageEffectSubgraph(plan, source);
        Assert.Equal(source, callback.AppendEffect(effect, source));
        Assert.Equal(source, plan.AppendEffects(ImageEffectGraph.Create(effect, ImageEffectGraph.Source()), source));
        Assert.Same(shared, plan.GetExtension<ImageEffectsGraphContext>());
        callback.Dispose();
        using CompiledRenderGraph compiled = plan.Compile();
    }

    /// <summary>Borrowed modules are reused and remain owned by their caller even if pipeline creation fails.</summary>
    /// <param name="borrowModules">Whether existing modules or CPU artifacts are supplied.</param>
    /// <returns>Completion of extension construction and shader ownership checks.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShaderModuleOwnershipIsPreservedOnPreparationFailureAsync(bool borrowModules)
    {
        var artifact = ShaderArtifact.PackTarget(ShaderTarget.Wgsl, ShaderStage.Vertex, "main", "test", [1], "{}");
        var vertex = new BorrowedShader(artifact);
        var fragment = new BorrowedShader(artifact);
        ImageEffectShaders shaders = borrowModules ? new ImageEffectShaders(vertex, fragment) : new ImageEffectShaders(artifact, artifact);
        IGraphicDevice device = DispatchProxy.Create<IGraphicDevice, ShaderDeviceProxy>();
        var proxy = (ShaderDeviceProxy)(object)device;
        using RenderGraphExtensions extensions = await new RenderGraphExtensionsBuilder().AddImageEffects(shaders).BuildAsync();
        using var plan = new RenderGraphBuilder(device, extensions: extensions);
        Assert.Throws<PipelineReachedException>(() => plan.AppendEffect(new OpacityEffect(0.5f), Source(plan)));
        Assert.All(proxy.Created, shader => Assert.False(((BorrowedShader)shader).Disposed));
        plan.Dispose();
        Assert.False(vertex.Disposed);
        Assert.False(fragment.Disposed);
        Assert.Equal(borrowModules ? 0 : 2, proxy.Created.Count);
        Assert.All(proxy.Created, shader => Assert.True(((BorrowedShader)shader).Disposed));
    }

    /// <summary>Failed cleanup retains dependent ownership and retries only the resources still awaiting release.</summary>
    [Fact]
    public void RetainedEffectRetriesFailedCleanupBeforeReleasingShaderOwnership()
    {
        var events = new List<string>();
        var shader = new ReleaseResource("shader", events);
        var effect = new ResourceOwningEffect([
            new ReleaseResource("dependency", events),
            new ReleaseResource("failing", events, failOnce: true),
            new ReleaseResource("released", events),
        ]);
        using var graph = new RenderGraphBuilder(Device());
        graph.Retain(shader);
        _ = graph.AppendEffect(effect, Source(graph));
        using CompiledRenderGraph compiled = graph.Compile();

        Assert.Throws<AggregateException>(() => compiled.Dispose());
        Assert.Equal(["released", "failing"], events);
        Assert.Throws<ObjectDisposedException>(() => ((IRenderGraphLifetime)effect.Context!).Validate());
        using var rejected = new TrackedResource();
        Assert.Throws<ObjectDisposedException>(() => effect.Context!.Own(rejected));

        compiled.Dispose();
        Assert.Equal(["released", "failing", "failing", "dependency", "shader"], events);
        compiled.Dispose();
        Assert.Equal(5, events.Count);
    }

    /// <summary>Failed preparation leaves retryable cleanup and its shader dependency with the graph.</summary>
    /// <param name="direct">Whether to use the direct effect entry point.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedPreparationKeepsCleanupUntilGraphDisposalSucceeds(bool direct)
    {
        var events = new List<string>();
        var effect = new FailingEffect(new ReleaseResource("effect", events, failOnce: true));
        using var graph = new RenderGraphBuilder(Device());
        graph.Retain(new ReleaseResource("shader", events));
        EffectImage source = Source(graph);
        Assert.Throws<InvalidOperationException>(() => direct
            ? graph.AppendEffect(effect, source)
            : graph.AppendEffects(ImageEffectGraph.Create(effect, ImageEffectGraph.Source()), source));
        Assert.Empty(events);
        Assert.Throws<ObjectDisposedException>(() => graph.Compile());
        Assert.Throws<AggregateException>(() => graph.Dispose());
        Assert.Equal(["effect"], events);
        graph.Dispose();
        Assert.Equal(["effect", "effect", "shader"], events);
    }

    /// <summary>A preowned context rejects a foreign device or color semantic before preparing any effect.</summary>
    /// <param name="wrongDevice">Whether to mismatch the device instead of the color semantic.</param>
    /// <param name="graphEntry">Whether to append the effect through its graph entry point.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SuppliedContextRejectsIncompatibleEnvironment(bool wrongDevice, bool graphEntry)
    {
        IGraphicDevice device = Device();
        using var graph = new RenderGraphBuilder(device);
        using var context = new EffectPreparationContext(
            wrongDevice ? Device() : device,
            wrongDevice ? EffectColorSemantic.DisplayLinear : EffectColorSemantic.SceneLinear);
        var effect = new CountingEffect();
        EffectImage source = Source(graph);
        if (graphEntry)
        {
            Assert.Throws<ArgumentException>(() => graph.AppendEffectsWithContext(
                ImageEffectGraph.Create(effect, ImageEffectGraph.Source()), source, context));
        }
        else
        {
            Assert.Throws<ArgumentException>(() => graph.AppendEffectWithContext(effect, context, source));
        }

        Assert.Equal(0, effect.Count);
    }

    private static IGraphicDevice Device() => DispatchProxy.Create<IGraphicDevice, UnusedDeviceProxy>();

    private static EffectImage Source(RenderGraphBuilder plan) => new(plan.CreateTexture(new() { Width = 4, Height = 4, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.Sampled | TextureUsage.RenderAttachment }), new(0, 0, 4, 4), EffectColorSemantic.DisplayLinear);

    /// <summary>Fails on GPU calls, proving CPU-only operations remain independent of the backend.</summary>
    public class UnusedDeviceProxy : DispatchProxy
    {
        /// <inheritdoc/>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new InvalidOperationException("CPU-only test called the graphics device.");
    }

    /// <summary>Fails after module selection, allowing ownership to be checked without a GPU.</summary>
    public class ShaderDeviceProxy : DispatchProxy
    {
        internal List<IGraphicsShader> Created { get; } = [];

        /// <inheritdoc/>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IGraphicDevice.CreateShader))
            {
                var shader = new BorrowedShader((ShaderArtifact)args![0]!);
                Created.Add(shader);
                return shader;
            }

            if (targetMethod?.Name == nameof(IGraphicDevice.CreateGraphicsPipeline))
            {
                throw new PipelineReachedException();
            }

            throw new InvalidOperationException("Unexpected device call.");
        }
    }

    private sealed class PipelineReachedException : Exception;

    private sealed class BorrowedShader(ShaderArtifact artifact) : IGraphicsShader
    {
        public ShaderArtifact Artifact => artifact;

        internal bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private abstract record IdentityEffect : ImageEffectDefinition
    {
        /// <inheritdoc/>
        public override int InputCount => 1;

        /// <inheritdoc/>
        public override EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs) => inputs[0];

        /// <inheritdoc/>
        public override EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output) => output;
    }

    private sealed record ShaderInspectionEffect(ImageEffectShaders Expected) : IdentityEffect
    {
        public override EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan, ReadOnlySpan<EffectImage> inputs)
        {
            Assert.Same(Expected, context.Shaders);
            return inputs[0];
        }
    }

    private sealed record CountingEffect : IdentityEffect
    {
        internal int Count { get; private set; }

        /// <inheritdoc/>
        public override EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan, ReadOnlySpan<EffectImage> inputs)
        {
            Count++;
            return inputs[0];
        }
    }

    private sealed record ForwardEffect : IdentityEffect
    {
        /// <inheritdoc/>
        public override int InputCount => 2;

        /// <inheritdoc/>
        public override EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan, ReadOnlySpan<EffectImage> inputs) => inputs[0];
    }

    private sealed record FailingEffect(IDisposable Resource) : IdentityEffect
    {
        /// <inheritdoc/>
        public override EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan, ReadOnlySpan<EffectImage> inputs)
        {
            context.Own(Resource);
            throw new InvalidOperationException("Injected effect failure.");
        }
    }

    private sealed record ResourceOwningEffect(IDisposable[] Resources) : IdentityEffect
    {
        internal EffectPreparationContext? Context { get; private set; }

        public override EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan, ReadOnlySpan<EffectImage> inputs)
        {
            Context = context;
            foreach (IDisposable resource in Resources)
            {
                context.Own(resource);
            }

            return inputs[0];
        }
    }

    private sealed class ReleaseResource(string name, List<string> events, bool failOnce = false) : IRenderGraphLifetime
    {
        private bool _fail = failOnce;

        public void Validate()
        {
        }

        public void Dispose()
        {
            events.Add(name);
            if (_fail)
            {
                _fail = false;
                throw new InvalidOperationException("Injected release failure.");
            }
        }
    }

    private sealed class TrackedResource : IDisposable
    {
        internal int DisposeCount { get; private set; }

        /// <inheritdoc/>
        public void Dispose() => DisposeCount++;
    }
}
