using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.ImageEffects;

/// <summary>Adds image-effect passes to RenderGraph, which manages dependencies, barriers and temporary images.</summary>
public static class ImageEffectRenderGraphExtensions
{
    /// <summary>Appends ColorMatrix passes from parameters and returns the owned output.</summary>
    /// <param name="plan">The caller-owned render graph.</param>
    /// <param name="input">The graph-local input image.</param>
    /// <param name="matrix">The immutable color matrix.</param>
    /// <returns>The graph-local output; fixed resources belong to the graph and its compiled plan.</returns>
    public static EffectImage AppendColorMatrix(this RenderGraphBuilder plan, EffectImage input, ColorMatrix4x5 matrix)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.AppendEffect(new ColorMatrixEffect(matrix), input);
    }

    /// <summary>Appends GaussianBlur passes from parameters and returns the owned output.</summary>
    /// <param name="plan">The caller-owned render graph.</param>
    /// <param name="input">The graph-local input image.</param>
    /// <param name="sigmaX">The finite nonnegative horizontal device-pixel sigma.</param>
    /// <param name="sigmaY">The finite nonnegative vertical device-pixel sigma.</param>
    /// <returns>The graph-local output; fixed resources belong to the graph and its compiled plan.</returns>
    public static EffectImage AppendGaussianBlur(this RenderGraphBuilder plan, EffectImage input, float sigmaX, float sigmaY)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.AppendEffect(new GaussianBlurEffect(sigmaX, sigmaY), input);
    }

    /// <summary>Appends Opacity passes from parameters and returns the owned output.</summary>
    /// <param name="plan">The caller-owned render graph.</param>
    /// <param name="input">The graph-local input image.</param>
    /// <param name="opacity">The finite multiplier in [0, 1].</param>
    /// <returns>The graph-local output; fixed resources belong to the graph and its compiled plan.</returns>
    public static EffectImage AppendOpacity(this RenderGraphBuilder plan, EffectImage input, float opacity)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.AppendEffect(new OpacityEffect(opacity), input);
    }

    /// <summary>Appends Composite passes from parameters and returns the owned output.</summary>
    /// <param name="plan">The caller-owned render graph.</param>
    /// <param name="source">The graph-local source image.</param>
    /// <param name="backdrop">The graph-local backdrop image.</param>
    /// <param name="mode">The composition mode.</param>
    /// <param name="sourceOffsetX">The integer horizontal source translation.</param>
    /// <param name="sourceOffsetY">The integer vertical source translation.</param>
    /// <returns>The graph-local output; fixed resources belong to the graph and its compiled plan.</returns>
    public static EffectImage AppendComposite(this RenderGraphBuilder plan, EffectImage source, EffectImage backdrop, EffectCompositeMode mode, int sourceOffsetX = 0, int sourceOffsetY = 0)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.AppendEffect(new CompositeEffect(mode, sourceOffsetX, sourceOffsetY), source, backdrop);
    }

    /// <summary>Registers image-effect state using supplied shaders or embedded defaults.</summary>
    /// <param name="extensions">The reusable graph extension configuration.</param>
    /// <param name="shaders">Optional borrowed shader artifacts or modules.</param>
    /// <returns>The same configuration.</returns>
    public static RenderGraphExtensions AddImageEffects(this RenderGraphExtensions extensions, ImageEffectShaders? shaders = null)
        => extensions.Add(graph => new ImageEffectsGraphContext(graph, shaders));

    /// <summary>Appends the effect graph using the graph's shared image-effects extension.</summary>
    /// <param name="plan">The caller-owned render graph.</param>
    /// <param name="graph">The immutable effect graph.</param>
    /// <param name="source">The source image bound to the same render graph.</param>
    /// <returns>The graph-local output; the graph owns preparation and cleanup.</returns>
    /// <remarks>Discard and dispose the graph if preparation fails.</remarks>
    public static EffectImage AppendEffects(this RenderGraphBuilder plan, ImageEffectGraph graph, EffectImage source)
        => GetContext(plan).AppendEffects(graph, source);

    /// <summary>Appends an effect graph into a context whose ownership was established before preparation.</summary>
    /// <param name="plan">The graph receiving passes.</param>
    /// <param name="graph">The immutable effect graph.</param>
    /// <param name="source">The graph-local input image.</param>
    /// <param name="context">The caller-owned context, using the same device and color semantic.</param>
    /// <returns>The graph-local result; the context owns its fixed resources even if preparation fails.</returns>
    /// <remarks>Discard the graph after failure and dispose the context, retrying cleanup if necessary.</remarks>
    public static EffectImage AppendEffectsWithContext(this RenderGraphBuilder plan, ImageEffectGraph graph, EffectImage source, EffectPreparationContext context)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ValidateImage(source);
        ValidateContext(plan, context, source.ColorSemantic);
        var results = new Dictionary<ImageEffectNode, EffectImage>(ReferenceEqualityComparer.Instance);
        try
        {
            return Visit(graph.Root);
        }
        catch
        {
            context.Close();
            throw;
        }

        EffectImage Visit(ImageEffectNode node)
        {
            if (results.TryGetValue(node, out EffectImage result))
            {
                return result;
            }

            if (results.Count >= 4096)
            {
                throw new NotSupportedException("An effect graph may contain at most 4096 distinct operations.");
            }

            result = node switch
            {
                SourceEffectNode => source,
                EffectOperationNode operation => AppendOperation(operation),
                _ => throw new NotSupportedException("Unknown effect operation."),
            };
            ValidateImage(result);
            if (result.ColorSemantic != source.ColorSemantic)
            {
                throw new InvalidOperationException("An effect cannot implicitly change the color semantic.");
            }

            if (results.Count >= 4096)
            {
                throw new NotSupportedException("An effect graph may contain at most 4096 distinct operations.");
            }

            results.Add(node, result);
            return result;
        }

        EffectImage AppendOperation(EffectOperationNode operation)
        {
            var images = new EffectImage[operation.Inputs.Count];
            var bounds = new EffectPixelRect[operation.Inputs.Count];
            for (int i = 0; i < operation.Inputs.Count; i++)
            {
                images[i] = Visit(operation.Inputs[i]);
                bounds[i] = images[i].Bounds;
            }

            EffectPixelRect expected = operation.Definition.GetOutputBounds(bounds);
            if (expected.IsEmpty)
            {
                throw new InvalidOperationException("An effect must declare positive output dimensions.");
            }

            EffectImage result = operation.Definition.AppendPasses(context, plan, images);
            if (result.Bounds != expected)
            {
                throw new InvalidOperationException("An effect must preserve its definition's declared output bounds.");
            }

            return result;
        }
    }

    /// <summary>Appends one existing effect through the graph's shared image-effects extension.</summary>
    /// <param name="plan">The caller-owned render graph; discard it if append fails.</param>
    /// <param name="effect">The immutable effect with its fixed implementation.</param>
    /// <param name="inputs">Ordered graph-local input images.</param>
    /// <returns>The graph-local output, with fixed resources owned by the graph and its plan.</returns>
    public static EffectImage AppendEffect(this RenderGraphBuilder plan, ImageEffectDefinition effect, params EffectImage[] inputs)
        => GetContext(plan).AppendEffect(effect, inputs);

    /// <summary>Appends one effect into an already owned preparation context.</summary>
    /// <param name="plan">The graph receiving passes.</param>
    /// <param name="effect">The immutable effect definition.</param>
    /// <param name="context">The caller-owned context with matching device and color semantic.</param>
    /// <param name="inputs">The ordered graph-local inputs.</param>
    /// <returns>The graph-local result, with fixed resources owned by the context.</returns>
    /// <remarks>Failure leaves cleanup with the caller; discard the graph and dispose the context.</remarks>
    public static EffectImage AppendEffectWithContext(this RenderGraphBuilder plan, ImageEffectDefinition effect, EffectPreparationContext context, params EffectImage[] inputs)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(inputs);
        if (effect.InputCount < 0 || effect.InputCount != inputs.Length)
        {
            throw new ArgumentException("The image count must match the effect input count.", nameof(inputs));
        }

        EffectColorSemantic semantic = inputs.Length == 0 ? EffectColorSemantic.DisplayLinear : inputs[0].ColorSemantic;
        ValidateContext(plan, context, semantic);
        var bounds = new EffectPixelRect[inputs.Length];
        for (int i = 0; i < inputs.Length; i++)
        {
            ValidateImage(inputs[i]);
            if (inputs[i].ColorSemantic != semantic)
            {
                throw new ArgumentException("Effect inputs must use the same color semantic.", nameof(inputs));
            }

            bounds[i] = inputs[i].Bounds;
        }

        EffectPixelRect expected = effect.GetOutputBounds(bounds);
        if (expected.IsEmpty)
        {
            throw new InvalidOperationException("An effect must declare positive output dimensions.");
        }

        try
        {
            EffectImage output = effect.AppendPasses(context, plan, inputs);
            ValidateImage(output);
            if (output.Bounds != expected || output.ColorSemantic != semantic)
            {
                throw new InvalidOperationException("An effect must preserve its declared bounds and color semantic.");
            }

            return output;
        }
        catch
        {
            context.Close();
            throw;
        }
    }

    internal static void ValidateImage(EffectImage image)
    {
        ArgumentNullException.ThrowIfNull(image.Texture);
        if (image.Bounds.IsEmpty)
        {
            throw new ArgumentException("Effect images must have positive dimensions.", nameof(image));
        }

        if (!Enum.IsDefined(image.ColorSemantic))
        {
            throw new ArgumentException("Unknown effect color semantic.", nameof(image));
        }
    }

    private static ImageEffectsGraphContext GetContext(RenderGraphBuilder plan)
    {
        _ = RequireDevice(plan);
        return plan.GetOrAddExtension(static graph => new ImageEffectsGraphContext(graph));
    }

    private static IGraphicDevice RequireDevice(RenderGraphBuilder plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Device ?? throw new InvalidOperationException("Image-effect passes require a RenderGraph with a graphics device.");
    }

    private static void ValidateContext(RenderGraphBuilder plan, EffectPreparationContext context, EffectColorSemantic semantic)
    {
        _ = RequireDevice(plan);
        if (!plan.IsBuilding)
        {
            throw new InvalidOperationException("The render graph no longer accepts effect preparation.");
        }

        context.EnsureAlive();
        if (!ReferenceEquals(context.Device, plan.Device) || context.ColorSemantic != semantic)
        {
            throw new ArgumentException("The preparation context must use the graph's device and the inputs' color semantic.", nameof(context));
        }
    }
}
