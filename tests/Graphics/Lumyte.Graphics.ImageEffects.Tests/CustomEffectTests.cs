using System.Reflection;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.ImageEffects.Tests;

/// <summary>Checks user-defined operations without changing the built-in registry or accessing the GPU.</summary>
public sealed class CustomEffectTests
{
    /// <summary>Checks child arrays are copied and invalid bindings fail before graph construction.</summary>
    [Fact]
    public void FactoryCopiesInputsAndValidatesBindings()
    {
        var definition = new Translation(3, -4);
        var renderer = new TranslationRenderer();
        var source = ImageEffectGraph.Source();
        ImageEffectGraph[] inputs = [source];
        var effect = ImageEffectGraph.Custom(definition, renderer, inputs);
        inputs[0] = ImageEffectGraph.Opacity(0, source);
        CustomEffectNode node = Assert.IsType<CustomEffectNode>(effect.Root);
        Assert.Same(definition, node.Definition);
        Assert.Same(source.Root, Assert.Single(node.Inputs));
        Assert.Throws<NotSupportedException>(() => ((IList<ImageEffectNode>)node.Inputs)[0] = inputs[0].Root);
        Assert.Throws<ArgumentNullException>(() => ImageEffectGraph.Custom<Translation>(null!, renderer, source));
        Assert.Throws<ArgumentNullException>(() => ImageEffectGraph.Custom(definition, null!, source));
        Assert.Throws<ArgumentNullException>(() => ImageEffectGraph.Custom(definition, renderer, (ImageEffectGraph[])null!));
        Assert.Throws<ArgumentNullException>(() => ImageEffectGraph.Custom(definition, renderer, [null!]));
        Assert.Throws<ArgumentException>(() => ImageEffectGraph.Custom(definition, renderer));
        Assert.Throws<ArgumentException>(() => ImageEffectGraph.Custom(definition, renderer, source, source));
    }

    /// <summary>Checks custom inverse mappings propagate through standard blur and shared branch inputs.</summary>
    [Fact]
    public void RegionsComposeAcrossCustomAndStandardNodes()
    {
        var source = ImageEffectGraph.Source();
        var blurred = ImageEffectGraph.GaussianBlur(1, 2, source);
        var renderer = new TranslationRenderer();
        var left = ImageEffectGraph.Custom(new Translation(-10, 0), renderer, blurred);
        var right = ImageEffectGraph.Custom(new Translation(10, 0), renderer, blurred);
        var effect = ImageEffectGraph.Composite(left, right, EffectCompositeMode.SrcOver);
        Assert.Equal(new EffectPixelRect(-13, -6, 27, 13), Compiler().GetRequiredSourceBounds(effect, new(0, 0, 1, 1)));
    }

    /// <summary>Checks all custom input slots contribute regions even when they refer to the same child.</summary>
    [Fact]
    public void MultipleInputsAccumulateTheirRequiredRegions()
    {
        var source = ImageEffectGraph.Source();
        var effect = ImageEffectGraph.Custom(new Branches(), new BranchRenderer(), source, source);
        Assert.Equal(new EffectPixelRect(-10, 0, 21, 1), Compiler().GetRequiredSourceBounds(effect, new(0, 0, 1, 1)));
    }

    /// <summary>Checks custom nodes share results only within one preparation and preserve typed parameters.</summary>
    [Fact]
    public void CustomDispatchSharesNodesOnlyWithinEachAppend()
    {
        var renderer = new TranslationRenderer();
        var shared = ImageEffectGraph.Custom(new Translation(3, -4), renderer, ImageEffectGraph.Source());
        var effect = ImageEffectGraph.Custom(new Branches(), new BranchRenderer(), shared, shared);
        var plan = new Graph(Device());
        EffectImage source = Source(plan);
        using PreparedImageEffect first = Compiler().AppendPasses(plan, effect, source);
        Assert.Equal(source.Bounds.Offset(3, -4), first.Output.Bounds);
        Assert.Equal(1, renderer.Calls);
        using PreparedImageEffect second = Compiler().AppendPasses(plan, effect, source);
        Assert.Equal(first.Output, second.Output);
        Assert.Equal(2, renderer.Calls);
    }

    /// <summary>Checks user operations can declare multiple passes and transfer fixed-resource ownership.</summary>
    [Fact]
    public void CustomRendererOwnsResourcesAndAddsMultiplePasses()
    {
        var resource = new TrackedResource();
        var renderer = new TranslationRenderer(resource, addPasses: true);
        var graph = new Graph(Device());
        PreparedImageEffect owner = Compiler().AppendPasses(graph, ImageEffectGraph.Custom(new Translation(0, 0), renderer, ImageEffectGraph.Source()), Source(graph));
        using (CompiledRenderGraph plan = graph.Compile())
        {
            Assert.Equal(new[] { "custom.first", "custom.second" }, plan.PassNames);
            Assert.Equal(0, resource.Disposals);
        }

        owner.Dispose();
        owner.Dispose();
        Assert.Equal(1, resource.Disposals);
    }

    /// <summary>Checks incorrect bounds and color semantics fail and release newly owned resources.</summary>
    /// <param name="wrongBounds">Whether to violate bounds rather than the color semantic.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidCustomOutputsReleaseFixedResources(bool wrongBounds)
    {
        var resource = new TrackedResource();
        var renderer = new InvalidRenderer(resource, wrongBounds);
        var graph = new Graph(Device());
        Assert.Throws<InvalidOperationException>(() => Compiler().AppendPasses(graph, ImageEffectGraph.Custom(new Translation(0, 0), renderer, ImageEffectGraph.Source()), Source(graph)));
        Assert.Equal(1, resource.Disposals);
    }

    /// <summary>Checks independent image generators can use the preparation color semantic.</summary>
    [Fact]
    public void ZeroInputGeneratorNeedsNoSourcePixels()
    {
        var definition = new Generator(new(-5, 9, 2, 3));
        var effect = ImageEffectGraph.Custom(definition, new GeneratorRenderer());
        var graph = new Graph(Device());
        using PreparedImageEffect owner = Compiler().AppendPasses(graph, effect, Source(graph) with { ColorSemantic = EffectColorSemantic.SceneLinear });
        Assert.Equal(definition.Bounds, owner.Output.Bounds);
        Assert.Equal(EffectColorSemantic.SceneLinear, owner.Output.ColorSemantic);
        Assert.Equal(new EffectPixelRect(7, 8, 0, 0), Compiler().GetRequiredSourceBounds(effect, new(7, 8, 1, 1)));
    }

    /// <summary>Checks empty child requirements stop inverse traversal through an otherwise throwing child.</summary>
    [Fact]
    public void UnneededInputsDoNotPropagateSourceRegions()
    {
        var child = ImageEffectGraph.Custom(new IgnoreInput(ThrowOnQuery: true), new IgnoreRenderer(), ImageEffectGraph.Source());
        var effect = ImageEffectGraph.Custom(new IgnoreInput(ThrowOnQuery: false), new IgnoreRenderer(), child);
        Assert.Equal(new EffectPixelRect(7, 8, 0, 0), Compiler().GetRequiredSourceBounds(effect, new(7, 8, 1, 1)));
    }

    /// <summary>Checks custom generators cannot request a zero-sized texture output.</summary>
    [Fact]
    public void EmptyDeclaredOutputFailsBeforeRenderer()
    {
        var renderer = new GeneratorRenderer();
        var graph = new Graph(Device());
        Assert.Throws<InvalidOperationException>(() => Compiler().AppendPasses(graph, ImageEffectGraph.Custom(new Generator(default), renderer), Source(graph)));
        Assert.Equal(0, renderer.Calls);
    }

    /// <summary>Checks custom graphs follow the same depth and distinct-node budgets as built-in operations.</summary>
    [Fact]
    public void CustomGraphsKeepDepthAndNodeBudgets()
    {
        var effect = ImageEffectGraph.Source();
        var renderer = new TranslationRenderer();
        for (int depth = 1; depth < 256; depth++)
        {
            effect = ImageEffectGraph.Custom(new Translation(0, 0), renderer, effect);
        }

        Assert.Throws<ArgumentException>(() => ImageEffectGraph.Custom(new Translation(0, 0), renderer, effect));
        ImageEffectGraph[] children = Enumerable.Range(0, 4096).Select(_ => ImageEffectGraph.Source()).ToArray();
        var wide = ImageEffectGraph.Custom(new Branches(children.Length), new BranchRenderer(), children);
        var graph = new Graph(Device());
        Assert.Throws<NotSupportedException>(() => Compiler().AppendPasses(graph, wide, Source(graph)));
        Assert.Throws<NotSupportedException>(() => Compiler().GetRequiredSourceBounds(wide, new(0, 0, 1, 1)));
    }

    /// <summary>Checks ordinary DI registration binds a typed renderer without extending the built-in table.</summary>
    [Fact]
    public void CustomRenderersUseOrdinaryTypedDiRegistration()
    {
        var services = new ServiceCollection();
        services.AddScoped<IImageEffectRenderer<Translation>>(_ => new TranslationRenderer());
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        IImageEffectRenderer<Translation> renderer = scope.ServiceProvider.GetRequiredService<IImageEffectRenderer<Translation>>();
        var graph = new Graph(Device());
        using PreparedImageEffect owner = Compiler().AppendPasses(graph, ImageEffectGraph.Custom(new Translation(1, 2), renderer, ImageEffectGraph.Source()), Source(graph));
        Assert.Equal(new EffectPixelRect(1, 2, 4, 4), owner.Output.Bounds);
    }

    private static ImageEffectCompiler Compiler() => new(Device(), ImageEffectRenderers.CreateDefault());

    private static IGraphicDevice Device() => DispatchProxy.Create<IGraphicDevice, ImageEffectTests.UnusedDeviceProxy>();

    private static EffectImage Source(Graph graph) => new(graph.CreateTexture(new() { Width = 4, Height = 4, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.Sampled | TextureUsage.RenderAttachment }), new(0, 0, 4, 4), EffectColorSemantic.DisplayLinear);

    private sealed record Translation(int X, int Y) : ImageEffectDefinition
    {
        /// <inheritdoc/>
        public override int InputCount => 1;

        /// <inheritdoc/>
        public override EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs) => inputs[0].Offset(X, Y);

        /// <inheritdoc/>
        public override EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output) => output.OffsetInverse(X, Y);
    }

    private sealed class TranslationRenderer(TrackedResource? resource = null, bool addPasses = false) : IImageEffectRenderer<Translation>
    {
        internal int Calls { get; private set; }

        /// <inheritdoc/>
        public EffectImage AppendPasses(EffectPreparationContext context, Graph plan, Translation effect, ReadOnlySpan<EffectImage> inputs)
        {
            Calls++;
            if (resource is not null)
            {
                context.Own(resource);
            }

            if (addPasses)
            {
                RenderGraphPass first = plan.AddPass("custom.first", _ => { }).KeepAlive();
                plan.AddPass("custom.second", _ => { }).DependsOn(first).KeepAlive();
            }

            return inputs[0] with { Bounds = inputs[0].Bounds.Offset(effect.X, effect.Y) };
        }
    }

    private sealed record Branches(int Count = 2) : ImageEffectDefinition
    {
        /// <inheritdoc/>
        public override int InputCount => Count;

        /// <inheritdoc/>
        public override EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs) => inputs[0];

        /// <inheritdoc/>
        public override EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output) => output.Offset(inputIndex == 0 ? -10 : 10, 0);
    }

    private sealed class BranchRenderer : IImageEffectRenderer<Branches>
    {
        /// <inheritdoc/>
        public EffectImage AppendPasses(EffectPreparationContext context, Graph plan, Branches effect, ReadOnlySpan<EffectImage> inputs) => inputs[0];
    }

    private sealed class InvalidRenderer(TrackedResource resource, bool wrongBounds) : IImageEffectRenderer<Translation>
    {
        /// <inheritdoc/>
        public EffectImage AppendPasses(EffectPreparationContext context, Graph plan, Translation effect, ReadOnlySpan<EffectImage> inputs)
        {
            context.Own(resource);
            return wrongBounds ? inputs[0] with { Bounds = inputs[0].Bounds.Offset(1, 0) } : inputs[0] with { ColorSemantic = EffectColorSemantic.SceneLinear };
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
    }

    private sealed class GeneratorRenderer : IImageEffectRenderer<Generator>
    {
        internal int Calls { get; private set; }

        /// <inheritdoc/>
        public EffectImage AppendPasses(EffectPreparationContext context, Graph plan, Generator effect, ReadOnlySpan<EffectImage> inputs)
        {
            Calls++;
            Assert.True(inputs.IsEmpty);
            RenderGraphTexture texture = plan.CreateTexture(new() { Width = checked((uint)effect.Bounds.Width), Height = checked((uint)effect.Bounds.Height), Format = TextureFormat.Rgba16Float, Usage = TextureUsage.Sampled | TextureUsage.RenderAttachment });
            return new(texture, effect.Bounds, context.ColorSemantic);
        }
    }

    private sealed record IgnoreInput(bool ThrowOnQuery) : ImageEffectDefinition
    {
        /// <inheritdoc/>
        public override int InputCount => 1;

        /// <inheritdoc/>
        public override EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs) => inputs[0];

        /// <inheritdoc/>
        public override EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output) => ThrowOnQuery ? throw new InvalidOperationException() : new(output.X, output.Y, 0, 0);
    }

    private sealed class IgnoreRenderer : IImageEffectRenderer<IgnoreInput>
    {
        /// <inheritdoc/>
        public EffectImage AppendPasses(EffectPreparationContext context, Graph plan, IgnoreInput effect, ReadOnlySpan<EffectImage> inputs) => inputs[0];
    }

    private sealed class TrackedResource : IDisposable
    {
        internal int Disposals { get; private set; }

        /// <inheritdoc/>
        public void Dispose() => Disposals++;
    }
}
