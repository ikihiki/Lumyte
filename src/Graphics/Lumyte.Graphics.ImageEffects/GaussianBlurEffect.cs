using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.ImageEffects;

/// <summary>Defines immutable parameters and region mappings for GaussianBlur.</summary>
public sealed record GaussianBlurEffect : ImageEffectDefinition
{
    /// <summary>Initializes a new instance of the <see cref="GaussianBlurEffect"/> class.</summary>
    /// <param name="sigmaX">The finite nonnegative horizontal device-pixel sigma.</param>
    /// <param name="sigmaY">The finite nonnegative vertical device-pixel sigma.</param>
    public GaussianBlurEffect(float sigmaX, float sigmaY)
    {
        _ = ImageEffectGraph.Radius(sigmaX);
        _ = ImageEffectGraph.Radius(sigmaY);
        SigmaX = sigmaX;
        SigmaY = sigmaY;
    }

    /// <summary>Gets the horizontal device-pixel sigma.</summary>
    public float SigmaX { get; }

    /// <summary>Gets the vertical device-pixel sigma.</summary>
    public float SigmaY { get; }

    /// <inheritdoc/>
    public override int InputCount => 1;

    /// <inheritdoc/>
    public override EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs) => inputs[0].Outset(ImageEffectGraph.Radius(SigmaX), ImageEffectGraph.Radius(SigmaY));

    /// <inheritdoc/>
    public override EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output) => output.Outset(ImageEffectGraph.Radius(SigmaX), ImageEffectGraph.Radius(SigmaY));

    /// <inheritdoc/>
    public override EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan, ReadOnlySpan<EffectImage> inputs)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(plan);
        if (inputs.Length != 1)
        {
            throw new ArgumentException("The effect requires one image.", nameof(inputs));
        }

        EffectImage input = inputs[0];

        context.EnsureAlive();
        ImageEffectRenderGraphExtensions.ValidateImage(input);
        EffectImage output = input;
        int radiusX = ImageEffectGraph.Radius(this.SigmaX);
        int radiusY = ImageEffectGraph.Radius(this.SigmaY);
        if (radiusX > 0)
        {
            output = EffectGpuPass.Append(context, plan, output, output, output.Bounds.Outset(radiusX, 0), new(1, this.SigmaX, radiusX, 0));
        }

        if (radiusY > 0)
        {
            output = EffectGpuPass.Append(context, plan, output, output, output.Bounds.Outset(0, radiusY), new(2, this.SigmaY, radiusY, 0));
        }

        return output;
    }
}
