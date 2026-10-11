using Lumyte.Graphics.ImageEffects;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Describes finite layer contents and post-composition effects.</summary>
public sealed record LayerOptions2D
{
    /// <summary>Gets the local input clip captured at BeginLayer.</summary>
    public required Rect2D ContentBounds { get; init; }

    /// <summary>Gets the effect graph applied to the completed layer.</summary>
    public ImageEffectGraph Effects { get; init; } = ImageEffectGraph.Source();

    /// <summary>Gets the finite opacity applied once after effects.</summary>
    public float Opacity { get; init; } = 1;

    /// <summary>Gets the optional local clip applied only to the effect output.</summary>
    public Rect2D? OutputClip { get; init; }
}
