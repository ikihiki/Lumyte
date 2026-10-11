using Lumyte.Graphics.Abstractions;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.ImageEffects;

/// <summary>Compiles immutable image operations into passes in a caller-owned render graph.</summary>
public sealed class ImageEffectCompiler
{
    private readonly IGraphicDevice _device;
    private readonly ImageEffectRenderers _renderers;

    /// <summary>Initializes a new instance of the <see cref="ImageEffectCompiler"/> class without allocating GPU resources.</summary>
    /// <param name="device">The borrowed graphics device.</param>
    /// <param name="renderers">The fixed, borrowed operation table.</param>
    public ImageEffectCompiler(IGraphicDevice device, ImageEffectRenderers renderers)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(renderers);
        _device = device;
        _renderers = renderers;
    }

    /// <summary>Appends the graph once; failure releases new resources and requires discarding the render graph.</summary>
    /// <param name="plan">The caller-owned render graph.</param>
    /// <param name="graph">The immutable effect graph.</param>
    /// <param name="source">The source image, already bound to the same render graph.</param>
    /// <returns>The owner of the result's fixed resources.</returns>
    public PreparedImageEffect AppendPasses(RenderGraphBuilder plan, ImageEffectGraph graph, EffectImage source)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(graph);
        ValidateImage(source);
        var context = new EffectPreparationContext(_device);
        var results = new Dictionary<ImageEffectNode, EffectImage>(ReferenceEqualityComparer.Instance);
        try
        {
            return new(Visit(graph.Root), context);
        }
        catch
        {
            context.Release();
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
                ColorMatrixEffectNode matrix => _renderers.ColorMatrix.AppendPasses(context, plan, matrix, Visit(matrix.Input)),
                GaussianBlurEffectNode blur => _renderers.GaussianBlur.AppendPasses(context, plan, blur, Visit(blur.Input)),
                OpacityEffectNode opacity => _renderers.Opacity.AppendPasses(context, plan, opacity, Visit(opacity.Input)),
                CompositeEffectNode composite => _renderers.Composite.AppendPasses(context, plan, composite, Visit(composite.Source), Visit(composite.Backdrop)),
                _ => throw new NotSupportedException("Unknown effect operation."),
            };
            ValidateImage(result);
            if (result.ColorSemantic != source.ColorSemantic)
            {
                throw new InvalidOperationException("An effect renderer cannot implicitly change the color semantic.");
            }

            if (results.Count >= 4096)
            {
                throw new NotSupportedException("An effect graph may contain at most 4096 distinct operations.");
            }

            results.Add(node, result);
            return result;
        }
    }

    /// <summary>Calculates a conservative finite source region without allocating GPU resources.</summary>
    /// <param name="graph">The immutable effect graph.</param>
    /// <param name="output">The desired output rectangle.</param>
    /// <returns>The union of all source regions required by the output.</returns>
    public EffectPixelRect GetRequiredSourceBounds(ImageEffectGraph graph, EffectPixelRect output)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (output.IsEmpty)
        {
            return output;
        }

        var required = new Dictionary<ImageEffectNode, EffectPixelRect>(ReferenceEqualityComparer.Instance);
        var order = new List<ImageEffectNode>();
        var visited = new HashSet<ImageEffectNode>(ReferenceEqualityComparer.Instance);
        Collect(graph.Root);
        required.Add(graph.Root, output);
        EffectPixelRect? result = null;
        for (int i = order.Count - 1; i >= 0; i--)
        {
            ImageEffectNode node = order[i];
            EffectPixelRect bounds = required[node];
            switch (node)
            {
                case SourceEffectNode:
                    result = result?.Union(bounds) ?? bounds;
                    break;
                case ColorMatrixEffectNode matrix:
                    Add(matrix.Input, bounds);
                    break;
                case OpacityEffectNode opacity:
                    Add(opacity.Input, bounds);
                    break;
                case GaussianBlurEffectNode blur:
                    Add(blur.Input, bounds.Outset(ImageEffectGraph.Radius(blur.SigmaX), ImageEffectGraph.Radius(blur.SigmaY)));
                    break;
                case CompositeEffectNode composite:
                    Add(composite.Source, bounds.OffsetInverse(composite.SourceOffsetX, composite.SourceOffsetY));
                    Add(composite.Backdrop, bounds);
                    break;
            }
        }

        return result ?? output;

        void Add(ImageEffectNode node, EffectPixelRect bounds) => required[node] = required.TryGetValue(node, out EffectPixelRect previous) ? previous.Union(bounds) : bounds;

        void Collect(ImageEffectNode node)
        {
            if (!visited.Add(node))
            {
                return;
            }

            if (visited.Count > 4096)
            {
                throw new NotSupportedException("An effect graph may contain at most 4096 distinct operations.");
            }

            switch (node)
            {
                case ColorMatrixEffectNode matrix:
                    Collect(matrix.Input);
                    break;
                case OpacityEffectNode opacity:
                    Collect(opacity.Input);
                    break;
                case GaussianBlurEffectNode blur:
                    Collect(blur.Input);
                    break;
                case CompositeEffectNode composite:
                    Collect(composite.Source);
                    Collect(composite.Backdrop);
                    break;
            }

            order.Add(node);
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
}
