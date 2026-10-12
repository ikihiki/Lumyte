using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.ImageEffects;

/// <summary>Defines immutable parameters and region mappings for Composite.</summary>
public sealed record CompositeEffect : ImageEffectDefinition
{
    /// <summary>Initializes a new instance of the <see cref="CompositeEffect"/> class.</summary>
    /// <param name="mode">The image composition operation.</param>
    /// <param name="sourceOffsetX">The integer horizontal source translation.</param>
    /// <param name="sourceOffsetY">The integer vertical source translation.</param>
    public CompositeEffect(EffectCompositeMode mode, int sourceOffsetX = 0, int sourceOffsetY = 0)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        Mode = mode;
        SourceOffsetX = sourceOffsetX;
        SourceOffsetY = sourceOffsetY;
    }

    /// <summary>Gets the image composition operation.</summary>
    public EffectCompositeMode Mode { get; }

    /// <summary>Gets the horizontal source translation.</summary>
    public int SourceOffsetX { get; }

    /// <summary>Gets the vertical source translation.</summary>
    public int SourceOffsetY { get; }

    /// <inheritdoc/>
    public override int InputCount => 2;

    /// <inheritdoc/>
    public override EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs) => inputs[0].Offset(SourceOffsetX, SourceOffsetY).Union(inputs[1]);

    /// <inheritdoc/>
    public override EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output) => inputIndex switch { 0 => output.OffsetInverse(SourceOffsetX, SourceOffsetY), 1 => output, _ => throw new ArgumentOutOfRangeException(nameof(inputIndex)) };

    /// <inheritdoc/>
    public override EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan, ReadOnlySpan<EffectImage> inputs)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(plan);
        if (inputs.Length != 2)
        {
            throw new ArgumentException("Composite requires two images.", nameof(inputs));
        }

        EffectImage source = inputs[0];
        EffectImage backdrop = inputs[1];

        if (!Enum.IsDefined(Mode))
        {
            throw new ArgumentOutOfRangeException(nameof(inputs));
        }

        EffectPixelRect bounds = source.Bounds.Offset(SourceOffsetX, SourceOffsetY).Union(backdrop.Bounds);
        return EffectGpuPass.Append(context, plan, source, backdrop, bounds, new(Mode == EffectCompositeMode.SrcOver ? 4 : 5, 0, 0, 0), offsetX: SourceOffsetX, offsetY: SourceOffsetY);
    }
}
