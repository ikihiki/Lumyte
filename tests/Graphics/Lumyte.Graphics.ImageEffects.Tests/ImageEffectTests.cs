using System.Reflection;
using Lumyte.Graphics.Abstractions;
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
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageEffectGraph.GaussianBlur(float.PositiveInfinity, 1, source));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageEffectGraph.GaussianBlur(1, -1, source));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageEffectGraph.Opacity(float.NaN, source));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageEffectGraph.Opacity(1.01f, source));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageEffectGraph.Composite(source, source, (EffectCompositeMode)99));
        Assert.Throws<ArgumentNullException>(() => ImageEffectGraph.ColorMatrix(null!, source));
    }

    /// <summary>Checks reverse traversal through shared blurred and translated children.</summary>
    [Fact]
    public void RequiredSourceBoundsIncludesKernelAndInverseOffsets()
    {
        ImageEffectCompiler compiler = Compiler();
        var source = ImageEffectGraph.Source();
        var blurred = ImageEffectGraph.GaussianBlur(2, 3, source);
        var graph = ImageEffectGraph.Composite(blurred, source, EffectCompositeMode.SrcOver, 10, -4);
        Assert.Equal(new EffectPixelRect(4, 25, 26, 38), compiler.GetRequiredSourceBounds(graph, new(20, 30, 10, 20)));
        Assert.Equal(new EffectPixelRect(3, 5, 0, 1), compiler.GetRequiredSourceBounds(graph, new(3, 5, 0, 1)));
    }

    /// <summary>Checks that separately shared effect branches accumulate their requested regions.</summary>
    [Fact]
    public void RequiredSourceBoundsAccumulatesAllSharedParents()
    {
        ImageEffectCompiler compiler = Compiler();
        var source = ImageEffectGraph.Source();
        var shared = ImageEffectGraph.GaussianBlur(1, 0, source);
        var left = ImageEffectGraph.Composite(shared, source, EffectCompositeMode.Plus, -10, 0);
        var right = ImageEffectGraph.Composite(shared, source, EffectCompositeMode.Plus, 10, 0);
        var graph = ImageEffectGraph.Composite(left, right, EffectCompositeMode.SrcOver);
        Assert.Equal(new EffectPixelRect(-13, 0, 27, 1), compiler.GetRequiredSourceBounds(graph, new(0, 0, 1, 1)));
    }

    /// <summary>Checks shared operations compile once per invocation, never once for all invocations.</summary>
    [Fact]
    public void CompilerMemoizesOnlyWithinOnePreparation()
    {
        var opacity = new CountingOpacityRenderer();
        var composite = new ForwardCompositeRenderer();
        var compiler = new ImageEffectCompiler(Device(), new(new DefaultColorMatrixEffectRenderer(), new DefaultGaussianBlurEffectRenderer(), opacity, composite));
        var shared = ImageEffectGraph.Opacity(0.5f, ImageEffectGraph.Source());
        var graph = ImageEffectGraph.Composite(shared, shared, EffectCompositeMode.Plus);
        var plan = new RenderGraphBuilder(Device());
        EffectImage source = Source(plan);
        using PreparedImageEffect first = compiler.AppendPasses(plan, graph, source);
        Assert.Equal(1, opacity.Count);
        Assert.Equal(source, first.Output);
        using PreparedImageEffect second = compiler.AppendPasses(plan, graph, source);
        Assert.Equal(2, opacity.Count);
    }

    /// <summary>Checks new fixed resources are released if a renderer fails while appending.</summary>
    [Fact]
    public void AppendFailureReleasesOwnedResources()
    {
        var resource = new TrackedResource();
        var compiler = new ImageEffectCompiler(Device(), new(new DefaultColorMatrixEffectRenderer(), new DefaultGaussianBlurEffectRenderer(), new FailingOpacityRenderer(resource), new ForwardCompositeRenderer()));
        var plan = new RenderGraphBuilder(Device());
        Assert.Throws<InvalidOperationException>(() => compiler.AppendPasses(plan, ImageEffectGraph.Opacity(0.5f, ImageEffectGraph.Source()), Source(plan)));
        Assert.Equal(1, resource.DisposeCount);
    }

    /// <summary>Checks zero-sigma blur is identity and allocates no device resources.</summary>
    [Fact]
    public void ZeroSigmaBlurPreservesSourceWithoutDeviceCalls()
    {
        var plan = new RenderGraphBuilder(Device());
        EffectImage source = Source(plan);
        using PreparedImageEffect prepared = Compiler().AppendPasses(plan, ImageEffectGraph.GaussianBlur(0, 0, ImageEffectGraph.Source()), source);
        Assert.Equal(source, prepared.Output);
    }

    /// <summary>Checks factory depth limits reject adversarial graphs before traversal.</summary>
    [Fact]
    public void GraphDepthBudgetIsEnforced()
    {
        var graph = ImageEffectGraph.Source();
        for (int i = 1; i < 256; i++)
        {
            graph = ImageEffectGraph.Opacity(1, graph);
        }

        Assert.Throws<ArgumentException>(() => ImageEffectGraph.Opacity(1, graph));
    }

    private static ImageEffectCompiler Compiler() => new(Device(), ImageEffectRenderers.CreateDefault());

    private static IGraphicDevice Device() => DispatchProxy.Create<IGraphicDevice, UnusedDeviceProxy>();

    private static EffectImage Source(RenderGraphBuilder plan) => new(plan.CreateTexture(new() { Width = 4, Height = 4, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.Sampled | TextureUsage.RenderAttachment }), new(0, 0, 4, 4), EffectColorSemantic.DisplayLinear);

    /// <summary>Fails on GPU calls, proving CPU-only operations remain independent of the backend.</summary>
    public class UnusedDeviceProxy : DispatchProxy
    {
        /// <inheritdoc/>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new InvalidOperationException("CPU-only test called the graphics device.");
    }

    private sealed class CountingOpacityRenderer : IOpacityEffectRenderer
    {
        internal int Count { get; private set; }

        /// <inheritdoc/>
        public EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan, OpacityEffectNode effect, EffectImage input)
        {
            Count++;
            return input;
        }
    }

    private sealed class ForwardCompositeRenderer : ICompositeEffectRenderer
    {
        /// <inheritdoc/>
        public EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan, CompositeEffectNode effect, EffectImage source, EffectImage backdrop) => source;
    }

    private sealed class FailingOpacityRenderer(TrackedResource resource) : IOpacityEffectRenderer
    {
        /// <inheritdoc/>
        public EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan, OpacityEffectNode effect, EffectImage input)
        {
            context.Own(resource);
            throw new InvalidOperationException("Injected renderer failure.");
        }
    }

    private sealed class TrackedResource : IDisposable
    {
        internal int DisposeCount { get; private set; }

        /// <inheritdoc/>
        public void Dispose() => DisposeCount++;
    }
}
