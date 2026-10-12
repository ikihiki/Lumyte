using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.ImageEffects;

/// <summary>Implements GaussianBlur using full-resolution fragment passes and graph-owned textures.</summary>
public sealed class DefaultGaussianBlurEffectRenderer : IImageEffectRenderer<GaussianBlurEffect>
{
    /// <inheritdoc/>
    public EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan, GaussianBlurEffect effect, ReadOnlySpan<EffectImage> inputs)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(effect);
        if (inputs.Length != 1)
        {
            throw new ArgumentException("The effect requires one image.", nameof(inputs));
        }

        EffectImage input = inputs[0];

        context.EnsureAlive();
        ImageEffectCompiler.ValidateImage(input);
        EffectImage output = input;
        int radiusX = ImageEffectGraph.Radius(effect.SigmaX);
        int radiusY = ImageEffectGraph.Radius(effect.SigmaY);
        if (radiusX > 0)
        {
            output = EffectGpuPass.Append(context, plan, output, output, output.Bounds.Outset(radiusX, 0), new(1, effect.SigmaX, radiusX, 0));
        }

        if (radiusY > 0)
        {
            output = EffectGpuPass.Append(context, plan, output, output, output.Bounds.Outset(0, radiusY), new(2, effect.SigmaY, radiusY, 0));
        }

        return output;
    }
}
