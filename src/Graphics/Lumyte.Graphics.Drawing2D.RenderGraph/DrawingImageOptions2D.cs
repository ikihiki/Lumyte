using Lumyte.Graphics.ImageEffects;

namespace Lumyte.Graphics.Drawing2D.RenderGraph;

/// <summary>Describes the finite physical extent of a transparent drawing image.</summary>
public sealed record DrawingImageOptions2D
{
    /// <summary>Gets the positive image extent in the common device-pixel coordinate system.</summary>
    public required EffectPixelRect Bounds { get; init; }

    /// <summary>Gets the finite positive logical-to-physical scale, without rounding drawing coordinates.</summary>
    public float PixelScale { get; init; } = 1;
}
