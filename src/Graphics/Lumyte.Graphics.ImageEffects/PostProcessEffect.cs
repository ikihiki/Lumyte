using System.Numerics;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.ImageEffects;

/// <summary>Grades straight RGB while preserving premultiplied alpha, with optional scene-to-display tone mapping.</summary>
public sealed record PostProcessEffect : ImageEffectDefinition
{
    /// <summary>Initializes a new instance of the <see cref="PostProcessEffect"/> class.</summary>
    /// <param name="exposure">Finite exposure in stops with a finite float multiplier.</param>
    /// <param name="saturation">The finite nonnegative saturation multiplier.</param>
    /// <param name="vignette">The finite vignette strength in [0, 1].</param>
    /// <param name="toneMap">Whether to apply Reinhard mapping to scene-linear input.</param>
    public PostProcessEffect(float exposure = 0, float saturation = 1, float vignette = 0, bool toneMap = false)
    {
        if (!float.IsFinite(exposure) || !float.IsFinite(MathF.Pow(2, exposure)))
        {
            throw new ArgumentOutOfRangeException(nameof(exposure));
        }

        if (!float.IsFinite(saturation) || saturation < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(saturation));
        }

        if (!float.IsFinite(vignette) || vignette < 0 || vignette > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(vignette));
        }

        (Exposure, Saturation, Vignette, ToneMap) = (exposure, saturation, vignette, toneMap);
    }

    /// <summary>Gets the exposure in stops.</summary>
    public float Exposure { get; }

    /// <summary>Gets the saturation multiplier.</summary>
    public float Saturation { get; }

    /// <summary>Gets the vignette strength.</summary>
    public float Vignette { get; }

    /// <summary>Gets a value indicating whether scene-linear input is mapped to display-linear output.</summary>
    public bool ToneMap { get; }

    /// <inheritdoc/>
    public override int InputCount => 1;

    /// <inheritdoc/>
    public override EffectPixelRect GetOutputBounds(ReadOnlySpan<EffectPixelRect> inputs) => inputs[0];

    /// <inheritdoc/>
    public override EffectPixelRect GetRequiredInputBounds(int inputIndex, EffectPixelRect output) => output;

    /// <inheritdoc/>
    public override EffectColorSemantic GetOutputColorSemantic(EffectColorSemantic input)
        => ToneMap ? EffectColorSemantic.DisplayLinear : input;

    /// <inheritdoc/>
    public override EffectImage AppendPasses(EffectPreparationContext context, RenderGraphBuilder plan, ReadOnlySpan<EffectImage> inputs)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(plan);
        if (inputs.Length != 1)
        {
            throw new ArgumentException("Post processing requires one image.", nameof(inputs));
        }

        EffectImage input = inputs[0];
        if (ToneMap && input.ColorSemantic != EffectColorSemantic.SceneLinear)
        {
            throw new ArgumentException("Tone mapping requires scene-linear input.", nameof(inputs));
        }

        return EffectGpuPass.Append(context, plan, input, input, input.Bounds, new(6, Exposure, Saturation, Vignette), extra: new Vector4(ToneMap ? 1 : 0, 0, 0, 0), outputSemantic: GetOutputColorSemantic(input.ColorSemantic));
    }
}
